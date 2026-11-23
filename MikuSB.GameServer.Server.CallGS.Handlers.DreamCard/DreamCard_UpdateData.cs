using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_UpdateData")]
public class DreamCard_UpdateData : ICallGSHandler
{
	private const uint DataGroupId = 62u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool dirty = false;
		try
		{
			List<DreamCardUpdateDataEntry> entries = JsonSerializer.Deserialize<List<DreamCardUpdateDataEntry>>(param) ?? new List<DreamCardUpdateDataEntry>();
			foreach (DreamCardUpdateDataEntry entry in entries)
			{
				if (entry.Id > 0)
				{
					string value = NormalizeJson(entry.Data);
					player.SetStrAttr(62u, (uint)entry.Id, value);
					sync.CustomStr[player.ToShiftedAttrKey(62u, (uint)entry.Id)] = value;
					dirty = true;
				}
			}
		}
		catch
		{
		}
		if (dirty)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "DreamCard_UpdateData", "{}", sync);
	}

	private static string NormalizeJson(JsonElement data)
	{
		return (data.ValueKind == JsonValueKind.Undefined) ? "null" : data.GetRawText();
	}
}
