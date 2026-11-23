using System;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

internal static class Rogue3DScoreSettle
{
	public const uint GroupId = 124u;

	public const uint LevelScoreStart = 2401u;

	public static (int Score, int BattleId, uint LevelId) ReadPayload(JsonObject? param)
	{
		if (param == null)
		{
			return (Score: 0, BattleId: 0, LevelId: 0u);
		}
		int item = ReadInt(param["nBattleId"]);
		uint item2 = (uint)Math.Max(0, ReadInt(param["nID"]));
		int item3 = 0;
		if (param["tbLog"] is JsonObject jsonObject && jsonObject["LevelFinish"] is JsonArray { Count: >=35 } jsonArray)
		{
			item3 = ReadInt(jsonArray[34]);
		}
		return (Score: item3, BattleId: item, LevelId: item2);
	}

	public static bool Record(PlayerInstance player, int battleId, uint levelId, int score, NtfSyncPlayer sync)
	{
		if (player == null)
		{
			return false;
		}
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == levelId);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 22u,
				Sid = levelId,
				Val = 1u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = ((playerAttr.Val == 0 || playerAttr.Val >= 1000) ? 1u : (playerAttr.Val + 1));
		}
		sync.Custom[player.ToPackedAttrKey(22u, levelId)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, levelId)] = playerAttr.Val;
		PlayerAttr playerAttr2 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == levelId);
		if (playerAttr2 == null)
		{
			playerAttr2 = new PlayerAttr
			{
				Gid = 21u,
				Sid = levelId,
				Val = 257u
			};
			player.Data.Attrs.Add(playerAttr2);
		}
		else
		{
			playerAttr2.Val |= 257u;
		}
		sync.Custom[player.ToPackedAttrKey(21u, levelId)] = playerAttr2.Val;
		sync.Custom[player.ToShiftedAttrKey(21u, levelId)] = playerAttr2.Val;
		bool result = false;
		if (battleId > 0)
		{
			uint sid = (uint)(2401 + battleId - 1);
			PlayerAttr playerAttr3 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
			if (playerAttr3 == null)
			{
				playerAttr3 = new PlayerAttr
				{
					Gid = 124u,
					Sid = sid,
					Val = (uint)Math.Max(0, score)
				};
				player.Data.Attrs.Add(playerAttr3);
				result = score > 0;
			}
			else if (score > playerAttr3.Val)
			{
				playerAttr3.Val = (uint)score;
				result = true;
			}
			sync.Custom[player.ToPackedAttrKey(124u, sid)] = playerAttr3.Val;
			sync.Custom[player.ToShiftedAttrKey(124u, sid)] = playerAttr3.Val;
		}
		return result;
	}

	public static uint ReadBest(PlayerInstance player, int battleId)
	{
		if (player == null || battleId <= 0)
		{
			return 0u;
		}
		uint sid = (uint)(2401 + battleId - 1);
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid)?.Val ?? 0;
	}

	public static int ReadInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<double>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (int)value3;
			}
			if (jsonValue.TryGetValue<string>(out string value4) && int.TryParse(value4, out var result))
			{
				return result;
			}
		}
		return 0;
	}
}
