using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_ChangeFlag")]
public class VirCaptureLevel_ChangeFlag : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureChangeFlagParam req = JsonSerializer.Deserialize<VirCaptureChangeFlagParam>(param);
		if (req == null || req.LevelId == 0 || req.RegionId == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_ChangeFlag", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, (!req.Clean) ? 1u : 0u, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		string rsp = $"{{\"nLevelID\":{req.LevelId},\"nRegionId\":{req.RegionId},\"bClean\":{req.Clean.ToString().ToLowerInvariant()}}}";
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_ChangeFlag", rsp, sync);
	}
}
