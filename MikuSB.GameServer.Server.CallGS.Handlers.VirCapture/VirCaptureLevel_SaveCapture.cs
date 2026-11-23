using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SaveCapture")]
public class VirCaptureLevel_SaveCapture : ICallGSHandler
{
	private const uint VirCaptureGroupId = 128u;

	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureSaveCapture");

	private const uint CurExpSid = 2u;

	private const uint CurLevelSid = 3u;

	private const uint BagNumSid = 5u;

	private const uint DailyExpSid = 8u;

	private const uint ColorMaxStartSid = 11u;

	private const uint RikiGroupId = 135u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureSaveCaptureParam req = JsonSerializer.Deserialize<VirCaptureSaveCaptureParam>(param);
		if (req == null || req.LevelId == 0 || req.RegionId == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint prevState = VirCaptureStateHelper.GetPointState(player, (uint)req.LevelId, (uint)req.RegionId);
		VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, 2u, sync);
		if (!GameData.VirCaptureCaptureRegionData.TryGetValue((uint)req.LevelId, out VirCaptureCaptureRegionExcel captureRegion))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		// 第 500 节：蓝图岛（CaptureRegion.LevelRegionName = newstar_building01）不是「抓怪物卡」的图，
		// 它的 region_info.json 里 PalType / Rewards1 全是空字符串，没有任何捕获奖励。
		// 原来走通用路径会一路撞到 error.BadParam，客户端在「劝降」时弹「请求参数错误」。
		// 这里单独走岛内分支：只落点位状态 + 回成功，不发道具。
		if (IslandCaptureMap(captureRegion.LevelRegionName))
		{
			int advanced = VirCaptureBuildTaskState.AdvanceByRegion(player, (uint)req.RegionId, sync);
			_ = advanced;
			// 第 505 节：岛上宝箱（RegionType 5 / PalGiftBox*）的奖励在 region_info.json 的 Rewards2，
			// 旧实现整段早退、回空 tbGDPL，所以「开宝箱什么都没有」。现在按点位奖励真实发放，
			// 已经完成过的点位（prevState == 2）不再重复给，避免刷箱。
			List<List<uint>> islandRewards = ((prevState == 2u) ? new List<List<uint>>() : VirCaptureCaptureRewardResolver.ResolveIslandRewards(captureRegion.LevelRegionName!, (uint)req.RegionId));
			JsonArray gdplFlat = new JsonArray();
			if (islandRewards.Count > 0)
			{
				// 第 506 节：宝箱奖励不能直接交给 RewardManager。
				// 4-1-6 这一段是「混合壳」（见 CommandFishBait 注释）：鱼饵/蓝图币是 cashbox，
				// 会正确换算成货币；但经验盒 [4,1,6,8] 的 LuaType=vircapture_exp_box，
				// RewardManager 会把它当普通道具塞进 InventoryData.Items —— 客户端根本不认这个壳，
				// 结果就是「开箱什么都没有」（实测 1:77 鱼饵 +2 到账，经验盒 200 个查不到）。
				int granted = await GrantIslandRewards(player, islandRewards, sync);
				foreach (List<uint> row in islandRewards)
				{
					foreach (uint value in row)
					{
						gdplFlat.Add((int)value);
					}
				}
				Log.Info($"[VirCapture] island region {req.RegionId} rewards={islandRewards.Count} granted={granted} uid={player.Uid} playerExp={GetOrCreateVirCaptureAttr(player, 2u).Val} lv={GetOrCreateVirCaptureAttr(player, 3u).Val}");
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			// 第 508 节：这里**不能**回 "nAddItemId": 0。
			// 客户端 VirCapture.lua 的回包处理是 `if R.nAddItemId then ... end`，
			// 而 Lua 里数字 0 是真值，于是它会拿 0 去 me:GetMonsterCard(0)（返回 nil），
			// 再索引卡片属性，抛 "attempt to index a nil value (local 'pItem')"（VirCapture.lua:2566）。
			// 整个回包函数就此中断，后面的 PalClient.CatchPalRsp / UpdatePointState 都没执行，
			// 表现就是「劝降完了怪物还在、还能再点一次劝降」（第 508 节的 bug）。
			// 岛内没有怪物卡，直接省略该字段，客户端就会跳过卡片构造，正常走完清理流程。
			JsonObject islandRsp = new JsonObject
			{
				["nLevelID"] = req.LevelId,
				["nRegionId"] = req.RegionId,
				["tbGDPL"] = gdplFlat
			};
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", islandRsp.ToJsonString(), sync);
			return;
		}
		List<uint> rewardGdpl = VirCaptureCaptureRewardResolver.ResolveGdpl(captureRegion, (uint)req.RegionId);
		if (rewardGdpl == null || rewardGdpl.Count < 4 || rewardGdpl[0] != 18)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", "{\"sErr\":\"error.BadParam\"}", sync);
			return;
		}
		BaseGameItemInfo grantedItem = await player.InventoryManager.AddMonsterCardItem(rewardGdpl[1], rewardGdpl[2], rewardGdpl[3], sendPacket: false);
		if (grantedItem == null)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", "{\"sErr\":\"error.BadParam\"}", sync);
			return;
		}
		sync.Items.Add(grantedItem.ToProto());
		SyncVirCaptureCounters(player, grantedItem.TemplateId, sync);
		ApplyCaptureExp(player, grantedItem.TemplateId, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		JsonObject jsonObject = new JsonObject
		{
			["nLevelID"] = req.LevelId,
			["nRegionId"] = req.RegionId,
			["nAddItemId"] = grantedItem.UniqueId
		};
		JsonNode[] array = rewardGdpl.Select((uint x) => JsonValue.Create((int)x)).ToArray();
		jsonObject["tbGDPL"] = new JsonArray((ReadOnlySpan<JsonNode?>)array);
		JsonObject response = jsonObject;
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveCapture", response.ToJsonString(), sync);
	}

	/// <summary>蓝图岛（新星开拓建造图）没有怪物卡奖励，捕获/劝降只落点位状态。</summary>
	private static bool IslandCaptureMap(string? levelRegionName)
	{
		return string.Equals(levelRegionName, "newstar_building01", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 岛屿宝箱奖励的实际发放（第 506 节）。
	/// 4-1-6 段是混合壳：Level 8/11/12/13 是新星开拓经验盒（客户端不认壳道具，必须换成经验），
	/// 其余（7 好伙伴券、9/10/40~46 鱼饵、14 蓝图币）都由 RewardManager 的 cashbox 分支
	/// 通过 Player.AddCurrency 正确落到货币属性上，这里只做经验盒的接管。
	/// </summary>
	private static async Task<int> GrantIslandRewards(PlayerInstance player, List<List<uint>> rows, NtfSyncPlayer sync)
	{
		int granted = 0;
		foreach (List<uint> row in rows)
		{
			if (row.Count < 5 || row[4] == 0)
			{
				continue;
			}
			uint genre = row[0];
			uint detail = row[1];
			uint particular = row[2];
			uint level = row[3];
			uint count = row[4];
			if (genre == 4u && detail == 1u && particular == 6u && (level == 8u || level == 11u || level == 12u || level == 13u))
			{
				AddVirCaptureExpValue(player, count, sync);
				Log.Info($"[VirCapture] expbox {genre},{detail},{particular},{level} x{count} -> exp +{count} (now {GetOrCreateVirCaptureAttr(player, 2u).Val})");
				granted++;
				continue;
			}
			if (await player.RewardManager.GrantRewardAsync(row, sync))
			{
				granted++;
			}
		}
		return granted;
	}

	/// <summary>按经验值推进新星开拓等级（与 ApplyCaptureExp 同一套 128/2、128/3 口径）。</summary>
	private static void AddVirCaptureExpValue(PlayerInstance player, uint exp, NtfSyncPlayer sync)
	{
		if (exp == 0)
		{
			return;
		}
		PlayerAttr levelAttr = GetOrCreateVirCaptureAttr(player, 3u);
		PlayerAttr expAttr = GetOrCreateVirCaptureAttr(player, 2u);
		uint maxLevel = ((GameData.VirCaptureLevelListData.Count == 0) ? 1u : GameData.VirCaptureLevelListData.Keys.Max());
		uint curLevel = Math.Max(1u, levelAttr.Val);
		if (curLevel >= maxLevel)
		{
			return;
		}
		uint rest = expAttr.Val + exp;
		VirCaptureLevelListExcel levelRow;
		while (GameData.VirCaptureLevelListData.TryGetValue(curLevel, out levelRow) && curLevel < maxLevel)
		{
			if (rest < levelRow.Exp)
			{
				break;
			}
			rest -= levelRow.Exp;
			curLevel++;
		}
		levelAttr.Val = curLevel;
		expAttr.Val = rest;
		SyncVirCaptureAttr(player, 3u, levelAttr.Val, sync);
		SyncVirCaptureAttr(player, 2u, expAttr.Val, sync);
	}

	private static void SyncVirCaptureCounters(PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
	{
		uint value = (uint)Enumerable.Count(player.InventoryManager.InventoryData.Items.Values, (BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD);
		VirCaptureStateHelper.SetUnsignedAttr(player, 5u, value, sync);
		if (GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel monsterCard) && monsterCard.RikiId != 0)
		{
			uint colorSid = 11 + Math.Max(0u, monsterCard.Color - 1);
			uint value2 = (player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == colorSid)?.Val ?? 0) + 1;
			VirCaptureStateHelper.SetUnsignedAttr(player, colorSid, value2, sync);
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 135 && x.Sid == monsterCard.RikiId);
			if (playerAttr == null)
			{
				playerAttr = new PlayerAttr
				{
					Gid = 135u,
					Sid = monsterCard.RikiId,
					Val = 0u
				};
				player.Data.Attrs.Add(playerAttr);
			}
			playerAttr.Val++;
			sync.Custom[player.ToPackedAttrKey(135u, monsterCard.RikiId)] = playerAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(135u, monsterCard.RikiId)] = playerAttr.Val;
		}
	}

	private static void ApplyCaptureExp(PlayerInstance player, ulong templateId, NtfSyncPlayer sync)
	{
		if (!GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel value) || value.Exp == 0)
		{
			return;
		}
		PlayerAttr orCreateVirCaptureAttr = GetOrCreateVirCaptureAttr(player, 3u);
		PlayerAttr orCreateVirCaptureAttr2 = GetOrCreateVirCaptureAttr(player, 2u);
		PlayerAttr orCreateVirCaptureAttr3 = GetOrCreateVirCaptureAttr(player, 8u);
		uint num = ((GameData.VirCaptureLevelListData.Count == 0) ? 1u : GameData.VirCaptureLevelListData.Keys.Max());
		uint num2 = Math.Max(1u, orCreateVirCaptureAttr.Val);
		if (num2 >= num)
		{
			return;
		}
		uint num3 = value.Exp;
		if (GameData.VirCaptureLevelListData.TryGetValue(num2, out VirCaptureLevelListExcel value2) && value2.ExpUp > 1.0)
		{
			num3 = (uint)Math.Floor((double)num3 * value2.ExpUp);
		}
		uint num4 = ResolveCurrentAct(player)?.MaxExp ?? 0;
		if (num4 != 0 && orCreateVirCaptureAttr3.Val >= num4)
		{
			return;
		}
		uint num5 = num3;
		if (num4 != 0)
		{
			num5 = Math.Min(num5, num4 - orCreateVirCaptureAttr3.Val);
		}
		if (num5 == 0)
		{
			return;
		}
		orCreateVirCaptureAttr3.Val += num5;
		SyncVirCaptureAttr(player, 8u, orCreateVirCaptureAttr3.Val, sync);
		uint num6 = orCreateVirCaptureAttr2.Val + num5;
		VirCaptureLevelListExcel value3;
		for (; GameData.VirCaptureLevelListData.TryGetValue(num2, out value3) && num2 < num; num2++)
		{
			if (num6 < value3.Exp)
			{
				break;
			}
			num6 -= value3.Exp;
		}
		orCreateVirCaptureAttr.Val = num2;
		orCreateVirCaptureAttr2.Val = ((num2 < num) ? num6 : (GameData.VirCaptureLevelListData.GetValueOrDefault(num)?.Exp ?? num6));
		SyncVirCaptureAttr(player, 3u, orCreateVirCaptureAttr.Val, sync);
		SyncVirCaptureAttr(player, 2u, orCreateVirCaptureAttr2.Val, sync);
	}

	private static PlayerAttr GetOrCreateVirCaptureAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 128u,
			Sid = sid,
			Val = 0u
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncVirCaptureAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		sync.Custom[player.ToPackedAttrKey(128u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(128u, sid)] = value;
	}

	private static VirCaptureTimeExcel? ResolveCurrentAct(PlayerInstance player)
	{
		uint num = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == 1)?.Val ?? 0;
		if (num != 0 && GameData.VirCaptureTimeData.TryGetValue(num, out VirCaptureTimeExcel value))
		{
			return value;
		}
		DateTime now = DateTime.Now;
		return (from x in GameData.VirCaptureTimeData.Values.Select((VirCaptureTimeExcel x) => new
			{
				Config = x,
				Start = ParseConfigTime(x.StartTime),
				End = ParseConfigTime(x.EndTime)
			}).Where(x =>
			{
				int result;
				if (x.Start.HasValue && x.End.HasValue && x.Start <= now)
				{
					DateTime value2 = now;
					DateTime? end = x.End;
					result = ((value2 < end) ? 1 : 0);
				}
				else
				{
					result = 0;
				}
				return (byte)result != 0;
			})
			orderby x.Start
			select x.Config).FirstOrDefault();
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}
}
