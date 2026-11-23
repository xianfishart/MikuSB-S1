using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 453 节：第 32 期「绘梦沙盘 → 模拟对抗」进房时上报自己的出战信息。
///
/// 客户端首次进入房间（rmolformation）会按顺序发三件事：
///   1) ReqOnlineRoom(303)                        —— 建房
///   2) Online_RMOL_UpdatePlayerInfo {Role,Skills} —— 上报自己的角色 / 技能
///   3) ReqOnlineRoomUpdate(313)                  —— 拉房间信息
/// 服务端原先没有第 2 步的 handler（日志 "No handler for CallGS API:
/// Online_RMOL_UpdatePlayerInfo"），回包不来，客户端停在半加载状态：
/// 场景只有天空和水面、角色挂在右上角、中间一直转加载图标；
/// 退出一次再进走的是「房间已存在」的分支（不再发这个包），所以才正常。
///
/// 这里只做回显（自己是什么角色 / 带哪些技能），不涉及匹配、准备、对战、结算。
/// </summary>
[CallGSApi("Online_RMOL_UpdatePlayerInfo")]
public class Online_RMOL_UpdatePlayerInfo : ICallGSHandler
{
	private static readonly Logger Log = new Logger("Online_RMOL_PlayerInfo");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int uid = connection.Player?.Uid ?? 0;
		string name = connection.Player?.Data.Name ?? string.Empty;
		Log.Info($"uid={uid} rmol pvp playerinfo param={param}");

		JsonObject rsp = new JsonObject
		{
			["Uid"] = uid,
			["Name"] = name,
			["Role"] = request["Role"]?.DeepClone() ?? 0,
			["Skills"] = request["Skills"]?.DeepClone() ?? new JsonArray(),
			// 客户端房间界面按 State 判断「准备完毕 / 等待中」，0 = 未准备。
			["State"] = 0,
			["Ready"] = false
		};
		await CallGSRouter.SendScript(connection, "Online_RMOL_UpdatePlayerInfo", rsp.ToJsonString());
	}
}
