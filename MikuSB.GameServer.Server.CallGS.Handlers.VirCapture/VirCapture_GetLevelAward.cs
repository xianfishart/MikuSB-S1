using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_GetLevelAward")]
public class VirCapture_GetLevelAward : ICallGSHandler
{
	private const uint VirCaptureGroupId = 128u;

	private const uint CurLevelSid = 3u;

	private const uint LevelAwardFlagStartSid = 101u;

	private const uint LevelAwardFlagEndSid = 120u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		VirCaptureGetLevelAwardParam req = JsonSerializer.Deserialize<VirCaptureGetLevelAwardParam>(param);
		if (req == null || req.IdList == null || req.IdList.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCapture_GetLevelAward", "{\"tbAwardList\":[]}");
			return;
		}
		uint curLevel = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == 3)?.Val ?? 0;
		List<uint> requestedLevels = (from x in (from x in req.IdList
				where x > 0
				select (uint)x).Distinct()
			orderby x
			select x).ToList();
		List<uint> claimLevels = requestedLevels.Where((uint num) => num <= curLevel && CanClaimLevel(player.Data, num)).ToList();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray responseAwards = new JsonArray();
		foreach (uint level in claimLevels)
		{
			if (!GameData.VirCaptureLevelListData.TryGetValue(level, out VirCaptureLevelListExcel levelCfg) || levelCfg.Rewards.Count == 0)
			{
				continue;
			}
			SetClaimed(player, sync, level);
			JsonArray group = new JsonArray();
			foreach (List<uint> reward in levelCfg.Rewards)
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
					group.Add(new JsonArray(buffer));
				}
			}
			responseAwards.Add(group);
			levelCfg = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject { ["tbAwardList"] = responseAwards };
		await CallGSRouter.SendScript(connection, "VirCapture_GetLevelAward", rsp.ToJsonString(), sync);
	}

	private static bool CanClaimLevel(PlayerGameData data, uint level)
	{
		uint sid = GetLevelAwardSid(level);
		if (sid < 101 || sid > 120)
		{
			return false;
		}
		int levelAwardBit = GetLevelAwardBit(level);
		return ((data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid)?.Val ?? 0) & (uint)(1 << levelAwardBit)) == 0;
	}

	private static void SetClaimed(PlayerInstance player, NtfSyncPlayer sync, uint level)
	{
		uint levelAwardSid = GetLevelAwardSid(level);
		int levelAwardBit = GetLevelAwardBit(level);
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 128u, levelAwardSid);
		orCreateAttr.Val |= (uint)(1 << levelAwardBit);
		sync.Custom[player.ToPackedAttrKey(128u, levelAwardSid)] = orCreateAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(128u, levelAwardSid)] = orCreateAttr.Val;
	}

	private static uint GetLevelAwardSid(uint level)
	{
		return 101 + level / 30;
	}

	private static int GetLevelAwardBit(uint level)
	{
		return (int)(level % 30);
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
			Sid = sid,
			Val = 0u
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	internal static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
	{
		await player.RewardManager.GrantRewardAsync(reward, sync);
	}

	internal static BaseGameItemInfo? AddOtherItem(InventoryData inventory, uint genre, uint detail, uint particular, uint level, uint count)
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
}
