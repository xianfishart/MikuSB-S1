using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「员工管理 → 培训」（升级员工等级，第 514 节新增）。
///
/// 客户端请求构造（反汇编 sd_VirCaptureBuild_ReqUpgrade.bin 确认）：
///   function VirCaptureBuild.ReqUpgrade(nMapId, nStaffId)
///     me:CallGS("VirCaptureStaff_Upgrade", json.encode({nMapId = nMapId, nStaffId = nStaffId}))
/// 之前服务端没有这个 api（只有 Recruit），点培训请求被静默丢弃，界面上毫无反应。
///
/// 回包（s2_tbCmds_VirCaptureStaff_Upgrade.luac）：只取 nStaffId，
/// 调 PalStaffManager:TrySyncStaffData（按 162 组的属性同步刷新整张表）并广播
/// NotifyStaffUpgrade —— 所以属性更新放进随回包的 NtfSyncPlayer 即可，不用额外推。
///
/// 费用：vircapture_staff.json 的 Consume（客户端 GetStaffUpgradeConsume =
/// tbStaffCfg.StaffUpgardeConsumeTb[level]，Lua 1 基 → 服务端 Consume[level-1]），
/// 佩芙 [4608, 25600, 134400]，与按钮上的 4608 对齐。货币 = cashId 55（蓝图币）。
/// 等级上限 = MaxLevel（4），写 162/(100+id*20+1)，客户端 GetStaffLevel 读同一处。
/// </summary>
[CallGSApi("VirCaptureStaff_Upgrade")]
public class VirCaptureStaff_Upgrade : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureStaffUpgrade");

	/// <summary>蓝图岛只有 326 这一张图。</summary>
	private const uint IslandMapId = 326u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		UpgradeParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<UpgradeParam>(param);
			}
		}
		catch (JsonException)
		{
		}

		uint staffId = req?.StaffId ?? 0u;
		if (staffId == 0u || !VirCaptureStaffTable.TryGetStaff(staffId, out VirCaptureStaffRow cfg))
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!VirCaptureStaffState.IsOwned(player, staffId))
		{
			await Send(connection, "{\"sErr\":\"ui.TxtBuildNoStaff\"}");
			return;
		}

		uint level = VirCaptureStaffState.GetLevel(player, staffId);
		if (level >= cfg.MaxLevel)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtBuildStaffMaxLevel\"}");
			return;
		}
		uint costIndex = level - 1u;
		if (costIndex >= (uint)cfg.Consume.Count)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtBuildStaffMaxLevel\"}");
			return;
		}
		uint cost = cfg.Consume[(int)costIndex];

		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (cost > 0u)
		{
			uint cashId = VirCaptureStaffTable.CashId(IslandMapId);
			if (cashId != 0u)
			{
				if (PortCurrency.GetBalance(player, cashId) < cost)
				{
					Log.Info($"[VirCapture] staff {staffId} lv{level}->{level + 1} cost {cost} balance={PortCurrency.GetBalance(player, cashId)} -> not enough uid={player.Uid}");
					await Send(connection, "{\"sErr\":\"error.gold_not_enough\"}");
					return;
				}
				if (!PortCurrency.TrySpend(player, cashId, cost, sync, out string error))
				{
					await Send(connection, "{\"sErr\":\"" + error + "\"}");
					return;
				}
			}
		}

		VirCaptureStaffState.WriteField(player, staffId, VirCaptureStaffTable.OffLevel, level + 1u, sync);
		VirCaptureStaffState.Save(player);
		Log.Info($"[VirCapture] staff {staffId} upgrade lv{level} -> {level + 1} cost={cost} cash={VirCaptureStaffTable.CashId(IslandMapId)} uid={player.Uid}");
		await CallGSRouter.SendScript(connection, "VirCaptureStaff_Upgrade", new JsonObject
		{
			["nStaffId"] = staffId
		}.ToJsonString(), sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureStaff_Upgrade", arg);
	}

	private sealed class UpgradeParam
	{
		[JsonPropertyName("nMapId")]
		public uint MapId { get; set; }

		[JsonPropertyName("nStaffId")]
		public uint StaffId { get; set; }
	}
}
