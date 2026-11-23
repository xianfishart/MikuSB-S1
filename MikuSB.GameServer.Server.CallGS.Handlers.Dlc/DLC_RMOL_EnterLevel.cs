using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 447 节：第 32 期「绘梦沙盘 / 沙盒推演」进关。
/// 客户端 rmolformation 点「立即出发」发 DLC_RMOL_EnterLevel
/// {"SkillIDList":[],"Girl":1,"Level":10001,"Session":1}，
/// 服务端原先没有 handler（日志 "No handler for CallGS API: DLC_RMOL_EnterLevel"），
/// 客户端等不到回包 → 按钮无反应。与 Repression_Chapter_EnterLevel 同口径，回 nSeed 即可，
/// 关卡数据（ChessFile / 胜利条件）由客户端本地 _Mod/RMOL 配置读取。
/// </summary>
[CallGSApi("DLC_RMOL_EnterLevel")]
public class DLC_RMOL_EnterLevel : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_EnterLevel");

	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int uid = connection.Player?.Uid ?? 0;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int level = ReadInt(request["Level"], request["level"], request["LevelId"], request["LevelID"]);
		int session = ReadInt(request["Session"], request["session"]);
		int girl = ReadInt(request["Girl"], request["girl"]);
		int seed = Random.Next(1, 1000000000);
		Log.Info($"uid={uid} rmol enter session={session} level={level} girl={girl} seed={seed}");
		// 客户端 s2c.tbCmds["DLC_RMOL_EnterLevel"]（RMOL 回包）：
		//   if tb.Err then UI.ShowTip(tb.Err) end
		//   EventSystem.Trigger("DLC_RMOL_EnterLevel_Return", tb.Session, tb.Level)
		// 而 umg_rmol_formation 的 Enter() 里按 (Session.ID, Level.ID) 校验后才 Map.Open。
		// 所以回包必须是对象且键名为 Session / Level（大小写敏感）。
		JsonObject rsp = new JsonObject
		{
			["Session"] = session,
			["Level"] = level,
			["nSeed"] = seed
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_EnterLevel", rsp.ToJsonString());
	}

	private static int ReadInt(params JsonNode[] nodes)
	{
		foreach (JsonNode node in nodes)
		{
			if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value))
			{
				return (int)value;
			}
		}
		return 0;
	}
}
