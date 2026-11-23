using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_GetAllCompleteAward")]
public class StarWish_GetAllCompleteAward : ICallGSHandler
{
	private const uint AllCompleteSid = 5u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		if (StarWishAttr.Read(player, 5u) == 0 && StarWishQuests.AllStoryDone(player))
		{
			StarWishMainExcel act = StarWishAttr.Act();
			if (act != null)
			{
				foreach (JsonNode item in await StarWishQuests.GrantAllAsync(player, act.DormGift, sync))
				{
					granted.Add(item?.DeepClone());
				}
			}
			StarWishAttr.Set(player, 5u, 1u, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetAllCompleteAward",
			["tbAward"] = granted
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetAllCompleteAward", rsp.ToJsonString(), sync);
	}
}
