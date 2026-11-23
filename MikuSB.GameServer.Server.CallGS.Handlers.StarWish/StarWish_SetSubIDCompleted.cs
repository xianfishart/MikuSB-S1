using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_SetSubIDCompleted")]
public class StarWish_SetSubIDCompleted : ICallGSHandler
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
			uint value = StarWishQuests.ReadId(req?["v"]);
			if (id != 0)
			{
				uint sid = StarWishQuests.ProgressSid(id);
				uint cur = StarWishAttr.Read(player, sid);
				uint next = ((value != 0) ? value : (cur + 1));
				StarWishStoryQuestExcel quest = GameData.StarWishStoryQuestData.GetValueOrDefault(id);
				if (quest != null && next > quest.CheckValue)
				{
					next = quest.CheckValue;
				}
				StarWishAttr.Set(player, sid, next, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
		await CallGSRouter.SendScript(connection, "StarWish_SetSubIDCompleted", "{}", sync);
	}
}
