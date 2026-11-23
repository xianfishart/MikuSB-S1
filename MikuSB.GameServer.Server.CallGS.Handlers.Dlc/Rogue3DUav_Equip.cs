using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3DUav_Equip")]
public class Rogue3DUav_Equip : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint idx = 0u;
		uint slot = 0u;
		uint replaceIdx = 0u;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			idx = Rogue3DAttr.ReadUInt(req?["nIdx"]);
			slot = Rogue3DAttr.ReadUInt(req?["nSlotIdx"]);
			replaceIdx = Rogue3DAttr.ReadUInt(req?["nReplaceIdx"]);
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null && idx < 260)
		{
			uint sid = 3105 + idx;
			uint cur = Rogue3DAttr.Read(player, sid);
			if (cur != 0)
			{
				Rogue3DAttr.Write(player, sid, Rogue3DAttr.SetField(cur, 28, 4, slot), sync);
				if (replaceIdx != 0 && replaceIdx < 260)
				{
					uint rsid = 3105 + replaceIdx;
					uint rcur = Rogue3DAttr.Read(player, rsid);
					if (rcur != 0)
					{
						Rogue3DAttr.Write(player, rsid, Rogue3DAttr.SetField(rcur, 28, 4, 0u), sync);
					}
				}
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		await CallGSRouter.SendScript(connection, "Rogue3DUav_Equip", "{}", sync);
	}
}
