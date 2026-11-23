using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Achievement;

public static class AchievementClaimService
{
	public const uint NormalGroupId = 2u;

	public const uint QuestGroupId = 7u;

	private const uint ClaimedBit = 1u;

	private const int ProgressShift = 1;

	private const uint MaxProgress = 2147483647u;

	public static bool IsClaimed(PlayerInstance player, uint groupId, uint id)
	{
		return (player.Attributes.GetValue(groupId, id) & 1) != 0;
	}

	public static uint GetProgress(PlayerInstance player, uint groupId, uint id)
	{
		return (player.Attributes.GetValue(groupId, id) >> 1) & 0x7FFFFFFF;
	}

	public static bool IsFinished(PlayerInstance player, uint groupId, uint id, uint checkValue)
	{
		uint num = ((checkValue == 0) ? 1u : checkValue);
		return GetProgress(player, groupId, id) >= num;
	}

	public static bool Complete(PlayerInstance player, uint groupId, uint id, uint checkValue, NtfSyncPlayer sync)
	{
		uint num = ((checkValue == 0) ? 1u : checkValue);
		PlayerAttr orCreate = player.Attributes.GetOrCreate(groupId, id);
		if (orCreate.Val >> 1 >= num)
		{
			return false;
		}
		orCreate.Val = (orCreate.Val & 1) | (num << 1);
		player.Attributes.SyncTo(sync, orCreate);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return true;
	}

	public static async ValueTask<JsonArray?> TryClaimAsync(PlayerInstance player, uint groupId, uint id, uint checkValue, IReadOnlyList<IReadOnlyList<uint>> rewardRows, NtfSyncPlayer sync)
	{
		if (IsClaimed(player, groupId, id))
		{
			return null;
		}
		if (!IsFinished(player, groupId, id, checkValue))
		{
			return null;
		}
		JsonArray granted = new JsonArray();
		foreach (IReadOnlyList<uint> reward in rewardRows)
		{
			if (reward.Count < 5 || reward[4] == 0)
			{
				continue;
			}
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			JsonArray rowArr = new JsonArray();
			foreach (uint v in reward)
			{
				rowArr.Add(v);
			}
			granted.Add(rowArr);
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(groupId, id);
		attr.Val |= 1u;
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return granted;
	}

	public static List<List<uint>> ReadRewardRows(JToken? token)
	{
		List<List<uint>> list = new List<List<uint>>();
		if (!(token is JArray jArray))
		{
			return list;
		}
		foreach (JToken item in jArray)
		{
			if (!(item is JArray { Count: >=5 } jArray2))
			{
				continue;
			}
			List<uint> list2 = new List<uint>(5);
			bool flag = true;
			for (int i = 0; i < 5; i++)
			{
				string s = jArray2[i]?.ToString().Trim().Trim(new char[3] { '[', ']', '"' }) ?? string.Empty;
				if (!uint.TryParse(s, out var result))
				{
					flag = false;
					break;
				}
				list2.Add(result);
			}
			if (flag && list2[4] != 0)
			{
				list.Add(list2);
			}
		}
		return list;
	}
}
