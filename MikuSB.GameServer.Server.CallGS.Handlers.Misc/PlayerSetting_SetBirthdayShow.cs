using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetBitthdayShow")]
public class PlayerSetting_SetBirthdayShow : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		BirthdayShowParam req = JsonSerializer.Deserialize<BirthdayShowParam>(param);
		if (req != null)
		{
			player.SetAttr(200u, 3u, req.Show ? 1u : 2u);
			DatabaseHelper.SaveDatabaseType(player.Data);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.ShowAttrs.AddRange(player.BuildShowAttrs());
			await CallGSRouter.SendScript(connection, "PlayerSetting_SetBitthdayShow", "null", sync);
		}
	}
}
