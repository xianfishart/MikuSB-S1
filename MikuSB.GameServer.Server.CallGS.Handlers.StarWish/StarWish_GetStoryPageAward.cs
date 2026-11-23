using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_GetStoryPageAward")]
public class StarWish_GetStoryPageAward : ICallGSHandler
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
		try
		{
			uint id = StarWishQuests.ReadId(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nID"]);
			StarWishStoryPageExcel page = GameData.StarWishStoryPageData.GetValueOrDefault(id);
			if (page != null && StarWishAttr.Read(player, 90 + id) == 0)
			{
				bool allDone = page.Quests.Count > 0;
				foreach (uint quest2 in page.Quests)
				{
					StarWishStoryQuestExcel quest = CollectionExtensions.GetValueOrDefault(key: quest2, dictionary: GameData.StarWishStoryQuestData);
					if (quest == null || !StarWishQuests.Completed(player, quest))
					{
						allDone = false;
						break;
					}
				}
				if (allDone)
				{
					foreach (JsonNode item in await StarWishQuests.GrantAllAsync(player, page.Awards, sync))
					{
						granted.Add(item?.DeepClone());
					}
					StarWishAttr.Set(player, 90 + id, 1u, sync);
				}
			}
		}
		catch
		{
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetStoryPageAward",
			["tbAward"] = granted
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetStoryPageAward", rsp.ToJsonString(), sync);
	}
}
