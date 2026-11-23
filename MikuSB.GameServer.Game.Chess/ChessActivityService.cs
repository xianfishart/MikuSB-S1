using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Chess;

public static class ChessActivityService
{
	private static readonly uint[] FansItem = new uint[4] { 5u, 25u, 4u, 1u };

	public static ChessActivityExcel? Row(uint id)
	{
		ChessActivityExcel value;
		return GameData.ChessActivityData.TryGetValue(id, out value) ? value : null;
	}

	public static ChessMapExcel? MapRow(uint activityId, uint mapId)
	{
		ChessMapExcel value;
		return GameData.ChessMapData.TryGetValue(activityId * 100 + mapId, out value) ? value : null;
	}

	public static long ToUnix(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0L;
		}
		return DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(raw.Trim().Trim(new char[2] { '[', ']' })));
	}

	public static ChessActivityExcel? SelectActivity(DateTime now, params uint[] prefer)
	{
		long nowUnix = DlcRuntimeOverrides.ToUnixSeconds(now);
		ChessActivityExcel chessActivityExcel = null;
		foreach (uint id in prefer)
		{
			ChessActivityExcel chessActivityExcel2 = Row(id);
			if (chessActivityExcel2 != null)
			{
				if (chessActivityExcel == null)
				{
					chessActivityExcel = chessActivityExcel2;
				}
				long num = ToUnix(chessActivityExcel2.Begin);
				long num2 = ToUnix(chessActivityExcel2.End);
				if ((num == 0L || nowUnix >= num) && (num2 == 0L || nowUnix < num2))
				{
					return chessActivityExcel2;
				}
			}
		}
		return (from r in GameData.ChessActivityData.Values
			where ToUnix(r.Begin) <= nowUnix
			orderby r.Id descending
			select r).FirstOrDefault() ?? chessActivityExcel ?? GameData.ChessActivityData.Values.OrderByDescending((ChessActivityExcel r) => r.Id).FirstOrDefault();
	}

	public static uint SelectedId(DateTime now)
	{
		return SelectActivity(now, 4u, 2u, 1u, 3u)?.Id ?? 0;
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint num = SelectedId(DateTime.Now);
		if (num != 0 && player.Attributes.GetValue(12u, 0u) != num)
		{
			PlayerAttr attr = player.Attributes.Set(12u, 0u, num);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
		if (player.Attributes.GetValue(12u, 201u) != 1)
		{
			PlayerAttr attr2 = player.Attributes.Set(12u, 201u, 1u);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr2);
			}
		}
	}

	public static uint MapEnterBits(PlayerInstance player)
	{
		return player.Attributes.GetValue(12u, 2u);
	}

	public static bool IsMapEntered(PlayerInstance player, uint mapId)
	{
		return mapId != 0 && mapId < 32 && ((MapEnterBits(player) >> (int)mapId) & 1) != 0;
	}

	public static void SetMapEntered(PlayerInstance player, uint mapId, NtfSyncPlayer sync)
	{
		if ((mapId < 32 && mapId != 0) || 1 == 0)
		{
			uint value = MapEnterBits(player) | (uint)(1 << (int)mapId);
			PlayerAttr attr = player.Attributes.Set(12u, 2u, value);
			player.Attributes.SyncTo(sync, attr);
		}
	}

	public static uint NewSeed()
	{
		return (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
	}

	public static uint OpenSession(PlayerInstance player, uint activityId, uint mapId, NtfSyncPlayer? sync, out bool first)
	{
		first = !IsMapEntered(player, mapId);
		uint num = NewSeed();
		JsonObject jsonObject = new JsonObject
		{
			["seed"] = num,
			["act"] = activityId,
			["map"] = mapId,
			["at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			["done"] = 0
		};
		PlayerStrAttr attr = player.Attributes.SetString(12u, 210u, jsonObject.ToJsonString());
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		if (first)
		{
			SetMapEntered(player, mapId, sync ?? new NtfSyncPlayer());
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return num;
	}

	public static JsonObject? ReadSession(PlayerInstance player)
	{
		string stringValue = player.Attributes.GetStringValue(12u, 210u);
		if (string.IsNullOrEmpty(stringValue))
		{
			return null;
		}
		try
		{
			return JsonNode.Parse(stringValue) as JsonObject;
		}
		catch
		{
			return null;
		}
	}

	public static uint SessionUInt(JsonObject? session, string key)
	{
		uint value;
		return (session?[key] is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out value)) ? value : 0u;
	}

	public static bool TryCloseSession(PlayerInstance player, uint seed, uint activityId, uint mapId)
	{
		JsonObject jsonObject = ReadSession(player);
		if (jsonObject == null)
		{
			return false;
		}
		if (SessionUInt(jsonObject, "done") != 0)
		{
			return false;
		}
		if (seed != 0 && SessionUInt(jsonObject, "seed") != seed)
		{
			return false;
		}
		if (SessionUInt(jsonObject, "act") != activityId || SessionUInt(jsonObject, "map") != mapId)
		{
			return false;
		}
		jsonObject["done"] = 1;
		player.Attributes.SetString(12u, 210u, jsonObject.ToJsonString());
		DatabaseHelper.SaveDatabaseType(player.Data);
		return true;
	}

	public static async Task<uint> PayRankRewardAsync(PlayerInstance player, uint activityId, uint mapId, uint rank, NtfSyncPlayer sync)
	{
		uint amount = MapRow(activityId, mapId)?.RankAward(rank) ?? 0;
		if (amount == 0)
		{
			amount = ((rank <= 1) ? 20u : 10u);
		}
		ChessActivityExcel activity = Row(activityId);
		if (activity != null && activity.AwardId != 0)
		{
			player.AddCurrency(activity.AwardId, amount, sync);
		}
		else
		{
			IReadOnlyList<IReadOnlyList<uint>> rows = new _003C_003Ez__ReadOnlySingleElementList<IReadOnlyList<uint>>(new _003C_003Ez__ReadOnlyArray<uint>(new uint[5]
			{
				FansItem[0],
				FansItem[1],
				FansItem[2],
				FansItem[3],
				amount
			}));
			await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		}
		return amount;
	}

	public static uint Votes(PlayerInstance player, uint npcId)
	{
		return player.Attributes.GetValue(118u, 2 * npcId + 1);
	}

	public static bool IsVoteRewarded(PlayerInstance player, uint npcId)
	{
		return player.Attributes.GetValue(118u, 2 * npcId + 2) != 0;
	}

	public static void AddVotes(PlayerInstance player, uint npcId, uint count, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.Add(118u, 2 * npcId + 1, count);
		player.Attributes.SyncTo(sync, attr);
	}

	public static void MarkVoteRewarded(PlayerInstance player, uint npcId, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.Set(118u, 2 * npcId + 2, 1u);
		player.Attributes.SyncTo(sync, attr);
	}

	public static bool IsEndUnlocked(PlayerInstance player, uint endId)
	{
		return player.Attributes.GetValue(131u, endId) != 0;
	}

	public static bool IsEndLiked(PlayerInstance player, uint endId)
	{
		return player.Attributes.GetValue(131u, 25 + endId) != 0;
	}

	public static void MarkEndLiked(PlayerInstance player, uint endId, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.Set(131u, 25 + endId, 1u);
		player.Attributes.SyncTo(sync, attr);
	}
}
