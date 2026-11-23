using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「建筑派驻 / 撤下员工」（第 498 节新增）。
///
/// 客户端：VirCaptureBuild.ReqAssignWorker(nId, nStaffId, nStaffState)   （三个参数都必须 &gt; 0）
///   me:CallGS("VirCaptureBuild_AssignWorker", json.encode({nId=.., nStaffId=.., nStaffState=..}))
/// 双向写：
///   161/(10000 + nId*30 + 11 + (slot-1)) = nStaffId   —— 建筑侧岗位
///   162/(100 + nStaffId*20 + 3) = nId                 —— 员工侧「在哪栋楼上班」
/// 客户端 GetStaffWorkBuildingPos(buildId, staffId) 就是在这 5 个岗里找下标。
/// </summary>
[CallGSApi("VirCaptureBuild_AssignWorker")]
public class VirCaptureBuild_AssignWorker : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildAssign");

	/// <summary>客户端传 nStaffState = 1 表示上岗，其它值按撤下处理。</summary>
	private const uint StateAssign = 1u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		AssignParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<AssignParam>(param);
			}
		}
		catch (JsonException)
		{
		}
		if (req == null || req.Id == 0u || req.StaffId == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!VirCaptureBuildTable.TryGet(req.Id, out VirCaptureBuildingRow cfg))
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (VirCaptureBuildState.GetLevel(player, cfg.Id) == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!VirCaptureStaffState.IsOwned(player, req.StaffId))
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint existing = VirCaptureBuildState.FindStaffSlot(player, cfg.Id, req.StaffId);

		if (req.State == StateAssign)
		{
			if (existing != 0u)
			{
				// 已经在本楼，直接回成功。
				await CallGSRouter.SendScript(connection, "VirCaptureBuild_AssignWorker", "{}", sync);
				return;
			}
			uint slot = VirCaptureBuildState.FindEmptySlot(player, cfg.Id);
			if (slot == 0u)
			{
				await Send(connection, "{\"sErr\":\"ui.TxtBuildWorkerFull\"}");
				return;
			}
			VirCaptureBuildState.Write(player, cfg.Id, VirCaptureBuildState.OffWorker1 + slot - 1u, req.StaffId, sync);
			VirCaptureStaffState.WriteField(player, req.StaffId, VirCaptureStaffTable.OffWorkBuilding, cfg.Id, sync);
			Log.Info($"[VirCapture] assign staff {req.StaffId} -> build {cfg.Id} slot {slot} uid={player.Uid}");
		}
		else
		{
			if (existing == 0u)
			{
				await CallGSRouter.SendScript(connection, "VirCaptureBuild_AssignWorker", "{}", sync);
				return;
			}
			VirCaptureBuildState.Write(player, cfg.Id, VirCaptureBuildState.OffWorker1 + existing - 1u, 0u, sync);
			VirCaptureStaffState.WriteField(player, req.StaffId, VirCaptureStaffTable.OffWorkBuilding, 0u, sync);
			Log.Info($"[VirCapture] unassign staff {req.StaffId} from build {cfg.Id} uid={player.Uid}");
		}

		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "VirCaptureBuild_AssignWorker", "{}", sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureBuild_AssignWorker", arg);
	}

	private sealed class AssignParam
	{
		[JsonPropertyName("nId")]
		public uint Id { get; set; }

		[JsonPropertyName("nStaffId")]
		public uint StaffId { get; set; }

		[JsonPropertyName("nStaffState")]
		public uint State { get; set; }
	}
}
