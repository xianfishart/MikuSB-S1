using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

internal static class SeasonBossSettlementWriter
{
	public static void Write(Connection connection, JsonNode? tb, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint levelId = (uint)ReadInt(tb?["nID"]);
		int score = ReadInt(tb?["nDamage"]);
		int time = ReadInt(tb?["nTime"]);
		int zoneId = ReadInt(tb?["nZoneID"]);
		int diff = ReadInt(tb?["nDiff"]);
		int num = ReadInt(tb?["nActID"]);
		int num2 = ReadInt(tb?["nTermID"]);
		SeasonBossProgressService.RecordResult(player, levelId, score, time, sync);
		SeasonBossProgressService.BumpLevelIndex(player, zoneId, diff, sync);
		SeasonBossQuest.Complete(player, levelId, sync);
		if (num > 0)
		{
			SetNum(player, sync, 18u, 1u, (uint)num);
		}
		if (num2 > 0)
		{
			SetNum(player, sync, 18u, 2u, (uint)num2);
		}
	}

	private static void SetNum(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}

	private static int ReadInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return (int)value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return value2;
			}
		}
		return 0;
	}
}
