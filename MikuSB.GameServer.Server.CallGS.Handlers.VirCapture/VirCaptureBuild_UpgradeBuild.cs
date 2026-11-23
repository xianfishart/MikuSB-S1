using System;
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
/// 蓝图岛「建造 / 升级建筑」（第 498 节新增）。
///
/// 客户端：VirCaptureBuild.ReqUpgradeBuild(nId)
///   me:CallGS("VirCaptureBuild_UpgradeBuild", json.encode({nId = nId}))
/// 状态写在属性组 161：161/(10000 + nId*30 + 1) = 等级，0 = 未建造。
///
/// 花费口径（第一版）：building.json 的 BuildLevel[等级-1] 当作蓝图岛现金（货币 55）。
/// 现金够就扣，不够就**放行并记 WARN** —— 经营产出还没做，现金来源是 0，
/// 严格扣费会让主线 103「建造海滩别墅」永远做不了。OrginNeed 那组材料编码还没确认，先不扣。
/// </summary>
[CallGSApi("VirCaptureBuild_UpgradeBuild")]
public class VirCaptureBuild_UpgradeBuild : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildUpgrade");

	private const uint IslandMapId = 326u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		BuildParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<BuildParam>(param);
			}
		}
		catch (JsonException)
		{
		}
		if (req == null || req.Id == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!VirCaptureBuildTable.TryGet(req.Id, out VirCaptureBuildingRow cfg))
		{
			Log.Warn($"[VirCapture] upgrade unknown build {req.Id} uid={player.Uid}");
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint level = VirCaptureBuildState.GetLevel(player, cfg.Id);
		if (level >= cfg.MaxLevel)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtLevelupMax\"}");
			return;
		}
		// 未建造（0 → 1）时才校验解锁条件：building.json 的 Condition 指向 buildingcondition.json，
		// 之前完全不看，任何建筑都能直接建。升级沿用客户端自己的建筑等级条件，这里不重复拦。
		if (level == 0u && !VirCaptureBuildCondition.Check(player, cfg, out string reason))
		{
			Log.Info($"[VirCapture] build {cfg.Id} rejected: {reason} uid={player.Uid}");
			await Send(connection, "{\"sErr\":\"" + reason + "\"}");
			return;
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint next = level + 1u;
		uint cost = cfg.CostToReach(next);
		uint cashId = VirCaptureStaffTable.CashId(IslandMapId);
		bool paid = false;
		// 第 509 节：蓝图币不足不再「放行升级但不扣钱」。
		// 498 节这么做是因为当时经营还没产出、现金恒为 0，严格扣费会把主线 103 卡死；
		// 现在营业已经能正常产出（一次经营上万），继续放行会出现
		// 「界面上写着消耗 336000，点了却一分不扣、余额始终不变」的怪现象。
		// 余额不够就和官服一样直接拒绝，玩家去经营赚钱即可。
		if (cost > 0u && cashId != 0u)
		{
			if (PortCurrency.GetBalance(player, cashId) < cost)
			{
				Log.Info($"[VirCapture] build {cfg.Id} lv{next} cost {cost} cash={cashId} balance={PortCurrency.GetBalance(player, cashId)} -> reject uid={player.Uid}");
				await Send(connection, "{\"sErr\":\"error.gold_not_enough\"}");
				return;
			}
			if (!PortCurrency.TrySpend(player, cashId, cost, sync, out string error))
			{
				await Send(connection, "{\"sErr\":\"" + error + "\"}");
				return;
			}
			paid = true;
		}

		uint newLevel = VirCaptureBuildState.Upgrade(player, cfg, sync);
		// 第 502 节：Type 2 主线任务（「建造 XX」）按建筑实际等级推进，避免「建好了但任务 0/1」把使用全锁住。
		VirCaptureBuildTaskState.AdvanceByBuild(player, cfg.Id, sync);
		// 第 504 节：评级条件（Type 16 建筑等级 / Type 20 累计消耗）与合集 Type 15 同步推进。
		if (paid)
		{
			VirCaptureBuildTaskState.AddSpend(player, cashId, cost, sync);
		}
		VirCaptureBuildTaskState.AdvanceByBuildLevel(player, sync);
		VirCaptureBuildTaskState.AdvanceComposite(player, sync);
		// 第 507 节：升级也会让 Type 19（某建筑 N 级并经营）/Type 27（指标）有机会推进。
		VirCaptureBuildTaskState.SyncDerived(player, sync);
		// 第 516 节：升级可能触发「区域建筑达到 N 级」计数任务（Type 26，如 314 海之滨 3 幢 4 级）。
		VirCaptureBuildTaskState.SyncStaffBuildTasks(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"[VirCapture] build {cfg.Id} -> lv{newLevel} uid={player.Uid}");
		// 第 504 节：客户端 PalManage.UpgradeBuildingLevelRsp 会读回包里的 nId：
		//   nLevel = VirCaptureBuild:GetBuildLevel(tbParam.nId)
		// 老版本回 "{}" → nId=nil → GetBuildLevel(nil)=0 → 不 Trigger 升级事件，
		// 并且 TryPop 里 PlayBuildingSeq(nil) 第一句就 return，回调永远不执行：
		// 画面被 Collapsed(UI.GetTop()) 遮住、HUD 不恢复，「建造序列 + 建造成功」全丢。
		// 补上 nId 之后客户端才会播 PalBuildingLevelSeq%d，播完 SafeOpen("UI_BuiltSuccess", nId)。
		JsonObject reply = new JsonObject
		{
			["nId"] = cfg.Id,
			["nLevel"] = newLevel
		};
		await CallGSRouter.SendScript(connection, "VirCaptureBuild_UpgradeBuild", reply.ToJsonString(), sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureBuild_UpgradeBuild", arg);
	}

	private sealed class BuildParam
	{
		[JsonPropertyName("nId")]
		public uint Id { get; set; }
	}
}
