using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_ChangeShowCard")]
public class PlayerSetting_ChangeShowCard : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ChangeShowCardParam req = JsonSerializer.Deserialize<ChangeShowCardParam>(param);
		if (req != null)
		{
			CharacterInfo card = player.CharacterManager.GetCharacterByGUID(req.Id);
			if (card == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeShowCard", "{}");
				return;
			}
			player.SetShowItem(4, card.Guid);
			DatabaseHelper.SaveDatabaseType(player.Data);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.ShowItems.AddRange(player.Data.ShowItems);
			await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeShowCard", "{}", sync);
		}
	}
}
