using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_GetLevelAward")]
public class DarkZone_GetLevelAward : ICallGSHandler
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
		JsonArray got = new JsonArray();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["tbIdList"];
			if (jsonNode is JsonArray ids)
			{
				foreach (JsonNode node in ids)
				{
					int id = DarkZoneData.ReadInt(node);
					JsonObject gain = DarkZoneData.FindGain(id);
					if (gain == null)
					{
						continue;
					}
					jsonNode = gain["Reward"];
					if (jsonNode is JsonArray rewards)
					{
						foreach (JsonNode rowNode in rewards)
						{
							JsonArray row = rowNode as JsonArray;
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
					}
					uint levelId = (uint)DarkZoneData.ReadInt(gain["LevelID"]);
					int section = DarkZoneData.SectionOf(levelId);
					DarkZoneData.MarkSection(player, section, sync);
					got.Add(id);
				}
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		catch
		{
		}
		JsonObject rsp = new JsonObject
		{
			["tbAward"] = award,
			["tbGot"] = got
		};
		await CallGSRouter.SendScript(connection, "DarkZone_GetLevelAward", rsp.ToJsonString(), sync);
	}
}
