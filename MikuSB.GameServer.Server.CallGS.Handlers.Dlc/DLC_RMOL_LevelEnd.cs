using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;

using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 448/449 节：第 32 期「绘梦沙盘 / 沙盒推演」通关结算。
///
/// 客户端（_Mod/RMOL/DLC_RMOL.lua 的 Level:End）发
/// {"Level":10001,"IsVictory":true,"Stars":[],"tbInternalInfo":{...}}，
/// 服务端原先没有 handler（日志 "No handler for CallGS API: DLC_RMOL_LevelEnd"），
/// 不回包 → 客户端结果界面卡住，且关卡解锁标记没落库，第二关一直是锁的。
///
/// 两个客户端读取点决定了这里要写什么：
/// 1) 解锁：Level:IsPassed() = me:GetAttribute(DLC_RMOL.LevelGID = 173, 关卡ID) 的 bit31；
/// 2) 成长：DLC_RMOL.tbSession[1]:CurGrowLevel() 把
///        me:GetAttribute(DLC_RMOL.AttrGID = 172, ID*SessionRange + GrowExpOffset)
///    当成「累计经验」再按 config/grow.json 的 NextLevelEXP 逐级扣减算等级，
///    所以经验必须写 (172, 1*100+0=100)，等级由客户端自己推。
///
/// 回包（s2c.tbCmds["DLC_RMOL_LevelEnd"]）：
///   if tb.Err then UI.ShowTip(tb.Err) end
///   EventSystem.Trigger("DLC_RMOL_LevelEnd_Return", tb.Level, tb.Awards, tb.ExpGain)
/// Awards 用「五行数组的列表」[[类型,?,ID,?,数量], ...]，与客户端配置
/// Resources/_Mod/RMOL/def/04_levels.json 的 Rewards_Win 同构（也是全服通用领奖格式）。
/// </summary>
[CallGSApi("DLC_RMOL_LevelEnd")]
public class DLC_RMOL_LevelEnd : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_LevelEnd");

	/// <summary>DLC_RMOL.LevelGID：关卡通关标记所在的属性组。</summary>
	private const uint LevelAttrGid = 173u;

	/// <summary>bit31 被客户端 IsPassed 当作「已通关」。</summary>
	private const uint PassedBit = 0x80000000u;

	/// <summary>DLC_RMOL.AttrGID：成长经验 / 成长奖励领取位图（客户端常量 172）。</summary>
	private const uint GrowAttrGid = 172u;

	/// <summary>DLC_RMOL.SessionRange / GrowExpOffset：sid = 期数 * 100 + 0。</summary>
	private const int SessionRange = 100;

	private const int GrowExpOffset = 0;

	/// <summary>第 32 期绘梦沙盘只开了 1 期（config/main.json 里的 SessionID=1）。</summary>
	private const int CurrentSessionId = 1;

	/// <summary>
	/// 第 452 节：打完一关 +300 繁荣经验（与「绘梦档案」一个任务 +800 配套）。
	/// 战败仍给 100，避免失败局一点不涨；若要与胜利同额把 LoseExp 改成 300 即可。
	/// </summary>
	private const int WinExp = 300;

	private const int LoseExp = 100;

	private static readonly object RewardLock = new object();

	private static readonly Dictionary<int, LevelRewardRow> RewardCache = new Dictionary<int, LevelRewardRow>();

	private static DateTime rewardCacheStamp = DateTime.MinValue;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int level = ReadInt(request["Level"], request["level"], request["LevelId"], request["LevelID"]);
		bool victory = ReadBool(request["IsVictory"]) || ReadBool(request["isVictory"]);

		PlayerInstance? player = connection.Player;
		int uid = player?.Uid ?? 0;

		JsonArray awards = new JsonArray();
		int expGain = victory ? WinExp : LoseExp;
		int growLevel = 0;
		uint levelAttrVal = 0u;
		uint expAttrVal = 0u;

		if (player != null && level > 0)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();

			// ---- 1. 通关标记（bit31）+ 星级位（bit1~bit3）+ 首次通关判断 ----
			// 客户端 Level:StarRecord 读 bit1/bit2/bit3 判断三颗星，Level:IsPassed 读 bit31。
			PlayerAttr levelAttr = player.Attributes.GetOrCreate(LevelAttrGid, (uint)level);
			bool firstPass = (levelAttr.Val & PassedBit) == 0;
			uint starBits = ReadStarBits(request["Stars"]);
			uint before = levelAttr.Val;
			if (victory)
			{
				levelAttr.Val |= PassedBit;
			}
			levelAttr.Val |= starBits;
			if (levelAttr.Val != before)
			{
				levelAttrVal = levelAttr.Val;
				player.Attributes.SyncTo(sync, levelAttr);
			}

			// ---- 2. 发奖（配置取自客户端 def/04_levels.json） ----
			LevelRewardRow row = GetRewardRow(level);
			List<IReadOnlyList<uint>> rows = victory
				? (firstPass && row.First.Count > 0 ? row.First : row.Win)
				: row.Lose;
			if (rows.Count > 0)
			{
				// 与全服结算同一条发奖通道：真正进背包 + 返回 [[G,D,P,L,N], ...] 给客户端展示。
				awards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
			}

			// ---- 3. 成长经验（累计值，客户端自己按 grow.json 换算等级） ----
			int expSid = CurrentSessionId * SessionRange + GrowExpOffset;
			PlayerAttr expAttr = player.Attributes.GetOrCreate(GrowAttrGid, (uint)expSid);
			expAttr.Val = (uint)Math.Min(uint.MaxValue, (long)expAttr.Val + expGain);
			expAttrVal = expAttr.Val;
			player.Attributes.SyncTo(sync, expAttr);

			// ---- 4. 绘梦档案任务进度（属性组 7：胜场 / 指定关卡 / 棋盘星数）----
			// 之前没人写这一组，繁荣嘉奖页签恒为 0/1，按钮一直是灰的。
			RmolTaskService.OnLevelEnd(player, level, victory, sync);

			DatabaseHelper.SaveDatabaseType(player.Data);
			if (player.InventoryManager != null)
			{
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}

			growLevel = CalcGrowLevel(expAttrVal);
			Log.Info($"uid={uid} rmol levelend level={level} victory={victory} first={firstPass} " +
				$"awards={awards.Count} exp+{expGain} totalExp={expAttrVal} growLv={growLevel} " +
				$"levelAttr={levelAttrVal}");

			JsonObject rsp = new JsonObject
			{
				["Level"] = level,
				["Awards"] = awards,
				["ExpGain"] = expGain
			};
			await CallGSRouter.SendScript(connection, "DLC_RMOL_LevelEnd", rsp.ToJsonString(), sync);
			return;
		}

		Log.Info($"uid={uid} rmol levelend level={level} victory={victory} (no player)");
		JsonObject fallback = new JsonObject
		{
			["Level"] = level,
			["Awards"] = awards,
			["ExpGain"] = 0
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_LevelEnd", fallback.ToJsonString());
	}

	/// <summary>
	/// 与服务端无关的纯展示计算：按 config/grow.json 的 NextLevelEXP 推等级，
	/// 只用于日志核对（客户端自己算，不依赖这个值）。
	/// </summary>
	private static int CalcGrowLevel(uint totalExp)
	{
		List<int> thresholds = GrowThresholds.Value;
		int exp = (int)Math.Min(int.MaxValue, totalExp);
		int lv = 1;
		foreach (int need in thresholds)
		{
			if (need <= 0 || exp < need)
			{
				return lv;
			}
			exp -= need;
			lv++;
		}
		return lv;
	}

	private static readonly Lazy<List<int>> GrowThresholds = new Lazy<List<int>>(() =>
	{
		List<int> list = new List<int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "_Mod", "RMOL", "config", "grow.json");
			if (!File.Exists(path))
			{
				return list;
			}
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
			foreach (JsonElement row in doc.RootElement.EnumerateArray())
			{
				int need = 0;
				if (row.TryGetProperty("NextLevelEXP", out JsonElement value))
				{
					if (value.ValueKind == JsonValueKind.Number)
					{
						need = value.GetInt32();
					}
					else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed))
					{
						need = parsed;
					}
				}
				list.Add(need);
			}
		}
		catch
		{
		}
		return list;
	});

	private static LevelRewardRow GetRewardRow(int levelId)
	{
		lock (RewardLock)
		{
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "_Mod", "RMOL", "def", "04_levels.json");
				DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
				if (stamp != rewardCacheStamp)
				{
					RewardCache.Clear();
					rewardCacheStamp = stamp;
					if (File.Exists(path))
					{
						using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
						foreach (JsonElement row in doc.RootElement.EnumerateArray())
						{
							int id = ReadIntElement(row, "Id");
							if (id == 0)
							{
								continue;
							}
							RewardCache[id] = new LevelRewardRow
							{
								First = ReadRewardRows(row, "Rewards_1st"),
								Win = ReadRewardRows(row, "Rewards_Win"),
								Lose = ReadRewardRows(row, "Rewards_Lose")
							};
						}
					}
				}
				return RewardCache.TryGetValue(levelId, out LevelRewardRow? cached) ? cached : new LevelRewardRow();
			}
			catch (Exception ex)
			{
				Log.Info($"[warn] read 04_levels.json failed: {ex.Message}");
				return new LevelRewardRow();
			}
		}
	}

	private static List<IReadOnlyList<uint>> ReadRewardRows(JsonElement row, string name)
	{
		List<IReadOnlyList<uint>> list = new List<IReadOnlyList<uint>>();
		if (!row.TryGetProperty(name, out JsonElement rewardArray) || rewardArray.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in rewardArray.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 5)
			{
				continue;
			}
			List<uint> values = new List<uint>(5);
			bool ok = true;
			for (int i = 0; i < 5; i++)
			{
				if (!TryReadUInt(item[i], out uint v))
				{
					ok = false;
					break;
				}
				values.Add(v);
			}
			if (ok && values[4] != 0)
			{
				list.Add(values);
			}
		}
		return list;
	}

	private static int ReadIntElement(JsonElement row, string name)
	{
		return row.TryGetProperty(name, out JsonElement value) && TryReadUInt(value, out uint v) ? (int)v : 0;
	}

	private static bool TryReadUInt(JsonElement value, out uint result)
	{
		result = 0u;
		if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out result))
		{
			return true;
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			string? text = value.GetString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				text = text.Trim().Trim('[', ']', '"');
				return uint.TryParse(text, out result);
			}
		}
		return false;
	}

	private sealed class LevelRewardRow
	{
		public List<IReadOnlyList<uint>> First = new List<IReadOnlyList<uint>>();

		public List<IReadOnlyList<uint>> Win = new List<IReadOnlyList<uint>>();

		public List<IReadOnlyList<uint>> Lose = new List<IReadOnlyList<uint>>();
	}

	private static int ReadInt(params JsonNode?[] nodes)
	{
		foreach (JsonNode? node in nodes)
		{
			if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value))
			{
				return (int)value;
			}
		}
		return 0;
	}

	private static bool ReadBool(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<bool>(out bool flag))
			{
				return flag;
			}
			if (jsonValue.TryGetValue<double>(out double number))
			{
				return number != 0.0;
			}
		}
		return false;
	}

	/// <summary>
	/// 把客户端上报的 Stars 转成属性 (173, 关卡) 的星级位。
	/// 客户端 Level:End 里 Stars 就是 StarRecord() 的结果：{bool, bool, bool}，
	/// 第 i 项为 true 表示拿到第 i 颗星，对应 bit(i+1)。
	/// 也兼容直接传 1/2/3 这种星数或位掩码的写法。
	/// </summary>
	private static uint ReadStarBits(JsonNode? node)
	{
		uint bits = 0u;
		if (node is not JsonArray stars)
		{
			return bits;
		}
		for (int i = 0; i < stars.Count && i < 3; i++)
		{
			JsonNode? item = stars[i];
			if (item is JsonValue value)
			{
				if (value.TryGetValue<bool>(out bool flag))
				{
					if (flag)
					{
						bits |= 1u << (i + 1);
					}
					continue;
				}
				if (value.TryGetValue<double>(out double number) && number != 0.0)
				{
					bits |= 1u << (i + 1);
				}
			}
			else if (int.TryParse(item?.ToString(), out int parsed) && parsed != 0)
			{
				bits |= 1u << (i + 1);
			}
		}
		// 整型数组另有语义：values 里直接是 1/2/3 星的条件 ID。
		if (bits == 0u && stars.Count > 0 && stars.Count <= 3)
		{
			bool allNumber = true;
			foreach (JsonNode? item in stars)
			{
				if (item is not JsonValue v || !v.TryGetValue<double>(out _))
				{
					allNumber = false;
					break;
				}
			}
			if (allNumber)
			{
				foreach (JsonNode? item in stars)
				{
					if (item is JsonValue v && v.TryGetValue<double>(out double star) && star >= 1 && star <= 3)
					{
						bits |= 1u << (int)star;
					}
				}
			}
		}
		return bits;
	}
}
