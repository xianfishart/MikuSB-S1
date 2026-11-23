using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「接待中心 → 招聘员工」（第 498 节新增）。
///
/// 客户端请求构造（反汇编 sd / ReqRecruit.bin 确认）：
///   function VirCaptureStaff.ReqRecruit(nStaffId)
///     me:CallGS("VirCaptureStaff_Recruit", json.encode({nStaffId = nStaffId}))
/// 之前服务端没有这个 api，点了只会回 ui.TxtNotOpen，员工数永远是 0，
/// 于是蓝大岛主线 101「招聘 1 位员工」交不了、后面接到 102/103 也全断。
///
/// 员工数据落在属性组 162：key = 100 + staffId*20 + offset
///   Level(1) / Flag(2) / WorkBuilding(3)
/// 「拥有」= Level &gt; 0（客户端 GetAllStaffList 就是这么判的）。
///
/// 花费：recruitment_staff.json 的 Money，货币是 operating_data.json 的 Cashid（326 = 55，
/// 蓝图岛现金）。本服还没做「经营产出」，现金来源是 0，所以这里的口径是
/// **够就扣、不够就免单并记 WARN**，否则玩家永远招不到人、主线彻底死锁。
/// </summary>
[CallGSApi("VirCaptureStaff_Recruit")]
public class VirCaptureStaff_Recruit : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureStaffRecruit");

	/// <summary>蓝图岛只有 326 这一张图（VirCaptureBuild.tbMapIndex = {326 = 0}）。</summary>
	private const uint IslandMapId = 326u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		RecruitParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<RecruitParam>(param);
			}
		}
		catch (JsonException)
		{
		}

		uint staffId = req?.StaffId ?? 0u;
		if (staffId == 0u)
		{
			// 招募池里 StaffID = 0 的「随机档位」：服务端替他抽一个没招到的。
			staffId = VirCaptureStaffState.PickRandomFromPool(player, IslandMapId);
			if (staffId == 0u)
			{
				await Send(connection, "{\"sErr\":\"ui.TxtBuildNoStaff\"}");
				return;
			}
		}

		if (!VirCaptureStaffTable.TryGetStaff(staffId, out VirCaptureStaffRow cfg))
		{
			Log.Warn($"[VirCapture] recruit bad staffId={staffId} uid={player.Uid}");
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (VirCaptureStaffState.IsOwned(player, staffId))
		{
			await Send(connection, "{\"sErr\":\"ui.TxtBuildStaffOwned\"}");
			return;
		}

		uint cashId = VirCaptureStaffTable.CashId(IslandMapId);
		uint cost = VirCaptureStaffTable.TryGetRecruit(IslandMapId, staffId, out VirCaptureRecruitRow recruit) ? recruit.Money : 0u;

		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (cost > 0u && cashId != 0u)
		{
			if (PortCurrency.GetBalance(player, cashId) >= cost)
			{
				if (!PortCurrency.TrySpend(player, cashId, cost, sync, out string error))
				{
					await Send(connection, "{\"sErr\":\"" + error + "\"}");
					return;
				}
				Log.Info($"[VirCapture] recruit staff {staffId} cost {cost} cash={cashId} uid={player.Uid}");
			}
			else
			{
				// 经营系统还没实现、现金永远是 0，扣款会把主线彻底卡死，先放行。
				Log.Warn($"[VirCapture] recruit staff {staffId} cost {cost} cash={cashId} but balance={PortCurrency.GetBalance(player, cashId)} -> free pass");
			}
		}

		if (!VirCaptureStaffState.Grant(player, staffId, sync))
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}

		VirCaptureStaffState.Save(player);
		// 第 516 节：招聘/发放员工后刷新「累计聘用 N 位员工」计数任务（Type 6，如 313）。
		VirCaptureBuildTaskState.SyncStaffBuildTasks(player, sync);
		Log.Info($"[VirCapture] recruit ok staffId={staffId} name={cfg.Name} owned={VirCaptureStaffState.OwnedCount(player)} uid={player.Uid}");
		await CallGSRouter.SendScript(connection, "VirCaptureStaff_Recruit", "{}", sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureStaff_Recruit", arg);
	}

	private sealed class RecruitParam
	{
		[JsonPropertyName("nStaffId")]
		public uint StaffId { get; set; }
	}
}
