using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 453 节：第 32 期「绘梦沙盘 → 模拟对抗」进入房间（只做跳转，不做联机对战）。
///
/// 现象：点「模拟对抗」后第一次进去是空白天空 + 角色挂在右上角、中间一直转加载图标；
///       点一次返回再进就正常。原因在客户端进房链路：
///         room:get(OnlineID=94) → Online.CraeteRoom(94, TeamId, false)
///         → 等 "DLC_RMOL_PVP_CREATEROOM_Return" 回包 → Inherit(房间基类) → 摆相机/开场景
///       服务端原先没有这个 handler，回包永远不来，客户端拿不到房间对象就停在半加载状态；
///       退出一次后客户端本地已有缓存，第二次才轮到正常渲染。
///
/// 本 handler 只负责把「房间已建好」告诉客户端，让跳转一次到位：
///   Status     = 1 → NotMatching（未匹配，界面显示「准备启程 / 开始匹配」）
///   SelfStatus = 1 → NotReady（自己未准备）
/// 匹配、准备、开始作战、对战结算等联机玩法逻辑一律不实现（用户明确只修跳转）。
///
/// 回包由 s2c.tbCmds["DLC_RMOL_PVP_CREATEROOM"] 分发，
/// 触发客户端 "DLC_RMOL_PVP_CREATEROOM_Return"(tb)，tb.Status / tb.SelfStatus 即房间状态。
/// </summary>
[CallGSApi("DLC_RMOL_PVP_CREATEROOM")]
public class DLC_RMOL_PVP_CREATEROOM : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_PVPCreate");

	/// <summary>Online.OnlineID = 94（RMOL 模拟对抗）。</summary>
	private const int OnlineId = 94;

	/// <summary>房间状态：1 → "NotMatching"（客户端 Inherit 内部做数字到字符串映射）。</summary>
	private const int StatusNotMatching = 1;

	/// <summary>自己状态：1 → "NotReady"。</summary>
	private const int SelfStatusNotReady = 1;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		Log.Info($"uid={connection.Player?.Uid ?? 0} rmol pvp createreom param={param}");

		JsonObject rsp = new JsonObject
		{
			["OnlineID"] = OnlineId,
			["Status"] = StatusNotMatching,
			["SelfStatus"] = SelfStatusNotReady
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_PVP_CREATEROOM", rsp.ToJsonString());
	}
}
