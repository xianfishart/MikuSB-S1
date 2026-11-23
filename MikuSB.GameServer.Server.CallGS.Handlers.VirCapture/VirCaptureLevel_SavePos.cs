using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SavePos")]
public class VirCaptureLevel_SavePos : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureSavePosParam req = JsonSerializer.Deserialize<VirCaptureSavePosParam>(param);
		if (req == null || req.LevelId == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SavePos", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, 3u, req.PosX, sync);
		VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, 4u, req.PosY, sync);
		VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, 5u, req.PosZ, sync);
		VirCaptureStateHelper.SetSignedMapOffset(player, (uint)req.LevelId, 6u, req.Toward, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_SavePos", "{}", sync);
	}
}
