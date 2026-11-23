using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("SettingChange")]
public class SettingChange : ICallGSHandler
{
	private const uint PlayerSettingGid = 44u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		List<SettingChangeParam> changes = JsonSerializer.Deserialize<List<SettingChangeParam>>(param) ?? new List<SettingChangeParam>();
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (SettingChangeParam change in changes)
		{
			string value = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 44 && x.Sid == change.Id)?.Val;
			if (value != null)
			{
				sync.CustomStr[player.ToShiftedAttrKey(44u, change.Id)] = value;
			}
		}
		if (sync.CustomStr.Count > 0)
		{
			await connection.SendPacket(1004, sync, 0);
		}
	}
}
