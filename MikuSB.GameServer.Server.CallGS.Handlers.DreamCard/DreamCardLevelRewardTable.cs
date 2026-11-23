using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

/// <summary>
/// 第 495 节：纷愿绮梦关卡通奖励（潜质点）。
///
/// 现象：关卡打完、结算面板正常，但「潜质点」一个没给，天赋树因为 0 点什么都点不了
/// （DreamCard_UnlockTalent 扣的是货币 52 → 属性 1/105，一直是 0）。
///
/// 原因：结算只把 RewardID 回给客户端（response["nRewardID"]），让客户端显示奖励图标，
/// 服务端自己从没读过 level_reward.json，也没发过东西。
/// level_reward.json 的一行是：
///   { "RewardID": 4, "CardUnlock": [58,59,...], "MoonNum": [52, 1] }
/// MoonNum 的 [0] 是**货币类型**（52 = 潜质点，对应属性 sid 2*52+1 = 105），[1] 是数量。
/// RewardID 本身由 levellist.json 的 (LevelListID, HardStage) 决定，结算里已经有映射。
///
/// 这里做两件事：
///   1. 每次普通关结算发放 MoonNum；
///   2. 登录时按属性 152 的通关记录补发一次（老存档之前一次都没拿到过），
///      用 152/9 当一次性标记，避免重复补。
///
/// 口径说明：目前是「每次通关都发」，不是「首通才发」。官方大概是首通，
/// 但本端之前一次都没发过，首通口径会让已经通关的关卡永远补不回来，
/// 所以先按每次通关发；要改回首通只需把 GrantOnClear 的调用点加个首通判断。
/// </summary>
internal static class DreamCardLevelRewardTable
{
	private const uint LevelGid = 152u;

	/// <summary>潜质点：货币类型 52（属性 1/105，2*52+1）。</summary>
	private const uint PointsMoneyType = 52u;

	/// <summary>152/9 用作「通奖励已补发」一次性标记（0 号是挑战期号，1~9 属空关卡块）。</summary>
	private const uint BackfillMarkerSid = 9u;

	private static Dictionary<uint, (uint MoonType, uint MoonCount)> _table;

	private static Dictionary<uint, (uint MoonType, uint MoonCount)> Table
	{
		get
		{
			if (_table != null)
			{
				return _table;
			}
			Dictionary<uint, (uint, uint)> dictionary = new Dictionary<uint, (uint, uint)>();
			try
			{
				string path = Path.Combine(ConfigManager.Config.Path.ResourcePath, "dlc", "DreamCard", "level_reward.json");
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray arr)
				{
					foreach (JsonNode? node in arr)
					{
						if (node is not JsonObject row)
						{
							continue;
						}
						uint id = ReadUInt(row["RewardID"]);
						if (id == 0)
						{
							continue;
						}
						uint type = 0u;
						uint count = 0u;
						if (row["MoonNum"] is JsonArray moon && moon.Count >= 2)
						{
							type = ReadUInt(moon[0]);
							count = ReadUInt(moon[1]);
						}
						dictionary[id] = (type, count);
					}
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("[DreamCard] level_reward.json load failed: " + ex.Message);
			}
			_table = dictionary;
			return _table;
		}
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue v)
		{
			if (v.TryGetValue<uint>(out uint u))
			{
				return u;
			}
			if (v.TryGetValue<int>(out int i) && i > 0)
			{
				return (uint)i;
			}
			if (v.TryGetValue<string>(out string? s) && uint.TryParse(s, out uint p))
			{
				return p;
			}
		}
		return 0u;
	}

	public static bool TryGet(uint rewardId, out uint moonType, out uint moonCount)
	{
		moonType = 0u;
		moonCount = 0u;
		if (rewardId == 0 || !Table.TryGetValue(rewardId, out (uint MoonType, uint MoonCount) row))
		{
			return false;
		}
		moonType = row.MoonType;
		moonCount = row.MoonCount;
		return true;
	}

	/// <summary>普通关结算发潜质点，返回发放的货币种数。</summary>
	public static int GrantOnClear(PlayerInstance player, NtfSyncPlayer? sync, uint rewardId)
	{
		if (player == null || rewardId == 0)
		{
			return 0;
		}
		if (!TryGet(rewardId, out uint type, out uint count) || count == 0 || type == 0)
		{
			return 0;
		}
		player.AddCurrency(type, count, sync ?? new NtfSyncPlayer());
		return 1;
	}

	/// <summary>
	/// 登录补发：按 152 的通关记录（sid = 关卡号*10 + 1，值 = 通关次数）
	/// 把每个已通关关卡的 RewardID 对应的潜质点补一次。用 152/9 做一次性标记。
	/// </summary>
	public static int BackfillLevelPoints(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player?.Data?.Attrs == null)
		{
			return 0;
		}
		PlayerAttr? marker = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == LevelGid && x.Sid == BackfillMarkerSid);
		if (marker != null && marker.Val != 0)
		{
			return 0;
		}
		Dictionary<uint, uint> pending = new Dictionary<uint, uint>();
		foreach (PlayerAttr attr in player.Data.Attrs.ToList())
		{
			if (attr.Gid != LevelGid || attr.Sid < 11 || attr.Sid % 10 != 1 || attr.Val == 0)
			{
				continue;
			}
			uint levelId = attr.Sid / 10;
			uint diff = 1u;
			PlayerAttr? diffAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == LevelGid && x.Sid == attr.Sid + 1);
			if (diffAttr != null && diffAttr.Val != 0)
			{
				diff = diffAttr.Val;
			}
			if (!DreamCard_LevelSettlement.TryGetOrdinaryReward(levelId, diff, out uint rewardId))
			{
				continue;
			}
			if (!TryGet(rewardId, out uint type, out uint count) || type == 0 || count == 0)
			{
				continue;
			}
			pending[type] = (pending.TryGetValue(type, out uint old) ? old : 0u) + count;
		}
		NtfSyncPlayer target = sync ?? new NtfSyncPlayer();
		int granted = 0;
		foreach (KeyValuePair<uint, uint> pair in pending)
		{
			player.AddCurrency(pair.Key, pair.Value, target);
			granted += (int)pair.Value;
		}
		PlayerAttr stamp = player.Attributes.GetOrCreate(LevelGid, BackfillMarkerSid);
		stamp.Val = 1u;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, stamp);
		}
		return granted;
	}
}
