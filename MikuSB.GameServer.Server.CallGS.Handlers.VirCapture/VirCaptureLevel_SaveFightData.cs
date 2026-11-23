using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_SaveFightData")]
public class VirCaptureLevel_SaveFightData : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureSaveFightData");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureSaveFightDataParam req = JsonSerializer.Deserialize<VirCaptureSaveFightDataParam>(param);
		if (req == null || req.LevelId == 0 || req.RegionId == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveFightData", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VirCaptureStateHelper.SetPointState(player, (uint)req.LevelId, (uint)req.RegionId, 2u, sync);
		// 第 510 节：剧情触发点（region RegionType 24 / PalCartoonTrigger）不经过劝降，
		// 客户端播完动画只发这一条 SaveFightData，从来不发 VirCaptureTask_DoSomething。
		// 主线 119「岛屿升级」就是 RouteType 24 的 508 点 + Type 18，于是剧情看完任务还是 0/1，
		// 后面 200 整条主线都卡住。这里把绑在该点位的 Type 18 任务直接推到 CheckValue。
		int taskChanged = VirCaptureBuildTaskState.AdvanceByRegion(player, (uint)req.RegionId, sync);
		if (taskChanged > 0)
		{
			VirCaptureBuildTaskState.SyncDerived(player, sync);
			Log.Info($"[VirCapture] fightdata region {req.RegionId} -> {taskChanged} task(s) advanced uid={player.Uid}");
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject response = new JsonObject
		{
			["nLevelID"] = req.LevelId,
			["nRegionId"] = req.RegionId,
			["tbRewards"] = new JsonArray()
		};
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_SaveFightData", response.ToJsonString(), sync);
	}
}
