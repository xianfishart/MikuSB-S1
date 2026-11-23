using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_GetMissionAward")]
public class DarkZone_GetMissionAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray award = new JsonArray();
		try
		{
			int id = DarkZoneData.ReadInt(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nId"]);
			JsonNode jsonNode = DarkZoneData.FindGain(id)?["Reward"];
			if (jsonNode is JsonArray rewards)
			{
				foreach (JsonNode node in rewards)
				{
					JsonArray row = node as JsonArray;
					if (row == null || row.Count < 5)
					{
						continue;
					}
					List<uint> values = new List<uint>();
					foreach (JsonNode item in row)
					{
						values.Add(uint.TryParse(item?.ToString(), out var n) ? n : 0u);
					}
					if (values.Count < 5 || values[0] == 0 || values[4] == 0)
					{
						continue;
					}
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
					JsonArray copy = new JsonArray();
					foreach (uint v in values)
					{
						copy.Add(v);
					}
					award.Add(copy);
				}
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
		}
		catch
		{
		}
		JsonObject rsp = new JsonObject { ["tbAwardList"] = award };
		await CallGSRouter.SendScript(connection, "DarkZone_GetMissionAward", rsp.ToJsonString(), sync);
	}
}
