using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 448 节：第 32 期「绘梦沙盘 / 沙盒推演」中途放弃。
///
///客户端 Level:Giveup 发 {"Level":10001,"tbInternalInfo":{...}}，
/// 服务端原先没有 handler，回包不来 → 客户端一直挂在「连接中」。
/// 放弃不置通关标记，只回 {"Level":...} 让
/// s2c.tbCmds["DLC_RMOL_LevelGiveup"] 触发 "DLC_RMOL_LevelGiveup_Return"。
/// </summary>
[CallGSApi("DLC_RMOL_LevelGiveup")]
public class DLC_RMOL_LevelGiveup : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_LevelGiveup");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int level = ReadInt(request["Level"], request["level"], request["LevelId"], request["LevelID"]);
		Log.Info($"uid={connection.Player?.Uid ?? 0} rmol giveup level={level}");
		JsonObject rsp = new JsonObject
		{
			["Level"] = level
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_LevelGiveup", rsp.ToJsonString());
	}

	private static int ReadInt(params JsonNode?[] nodes)
	{
		foreach (JsonNode? node in nodes)
		{
			if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value))
			{
				return (int)value;
			}
		}
		return 0;
	}
}
