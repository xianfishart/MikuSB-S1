using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

public static class RogueSettlement
{
	private static readonly Logger Log = new Logger("RogueSettlement");

	public static async Task<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(PlayerInstance player, JsonNode? tbParam)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint nodeId = ReadUInt(tbParam, "nID");
		uint state = ReadUInt(tbParam, "nState");
		uint levelId = ReadUInt(tbParam, "nLevelID");
		if (state == 0)
		{
			state = 2u;
		}
		if (nodeId == 0)
		{
			Log.Warn($"Rogue settlement without node id for uid={player.Uid}.");
			return (Payload: new JsonArray
			{
				new JsonObject { ["Awards"] = new JsonArray() }
			}, Sync: sync);
		}
		RogueService.SetNodeState(player, nodeId, state, sync);
		RogueService.ApplyHealth(player, tbParam?["tbHP"], sync);
		JsonArray awards = new JsonArray();
		if (state == 2)
		{
			RogueLevelExcel level = ((levelId != 0) ? RogueService.FindLevel(levelId) : null);
			RogueMapExcel node = RogueService.FindNode(RogueService.CurrentMap(player), nodeId);
			if (node != null && node.Node == 1 && node.LevelId != 0 && level == null)
			{
				level = RogueService.FindLevel(node.LevelId);
			}
			if (level != null && level.AwardRows.Count > 0)
			{
				IReadOnlyList<IReadOnlyList<uint>> rows = ((IEnumerable<List<uint>>)level.AwardRows).Select((Func<List<uint>, IReadOnlyList<uint>>)((List<uint> row) => row)).ToList();
				foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
				{
					awards.Add(item?.DeepClone());
				}
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonArray payload = new JsonArray
		{
			new JsonObject { ["Awards"] = awards }
		};
		Log.Info($"Rogue settlement: uid={player.Uid}, node={nodeId}, state={state}, level={levelId}, awards={awards.Count}.");
		return (Payload: payload, Sync: sync);
	}

	private static uint ReadUInt(JsonNode? node, string key)
	{
		if (!(node?[key] is JsonValue jsonValue))
		{
			return 0u;
		}
		if (jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		if (jsonValue.TryGetValue<int>(out var value2) && value2 > 0)
		{
			return (uint)value2;
		}
		if (jsonValue.TryGetValue<string>(out string value3) && uint.TryParse(value3, out var result))
		{
			return result;
		}
		return 0u;
	}
}
