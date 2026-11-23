using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「快捷劝降」（2026-11-15 新增，修复记录第 338 节）。
///
/// 客户端链路（已用运行时 dump 确认）：
///   UMG.VirCapture.umg_vircapture_gacha:DoClickGacha
///     → 客户端自检好伙伴券数量（tbConfig.tbCost × 1 / × 10）
///     → VirCapture.DoGachaMonster(bTen) → me:CallGS("VirCapture_GachaMonster", {"bTen":bool})
///   回包 → s2c.tbCmds.VirCapture_GachaMonster(rsp)
///     → 有 sErr 就 UI.ShowTip(sErr)
///     → 否则 UI.GetUI("VirCaptureGacha"):OnReceiveUpdate(rsp) → ShowResult(rsp)
///     → Item.Gain(rsp.tbAwardList, ...) 播获得动画
/// 所以响应必须是 {"tbAwardList": [ [g,d,p,l,count], ... ]} —— **扁平一层**，
/// 每个元素是一件 GDPL+数量（别再套 group，第 339 节踩过）。
///
/// 配置口径：
///   Resources/dlc/vircapture/gachapool.json → Cost [5,25,2,6,1]（好伙伴券 ×1/次）、PoolFile "pool01"
///   Resources/dlc/vircapture/pool/pool01.json → 40 个怪物卡 GDPL 18-1-P-1，Weight 全 100
///   monster_card.json 的 Color：3/4 = 一般、5 = 厉害、6 = 强力
/// 概率：按界面文案（一般 80% / 厉害 15% / 强力 5%）先抽档，再在档内等权随机。
/// 结算：与 VirCaptureLevel_SaveCapture 一致 —— AddMonsterCardItem → 计数 → 加经验升级。
/// </summary>
[CallGSApi("VirCapture_GachaMonster")]
public class VirCapture_GachaMonster : ICallGSHandler
{
	private sealed class GachaParam
	{
		[JsonPropertyName("bTen")]
		public bool BTen { get; set; }
	}

	private sealed class PoolEntry
	{
		public uint G { get; init; }

		public uint D { get; init; }

		public uint P { get; init; }

		public uint L { get; init; }

		public int Weight { get; init; }
	}

	private const uint Gid = 128u;

	private const uint CurLevelSid = 3u;

	private const uint CurExpSid = 2u;

	private const uint BagNumSid = 5u;

	private const uint DailyExpSid = 8u;

	private const uint ColorMaxStartSid = 11u;

	private const uint RikiGid = 135u;

	/// <summary>好伙伴券 5-25-2-6（supplies.vircapture.token2）。</summary>
	private const uint CostG = 5u;

	private const uint CostD = 25u;

	private const uint CostP = 2u;

	private const uint CostL = 6u;

	/// <summary>单次消耗数量（gachapool.Cost 第 5 位）。</summary>
	private const uint CostPerPull = 1u;

	private const int TenCount = 10;

	private static readonly Lock PoolLock = new Lock();

	private static List<PoolEntry> _pool;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		GachaParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<GachaParam>(param);
			}
		}
		catch
		{
		}
		int pulls = ((req != null && req.BTen) ? TenCount : 1);
		uint need = CostPerPull * (uint)pulls;

		// 扣好伙伴券（客户端自己也查过一遍，这里做服务端权威校验）。
		BaseGameItemInfo costItem = player.InventoryManager.GetNormalItemGDPL((ItemTypeEnum)CostG, CostD, CostP, CostL);
		if (costItem == null || costItem.ItemCount < need)
		{
			await Send(connection, "{\"sErr\":\"error.gold_not_enough\"}");
			return;
		}

		List<PoolEntry> pool = GetPool();
		if (pool.Count == 0)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		costItem.ItemCount -= need;
		sync.Items.Add(costItem.ToProto());

		// 抽卡（先按界面概率抽档，再在档内按 Weight 随机）。
		List<PoolEntry> results = new List<PoolEntry>(pulls);
		for (int i = 0; i < pulls; i++)
		{
			results.Add(Pick(pool));
		}

		// 注意：tbAwardList 必须是**扁平一层**的 GDPL+数量 列表。
		// GainItem.OnOpen 直接 ipairs(tbAwardList)，每项按 [1]=G [2]=D [3]=P [4]=L [5]=N 取，
		// 若外面再套一层 group（[[[...]]]），string.format("%d-%d-%d-%d", 表, ...) 会抛错被吞，
		// 结果界面里只剩一个空卡槽（第 339 节）。
		JsonArray awardList = new JsonArray();
		foreach (PoolEntry pick in results)
		{
			BaseGameItemInfo granted = await player.InventoryManager.AddMonsterCardItem(pick.D, pick.P, pick.L, sendPacket: false);
			if (granted == null)
			{
				continue;
			}
			sync.Items.Add(granted.ToProto());
			SyncCounters(player, granted.TemplateId, sync);
			ApplyCaptureExp(player, granted.TemplateId, sync);
			JsonArray one = new JsonArray
			{
				(int)pick.G,
				(int)pick.D,
				(int)pick.P,
				(int)pick.L,
				1
			};
			awardList.Add(one);
		}
		// 开拓目标：201「累计劝降伙伴」+ 215「累计快捷劝降」（十连才算一次快捷）。
		VirCaptureMissionProgress.Add(player, VirCaptureMissionProgress.KeyGacha, pulls);
		if (pulls > 1)
		{
			VirCaptureMissionProgress.Add(player, VirCaptureMissionProgress.KeyQuickGacha, 1L);
		}
		VirCaptureMissionProgress.SyncAll(player, sync);

		JsonObject rsp = new JsonObject
		{
			["tbAwardList"] = awardList,
			["nBagNum"] = player.InventoryManager.InventoryData.Items.Values.Count((BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD)
		};
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "VirCapture_GachaMonster", rsp.ToJsonString(), sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCapture_GachaMonster", arg);
	}

	/// <summary>档位：0 = 一般（Color 3/4，80%）、1 = 厉害（Color 5，15%）、2 = 强力（Color 6，5%）。</summary>
	private static int TierOf(uint particular)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl(18u, 1u, particular, 1u);
		if (GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel cfg))
		{
			if (cfg.Color >= 6)
			{
				return 2;
			}
			if (cfg.Color == 5)
			{
				return 1;
			}
		}
		return 0;
	}

	private static PoolEntry Pick(List<PoolEntry> pool)
	{
		int roll = Random.Shared.Next(100);
		int tier = ((roll < 80) ? 0 : ((roll < 95) ? 1 : 2));
		List<PoolEntry> candidates = pool.Where((PoolEntry e) => TierOf(e.P) == tier).ToList();
		if (candidates.Count == 0)
		{
			candidates = pool;
		}
		int total = candidates.Sum((PoolEntry e) => e.Weight);
		if (total <= 0)
		{
			return candidates[Random.Shared.Next(candidates.Count)];
		}
		int r = Random.Shared.Next(total);
		foreach (PoolEntry e in candidates)
		{
			r -= e.Weight;
			if (r < 0)
			{
				return e;
			}
		}
		return candidates[candidates.Count - 1];
	}

	private static List<PoolEntry> GetPool()
	{
		using (PoolLock.EnterScope())
		{
			if (_pool != null)
			{
				return _pool;
			}
			_pool = new List<PoolEntry>();
			string path = Path.Combine(ConfigManager.Config.Path.ResourcePath, "dlc", "vircapture", "pool", "pool01.json");
			if (!File.Exists(path))
			{
				return _pool;
			}
			JArray arr = JArray.Parse(File.ReadAllText(path));
			foreach (JToken token in arr)
			{
				List<uint> gdpl = token["GDPL"]?.ToObject<List<uint>>();
				if (gdpl != null && gdpl.Count >= 4)
				{
					uint weight = token["Weight"]?.Value<uint>() ?? 100u;
					_pool.Add(new PoolEntry
					{
						G = gdpl[0],
						D = gdpl[1],
						P = gdpl[2],
						L = gdpl[3],
						Weight = (int)Math.Max(1u, weight)
					});
				}
			}
			return _pool;
		}
	}

	private static void SyncCounters(PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
	{
		uint bagNum = (uint)player.InventoryManager.InventoryData.Items.Values.Count((BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD);
		SetUnsigned(player, BagNumSid, bagNum, sync);
		if (GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel card) && card.RikiId != 0)
		{
			uint colorSid = ColorMaxStartSid + Math.Max(0u, card.Color - 1);
			uint colorCount = (player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == colorSid)?.Val ?? 0) + 1;
			SetUnsigned(player, colorSid, colorCount, sync);
			PlayerAttr rikiAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == RikiGid && x.Sid == card.RikiId);
			if (rikiAttr == null)
			{
				rikiAttr = new PlayerAttr
				{
					Gid = RikiGid,
					Sid = card.RikiId,
					Val = 0u
				};
				player.Data.Attrs.Add(rikiAttr);
			}
			rikiAttr.Val++;
			sync.Custom[player.ToPackedAttrKey(RikiGid, card.RikiId)] = rikiAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(RikiGid, card.RikiId)] = rikiAttr.Val;
		}
	}

	/// <summary>与 VirCaptureLevel_SaveCapture.ApplyCaptureExp 同口径：卡经验 × 本级 ExpUp 放大后累加、够就升级。</summary>
	private static void ApplyCaptureExp(PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
	{
		if (!GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel card) || card.Exp == 0)
		{
			return;
		}
		PlayerAttr levelAttr = GetOrCreate(player, CurLevelSid);
		PlayerAttr expAttr = GetOrCreate(player, CurExpSid);
		PlayerAttr dailyAttr = GetOrCreate(player, DailyExpSid);
		uint maxLevel = ((GameData.VirCaptureLevelListData.Count == 0) ? 1u : GameData.VirCaptureLevelListData.Keys.Max());
		uint curLevel = Math.Max(1u, levelAttr.Val);
		if (curLevel >= maxLevel)
		{
			return;
		}
		uint gain = card.Exp;
		if (GameData.VirCaptureLevelListData.TryGetValue(curLevel, out VirCaptureLevelListExcel levelCfg) && levelCfg.ExpUp > 1.0)
		{
			gain = (uint)Math.Floor((double)gain * levelCfg.ExpUp);
		}
		uint maxExp = ResolveCurrentActMaxExp(player);
		if (maxExp != 0 && dailyAttr.Val >= maxExp)
		{
			return;
		}
		uint applied = gain;
		if (maxExp != 0)
		{
			applied = Math.Min(applied, maxExp - dailyAttr.Val);
		}
		if (applied == 0)
		{
			return;
		}
		dailyAttr.Val += applied;
		SetUnsigned(player, DailyExpSid, dailyAttr.Val, sync);
		uint pool = expAttr.Val + applied;
		while (GameData.VirCaptureLevelListData.TryGetValue(curLevel, out VirCaptureLevelListExcel curCfg) && curLevel < maxLevel)
		{
			if (pool < curCfg.Exp)
			{
				break;
			}
			pool -= curCfg.Exp;
			curLevel++;
		}
		levelAttr.Val = curLevel;
		expAttr.Val = ((curLevel < maxLevel) ? pool : (GameData.VirCaptureLevelListData.GetValueOrDefault(maxLevel)?.Exp ?? pool));
		SetUnsigned(player, CurLevelSid, levelAttr.Val, sync);
		SetUnsigned(player, CurExpSid, expAttr.Val, sync);
	}

	private static uint ResolveCurrentActMaxExp(PlayerInstance player)
	{
		uint actId = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == 1u)?.Val ?? 0u;
		if (actId != 0 && GameData.VirCaptureTimeData.TryGetValue(actId, out VirCaptureTimeExcel act))
		{
			return act.MaxExp;
		}
		return 0u;
	}

	private static PlayerAttr GetOrCreate(PlayerInstance player, uint sid)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == sid);
		if (attr != null)
		{
			return attr;
		}
		attr = new PlayerAttr
		{
			Gid = Gid,
			Sid = sid,
			Val = 0u
		};
		player.Data.Attrs.Add(attr);
		return attr;
	}

	private static void SetUnsigned(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		GetOrCreate(player, sid).Val = value;
		sync.Custom[player.ToPackedAttrKey(Gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(Gid, sid)] = value;
	}
}
