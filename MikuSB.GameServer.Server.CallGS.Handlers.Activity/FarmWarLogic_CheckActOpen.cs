using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("FarmWarLogic_CheckActOpen")]
public class FarmWarLogic_CheckActOpen : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		uint nId = player.GetAttr(164u, 1u);
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty("nId", out var node) && node.TryGetInt32(out var requested) && requested > 0)
			{
				nId = (uint)requested;
			}
		}
		catch
		{
		}
		if (nId == 0)
		{
			nId = 1u;
		}
		await CallGSRouter.SendScript(connection, "FarmWarLogic_CheckActOpen", "{\"bOpen\":true,\"nId\":" + nId + "}");
	}
}
