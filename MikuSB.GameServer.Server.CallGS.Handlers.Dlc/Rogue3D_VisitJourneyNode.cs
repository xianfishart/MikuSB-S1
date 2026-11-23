using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3D_VisitJourneyNode")]
public class Rogue3D_VisitJourneyNode : ICallGSHandler
{
	private static JsonArray? _nodes;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		uint nodeId = 0u;
		uint periodId = 0u;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			nodeId = Rogue3DAttr.ReadUInt(req?["nNodeID"]);
			periodId = Rogue3DAttr.ReadUInt(req?["nPeriodID"]);
		}
		catch
		{
		}
		JsonObject rsp = new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (player != null && nodeId != 0)
		{
			JsonObject row = FindNode(nodeId, periodId);
			uint sid = 2001 + nodeId / 8;
			int shift = (int)(nodeId % 8 * 4);
			uint cur = Rogue3DAttr.Read(player, sid);
			uint state = 2u;
			if (Rogue3DAttr.GetField(cur, shift, 4) < state)
			{
				Rogue3DAttr.Write(player, sid, Rogue3DAttr.SetField(cur, shift, 4, state), sync);
			}
			JsonNode jsonNode = row?["Reward"];
			JsonArray reward = jsonNode as JsonArray;
			if (reward != null && reward.Count >= 5)
			{
				List<uint> list = (from x in reward.Select(Rogue3DAttr.ReadUInt)
					where x != 0
					select x).ToList();
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, list);
				JsonArray echo = new JsonArray();
				foreach (uint value in list)
				{
					echo.Add(value);
				}
				rsp["Award"] = new JsonArray { echo };
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		await CallGSRouter.SendScript(connection, "Rogue3D_VisitJourneyNode", rsp.ToJsonString(), sync);
	}

	private static JsonObject? FindNode(uint nodeId, uint periodId)
	{
		if (_nodes == null)
		{
			_nodes = new JsonArray();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "dlc";
				buffer[3] = "rogue3d";
				buffer[4] = "journey_map.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							_nodes.Add(jsonObject.DeepClone());
						}
					}
				}
			}
			catch
			{
			}
		}
		foreach (JsonNode node in _nodes)
		{
			if (!(node is JsonObject jsonObject2) || Rogue3DAttr.ReadUInt(jsonObject2["ID"]) != nodeId || (periodId != 0 && Rogue3DAttr.ReadUInt(jsonObject2["PeriodsID"]) != periodId))
			{
				continue;
			}
			return jsonObject2;
		}
		return null;
	}
}
