using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Character;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("RoleCard_SetSupporterTeamIndex")]
public class RoleCard_SetSupporterTeamIndex : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SetSupporterTeamIndexParam req = JsonSerializer.Deserialize<SetSupporterTeamIndexParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "RoleCard_SetSupporterTeamIndex", "{\"err\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		CharacterInfo cardData = player.CharacterManager.GetCharacterByGUID(req.CardId);
		if (cardData != null)
		{
			cardData.SupportTeamIndex = req.Index;
			NtfSyncPlayer sync = new NtfSyncPlayer
			{
				Items = { cardData.ToProto() }
			};
			await CallGSRouter.SendScript(connection, "RoleCard_SetSupporterTeamIndex", "null", sync);
		}
	}
}
