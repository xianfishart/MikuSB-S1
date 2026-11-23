using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

public static class RoleFestivalService
{
	public const uint GroupId = 72u;

	public const uint ActSid = 1u;

	private const uint ContentStartSid = 1000u;

	private const uint ReadStateStartSid = 3000u;

	private const uint RewardStartSid = 100u;

	public static uint GetActId(PlayerInstance player)
	{
		return player.Attributes.GetValue(72u, 1u);
	}

	public static uint ContentSid(uint npcId)
	{
		return 1000 + npcId;
	}

	public static uint ReadSid(uint npcId)
	{
		return 3000 + npcId;
	}

	public static uint RewardSid(uint messageNum)
	{
		return 100 + messageNum;
	}

	public static List<RoleFestivalContentExcel> MessagesOf(uint actId)
	{
		if (actId == 0)
		{
			return new List<RoleFestivalContentExcel>();
		}
		return (from row in GameData.RoleFestivalContentData.Values
			where row.ActId == actId
			orderby row.Npc
			select row).ToList();
	}

	public static int OpenMessages(PlayerInstance player, uint actId, NtfSyncPlayer? sync = null)
	{
		if (actId == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (RoleFestivalContentExcel item in MessagesOf(actId))
		{
			if (item.IsEasterEgg == 0 && player.Attributes.GetValue(72u, ContentSid(item.Npc)) != 1)
			{
				PlayerAttr attr = player.Attributes.Set(72u, ContentSid(item.Npc), 1u);
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, attr);
				}
				num++;
			}
		}
		return num;
	}

	public static int CountRead(PlayerInstance player, uint actId)
	{
		if (actId == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (RoleFestivalContentExcel item in MessagesOf(actId))
		{
			if (player.Attributes.GetValue(72u, ReadSid(item.Npc)) == 1)
			{
				num++;
			}
		}
		return num;
	}

	public static bool IsMessageOfAct(uint actId, uint npcId)
	{
		RoleFestivalContentExcel value;
		return actId != 0 && GameData.RoleFestivalContentData.TryGetValue(actId * 1000 + npcId, out value);
	}

	public static RoleFestivalRewardExcel? FindReward(uint actId, uint messageNum)
	{
		RoleFestivalRewardExcel value;
		return GameData.RoleFestivalRewardData.TryGetValue(actId * 1000 + messageNum, out value) ? value : null;
	}
}
