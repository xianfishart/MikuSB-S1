using System;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetBirthday")]
public class PlayerSetting_SetBirthday : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		BirthdayParam req = JsonSerializer.Deserialize<BirthdayParam>(param);
		if (req == null || req.Month < 1 || req.Month > 12 || req.Day < 1 || req.Day > 31)
		{
			await CallGSRouter.SendScript(connection, "PlayerSetting_SetBirthday", "{\"sErr\":\"ui.AccountBirthdayTip\"}");
			return;
		}
		player.SetAttr(200u, 1u, (uint)req.Month);
		player.SetAttr(200u, 2u, (uint)req.Day);
		if (player.GetAttr(200u, 3u) == 0)
		{
			player.SetAttr(200u, 3u, 1u);
		}
		player.SetAttr(151u, 1u, (uint)DateTime.Now.Year);
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.ShowAttrs.AddRange(player.BuildShowAttrs());
		await CallGSRouter.SendScript(connection, "PlayerSetting_SetBirthday", "null", sync);
	}
}
