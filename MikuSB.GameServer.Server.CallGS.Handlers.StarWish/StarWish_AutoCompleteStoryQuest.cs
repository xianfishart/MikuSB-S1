using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_AutoCompleteStoryQuest")]
public class StarWish_AutoCompleteStoryQuest : ICallGSHandler
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
			uint id = StarWishQuests.ReadId(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nID"]);
			StarWishStoryQuestExcel quest = GameData.StarWishStoryQuestData.GetValueOrDefault(id);
			if (quest != null)
			{
				StarWishAttr.Set(player, StarWishQuests.ProgressSid(id), quest.CheckValue, sync);
			}
		}
		catch
		{
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "StarWish_AutoCompleteStoryQuest", "{}", sync);
	}
}
