using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_GetStoryQuestAward")]
public class StarWish_GetStoryQuestAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		JsonArray gotIds = new JsonArray();
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["tbID"] is JsonArray ids)
			{
				foreach (JsonNode node in ids)
				{
					uint id = StarWishQuests.ReadId(node);
					StarWishStoryQuestExcel quest = GameData.StarWishStoryQuestData.GetValueOrDefault(id);
					if (quest == null || !StarWishQuests.Completed(player, quest) || StarWishQuests.Claimed(player, id))
					{
						continue;
					}
					foreach (JsonNode item in await StarWishQuests.GrantAllAsync(player, quest.Awards, sync))
					{
						granted.Add(item?.DeepClone());
					}
					StarWishAttr.Set(player, StarWishQuests.ClaimedSid(id), 1u, sync);
					gotIds.Add((int)id);
				}
			}
		}
		catch
		{
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetStoryQuestAward",
			["tbAward"] = granted,
			["tbID"] = gotIds
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetStoryQuestAward", rsp.ToJsonString(), sync);
	}
}
