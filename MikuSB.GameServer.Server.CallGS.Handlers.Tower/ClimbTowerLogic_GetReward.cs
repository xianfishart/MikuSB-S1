using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_GetReward")]
public class ClimbTowerLogic_GetReward : ICallGSHandler
{
	private const uint TowerGroupId = 3u;

	private const uint RewardStateSidBase = 100u;

	private const uint TowerLevelStateSidBase = 10000u;

	private const uint LaunchPassGroupId = 22u;

	private const uint AdvancedDiffSid = 4u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ClimbTowerGetRewardParam req = JsonSerializer.Deserialize<ClimbTowerGetRewardParam>(param);
		if (req == null || req.Layer <= 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		ClimbTowerTimeExcel cycle = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
		if (cycle == null)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!TryResolveLayer(cycle, req.Layer, player.Data, out IReadOnlyList<uint> towerIds, out int diff))
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.ClimbTowerAwardData.TryGetValue((uint)req.Layer, out Dictionary<int, ClimbTowerAwardExcel> diffMap) || !diffMap.TryGetValue(diff, out ClimbTowerAwardExcel rewardCfg))
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<int> groups = ResolveRequestedGroups(req.Group);
		if (groups.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<int> claimableGroups = groups.Where((int num) => CanClaimGroup(player.Data, rewardCfg, towerIds, req.Layer, num)).Distinct().ToList();
		if (claimableGroups.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr rewardStateAttr = GetOrCreateAttr(player.Data, 3u, (uint)(100 + req.Layer));
		JsonArray responseRewards = new JsonArray();
		foreach (int group in claimableGroups)
		{
			rewardStateAttr.Val |= (uint)(1 << GetFlagBitOffset(group));
			foreach (IReadOnlyList<uint> reward in rewardCfg.GetRewards(group))
			{
				if (reward.Count >= 5)
				{
					await GrantRewardAsync(player, sync, reward);
					InlineArray5<JsonNode> buffer = default;
					buffer[0] = (int)reward[0];
					buffer[1] = (int)reward[1];
					buffer[2] = (int)reward[2];
					buffer[3] = (int)reward[3];
					buffer[4] = (int)reward[4];
					responseRewards.Add(new JsonArray(buffer));
				}
			}
		}
		SyncAttr(sync, player, rewardStateAttr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject { ["tbRewards"] = responseRewards };
		await CallGSRouter.SendScript(connection, "ClimbTowerLogic_GetReward", rsp.ToJsonString(), sync);
	}

	private static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
	{
		await player.RewardManager.GrantRewardAsync(reward, sync);
	}

	private static BaseGameItemInfo? AddOtherItem(InventoryData inventory, uint genre, uint detail, uint particular, uint level, uint count)
	{
		uint templateId = (uint)GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
		if (!GameData.OtherItemData.TryGetValue(templateId, out OtherItemExcel value))
		{
			return null;
		}
		uint val = ((value.GMnum != 0) ? value.GMnum : 99999u);
		BaseGameItemInfo baseGameItemInfo = inventory.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
		if (baseGameItemInfo != null)
		{
			baseGameItemInfo.ItemCount = Math.Min(baseGameItemInfo.ItemCount + count, val);
			return baseGameItemInfo;
		}
		BaseGameItemInfo baseGameItemInfo2 = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = inventory.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_USEABLE,
			ItemCount = Math.Min(count, val)
		};
		inventory.Items[baseGameItemInfo2.UniqueId] = baseGameItemInfo2;
		return baseGameItemInfo2;
	}

	private static bool CanClaimGroup(PlayerGameData data, ClimbTowerAwardExcel rewardCfg, IReadOnlyList<uint> towerIds, int layer, int group)
	{
		bool flag = ((group < 0 || group > 3) ? true : false);
		if (flag || IsRewardClaimed(data, layer, group))
		{
			return false;
		}
		if (group == 0)
		{
			return IsLayerPass(data, towerIds);
		}
		int starCount = rewardCfg.GetStarCount(group);
		return starCount > 0 && GetLayerStar(data, towerIds) >= starCount;
	}

	private static bool IsLayerPass(PlayerGameData data, IReadOnlyList<uint> towerIds)
	{
		foreach (uint towerId in towerIds)
		{
			if (!GameData.ClimbTowerLevelOrderData.TryGetValue(towerId, out ClimbTowerLevelOrderExcel orderCfg))
			{
				return false;
			}
			PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == orderCfg.LevelID);
			if (playerAttr == null || playerAttr.Val == 0)
			{
				return false;
			}
		}
		return true;
	}

	private static int GetLayerStar(PlayerGameData data, IReadOnlyList<uint> towerIds)
	{
		int num = 0;
		foreach (uint towerId in towerIds)
		{
			uint num2 = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 3 && x.Sid == 10000 + towerId)?.Val ?? 0;
			for (int num3 = 0; num3 < 9; num3++)
			{
				if (((num2 >> num3) & 1) != 0)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static bool IsRewardClaimed(PlayerGameData data, int layer, int group)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 3 && x.Sid == (uint)(100 + layer));
		if (playerAttr == null)
		{
			return false;
		}
		int flagBitOffset = GetFlagBitOffset(group);
		return ((playerAttr.Val >> flagBitOffset) & 0xF) != 0;
	}

	private static int GetFlagBitOffset(int group)
	{
		if (1 == 0)
		{
		}
		int result = group switch
		{
			0 => 0, 
			1 => 4, 
			2 => 8, 
			3 => 12, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static List<int> ResolveRequestedGroups(int? group)
	{
		int num;
		if (!group.HasValue)
		{
			num = 4;
			List<int> list = new List<int>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<int> span = CollectionsMarshal.AsSpan(list);
			span[0] = 0;
			span[1] = 1;
			span[2] = 2;
			span[3] = 3;
			return list;
		}
		num = group.Value;
		List<int> list2;
		if (num < 0 || num > 3)
		{
			list2 = new List<int>();
		}
		else
		{
			num = 1;
			list2 = new List<int>(num);
			CollectionsMarshal.SetCount(list2, num);
			CollectionsMarshal.AsSpan(list2)[0] = group.Value;
		}
		return list2;
	}

	private static bool TryResolveLayer(ClimbTowerTimeExcel cycle, int layer, PlayerGameData data, out IReadOnlyList<uint> towerIds, out int diff)
	{
		IReadOnlyList<IReadOnlyList<uint>> levelGroups = cycle.GetLevelGroups(1);
		if (layer <= levelGroups.Count)
		{
			towerIds = levelGroups[layer - 1];
			diff = 1;
			return towerIds.Count > 0;
		}
		int num = layer - levelGroups.Count;
		IReadOnlyList<IReadOnlyList<uint>> levelGroups2 = cycle.GetLevelGroups(2);
		if (num <= 0 || num > levelGroups2.Count)
		{
			towerIds = Array.Empty<uint>();
			diff = 0;
			return false;
		}
		diff = (int)(data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 3 && x.Sid == 4)?.Val ?? 0);
		towerIds = levelGroups2[num - 1];
		return diff > 0 && towerIds.Count > 0;
	}

	private static ClimbTowerTimeExcel? ResolveCurrentCycle(IEnumerable<ClimbTowerTimeExcel> configs, DateTime now)
	{
		var source = (from x in configs
			select new
			{
				Config = x,
				Start = ParseConfigTime(x.StartTime),
				End = ParseConfigTime(x.EndTime)
			} into x
			where x.Start.HasValue && x.End.HasValue
			orderby x.Start
			select x).ToList();
		var anon = source.FirstOrDefault(x =>
		{
			int result;
			if (x.Start <= now)
			{
				DateTime value = now;
				DateTime? end = x.End;
				result = ((value < end) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		});
		if (anon != null)
		{
			return anon.Config;
		}
		var anon2 = source.LastOrDefault(x => x.Start <= now);
		if (anon2 != null)
		{
			return anon2.Config;
		}
		return source.FirstOrDefault()?.Config;
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

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
