using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("DoDailyTalk")]
public class DoDailyTalk : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonObject root = HouseJson.ParseObject(param);
			if (root != null)
			{
				JsonObject request = (root["tbParam"] as JsonObject) ?? root;
				int girlId = DormDailyEvents.Num(request, "GirlId");
				int eventId = DormDailyEvents.Take(player, girlId).EventId;
				NtfSyncPlayer sync = new NtfSyncPlayer();
				var (added, preLevel, newLevel) = await HouseFavor.Award(connection, girlId, 100u, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
				sync.Custom[player.ToPackedAttrKey(101u, (uint)(girlId * 50 + 3))] = 0u;
				sync.Custom[player.ToShiftedAttrKey(101u, (uint)(girlId * 50 + 3))] = 0u;
				JsonObject response = new JsonObject
				{
					["FuncName"] = "DoDailyTalk",
					["GirlId"] = girlId,
					["EventId"] = eventId
				};
				HouseFavor.AddFavorFields(response, preLevel, newLevel, added);
				TaskEventService.AddByType(player, 120, 1);
				await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
			}
		}
	}
}
