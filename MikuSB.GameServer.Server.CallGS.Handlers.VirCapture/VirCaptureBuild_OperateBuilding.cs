using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「营业结算」（第 498 节新增）。
///
/// 客户端：VirCaptureBuild.ReqOperateBuilding(nMapId)
///   nMapId 先过一遍 VirCapture.GetLevelID()，再
///   me:CallGS("VirCaptureBuild_OperateBuilding", json.encode({nMapId = ...}))
///
/// 第一版口径：把**所有已建建筑**的 OrginInCome[等级-1] 加总，记到蓝图岛现金（货币 55）上。
/// 员工加成、营业消耗（OpeCost）、每日次数限制都还没做 —— 这几项依赖经营系统，
/// 现在做了也会和官服对不上，先保证「点营业能拿到钱、界面数字会动」。
/// </summary>
[CallGSApi("VirCaptureBuild_OperateBuilding")]
public class VirCaptureBuild_OperateBuilding : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildOperate");

	private const uint IslandMapId = 326u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		OperateParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<OperateParam>(param);
			}
		}
		catch (JsonException)
		{
		}

		uint cashId = VirCaptureStaffTable.CashId(IslandMapId);
		if (cashId == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}

		uint gross = 0u;
		uint cost = 0u;
		int built = 0;
		foreach (VirCaptureBuildingRow cfg in VirCaptureBuildTable.All.Values)
		{
			uint level = VirCaptureBuildState.GetLevel(player, cfg.Id);
			if (level == 0u)
			{
				continue;
			}
			built++;
			gross += cfg.IncomeAt(level);
			cost += cfg.OpeCostAt(level);
		}
		if (built == 0)
		{
			// 一栋楼都没有，没得营业。
			await Send(connection, "{\"sErr\":\"ui.TxtBuildNoBuilding\"}");
			return;
		}

		// 第 503 节：结算面板的「本次经营最终收益」读的是 rsp.Result.nRevenue，
		// 以前回包里没有这个字段，界面那一行就是空白的。这里按「总收入 - 营业消耗」给它。
		uint total = gross > cost ? gross - cost : 0u;

		NtfSyncPlayer sync = new NtfSyncPlayer();
		PortCurrency.Add(player, cashId, total, sync);

		// 第 517 节：经营效果等级（Type 19 Param[2]「卓越级」的判据），随回包带给结算界面。
		uint effectLevel = VirCaptureBuildState.ComputeEffectGrade(player, IslandMapId);

		// 第 503 节：客户端 HUD 的「累计经营」读的是 161/1001（VirCaptureBuild.GetTotalOperate），
		// 每日次数读 161/1002。以前这两格从来没人写，所以不管营业多少次都显示 0，
		// 「完成 N 次经营」的主线任务也永远推不动。
		VirCaptureBuildState.AddOperate(player, sync, out uint totalOperate, out uint dailyOperate);
		int taskChanged = VirCaptureBuildTaskState.AdvanceByOperate(player, IslandMapId, totalOperate, sync);
		// 第 507 节：Type 19「在某栋建筑完成 N 次经营」（主线 112）与后续 Type 27 指标链也在这里推进。
		taskChanged += VirCaptureBuildTaskState.SyncDerived(player, sync);

		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"[VirCapture] operate map={IslandMapId} buildings={built} income={total} (gross={gross} cost={cost}) cash={cashId} effect={effectLevel} totalOperate={totalOperate} daily={dailyOperate} tasks={taskChanged} uid={player.Uid}");

		// 客户端 PalManage.OperateBuildingRsp 需要 nMapId 才会去汇总各区域收入并打开结算界面，
		// 以前回的是 "{}"，界面拿不到地图 ID，只能显示空数据。
		await CallGSRouter.SendScript(connection, "VirCaptureBuild_OperateBuilding", new JsonObject
		{
			["nMapId"] = IslandMapId,
			["nRevenue"] = total,
			["nTotalOperate"] = totalOperate,
			["nDailyOperate"] = dailyOperate,
			["nEffectLevel"] = effectLevel
		}.ToJsonString(), sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureBuild_OperateBuilding", arg);
	}

	private sealed class OperateParam
	{
		[JsonPropertyName("nMapId")]
		public uint MapId { get; set; }
	}
}
