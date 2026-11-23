using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SelectMode")]
public class Rogue3D_SelectMode : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint modeId = JsonSerializer.Deserialize<SelectModeParam>(param)?.ModeId ?? 0;
		NtfSyncPlayer sync = Rogue3DStateHelper.EnsureUnlockState(connection.Player);
		JsonObject response = new JsonObject
		{
			["nModeID"] = modeId,
			["bOpen"] = true
		};
		await CallGSRouter.SendScript(connection, "Rogue3D_SelectMode", response.ToJsonString(), sync);
	}
}
