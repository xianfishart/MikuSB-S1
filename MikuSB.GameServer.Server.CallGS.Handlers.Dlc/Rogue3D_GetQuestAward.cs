using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3D_GetQuestAward")]
public class Rogue3D_GetQuestAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonArray rewards = new JsonArray();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonArray ids = new JsonArray();
			foreach (int id in ReadIdList(param))
			{
				ids.Add(id);
			}
			var (flat, _) = await ActivityQuestClaim.ClaimAsync(player, ids, sync);
			foreach (JsonNode item in flat)
			{
				rewards.Add(item?.DeepClone());
			}
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		await CallGSRouter.SendScript(connection, "Rogue3D_GetQuestAward", new JsonObject { ["tbAwardList"] = rewards }.ToJsonString(), sync);
	}

	internal static IEnumerable<int> ReadIdList(string param)
	{
		List<int> list = new List<int>();
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["tbIdList"] is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					list.Add((int)Rogue3DAttr.ReadUInt(item));
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
