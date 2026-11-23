using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

internal static class StarWishQuests
{
	public static uint ProgressSid(uint id)
	{
		return 100 + 3 * id;
	}

	public static uint ClaimedSid(uint id)
	{
		return ProgressSid(id) + 1;
	}

	public static bool Completed(PlayerInstance player, StarWishStoryQuestExcel q)
	{
		return StarWishAttr.Read(player, ProgressSid(q.Id)) >= q.CheckValue;
	}

	public static bool Claimed(PlayerInstance player, uint id)
	{
		return StarWishAttr.Read(player, ClaimedSid(id)) != 0;
	}

	public static bool AllStoryDone(PlayerInstance player)
	{
		foreach (var (_, q) in GameData.StarWishStoryQuestData)
		{
			if (!Completed(player, q))
			{
				return false;
			}
		}
		return GameData.StarWishStoryQuestData.Count > 0;
	}

	public static async Task<JsonArray> GrantAllAsync(PlayerInstance player, IEnumerable<List<uint>> rows, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		foreach (List<uint> row in rows)
		{
			if (row.Count < 5 || row[4] == 0)
			{
				continue;
			}
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, row);
			JsonArray arr = new JsonArray();
			foreach (uint v in row)
			{
				arr.Add((int)v);
			}
			granted.Add(arr);
		}
		return granted;
	}

	public static uint ReadId(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}

	/// <summary>
	/// 今天已提交（组 7 / 任务 ID 的 bit0 = 1）的重复任务数量。
	/// 「希冀点缀」的宝箱 i 就是按这个数量解锁的。
	/// </summary>
	public static int ClaimedToday(PlayerInstance player)
	{
		int num = 0;
		foreach (uint questId in StarWishDailyServiceRepeatIds())
		{
			PlayerAttr? attr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == ActivityQuestService.QuestGid && x.Sid == questId);
			if (attr != null && (attr.Val & 1u) != 0)
			{
				num++;
			}
		}
		return num;
	}

	/// <summary>
	/// 三档宝箱全部打勾的位掩码。
	/// 客户端 GetRepeatQuestExtraAwardState(i) = GetBits(180/4, i)，位序实测是「从 0 数」，
	/// 但为了兼容可能存在的「从 1 数」实现，这里把 bit0 ~ bit(count) 一起置上，
	/// 两种位序下三个箱子都会显示已领取。
	/// </summary>
	public static uint AllBoxMask(int count)
	{
		if (count <= 0)
		{
			return 0u;
		}
		if (count >= 30)
		{
			return uint.MaxValue;
		}
		return (1u << (count + 1)) - 1u;
	}

	private static List<uint> StarWishDailyServiceRepeatIds()
	{
		return StarWishDailyService.RepeatQuestIds();
	}
}
