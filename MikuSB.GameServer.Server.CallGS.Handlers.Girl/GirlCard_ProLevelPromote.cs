using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_ProLevelPromote")]
public class GirlCard_ProLevelPromote : ICallGSHandler
{
	private const uint MaxProLevel = 3u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ProLevelPromoteParam req = JsonSerializer.Deserialize<ProLevelPromoteParam>(param);
		if (req == null || req.CardId == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_ProLevelPromote", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_ProLevelPromote", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (card.ProLevel >= 3)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_ProLevelPromote", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		card.ProLevel++;
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		await CallGSRouter.SendScript(connection, "GirlCard_ProLevelPromote", "{}", new NtfSyncPlayer
		{
			Items = { card.ToProto() }
		});
	}
}
