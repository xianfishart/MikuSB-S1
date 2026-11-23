using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_SetStoryQuestCompleted")]
public class StarWish_SetStoryQuestCompleted : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			uint id = StarWishQuests.ReadId(req?["nID"]);
			uint value = StarWishQuests.ReadId(req?["Value"] ?? req?["value"]);
			StarWishStoryQuestExcel quest = GameData.StarWishStoryQuestData.GetValueOrDefault(id);
			if (quest != null)
			{
				StarWishAttr.Set(player, StarWishQuests.ProgressSid(id), (value != 0) ? value : quest.CheckValue, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
		await CallGSRouter.SendScript(connection, "StarWish_SetStoryQuestCompleted", "{}", sync);
	}
}
