using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("BuyRefreshMapTimes")]
public class BuyRefreshMapTimes : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint bought = HouseAttr.Read(player, DormPuzzleRequests.BuyRefreshSid);
		if (bought >= 2)
		{
			await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"BuyRefreshMapTimesSuccess\",\"sErr\":\"house.CantBuyPuzzleRefresh\"}");
			return;
		}
		uint price = LoadRefreshPrice((int)(bought + 1));
		PlayerAttr gold = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == 5);
		if (gold == null || gold.Val < price)
		{
			await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"BuyRefreshMapTimesSuccess\",\"sErr\":\"tip.cash_not_enough\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		gold.Val -= price;
		sync.Custom[player.ToPackedAttrKey(1u, 5u)] = gold.Val;
		sync.Custom[player.ToShiftedAttrKey(1u, 5u)] = gold.Val;
		await HouseAttr.SetAsync(connection, DormPuzzleRequests.RefreshSid, HouseAttr.Read(player, DormPuzzleRequests.RefreshSid) + 1, sync);
		await HouseAttr.SetAsync(connection, DormPuzzleRequests.BuyRefreshSid, bought + 1, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"BuyRefreshMapTimesSuccess\",\"bSuccess\":true}", sync);
	}

	private static uint LoadRefreshPrice(int index)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "BuyPuzzleRefreshPrice.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			JsonObject jsonObject = ((jsonArray != null) ? jsonArray.OfType<JsonObject>().FirstOrDefault((JsonObject x) => HouseJson.NumField(x, "Index") == index) : null);
			int num = ((jsonObject != null) ? HouseJson.NumField(jsonObject, "Price") : 0);
			return (num > 0) ? ((uint)num) : 50u;
		}
		catch
		{
			return 50u;
		}
	}
}
