using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 异绪消解关卡失败上报。
/// 客户端 GirlRush.Req_LevelFail 只负责上报（REQ_LEVEL_FAIL + ExtendFightLog + ExtendFightDynamicLog），
/// 并不等待回包，而且 s2c.tbCmds 里没有注册 "GirlRush_LevelFail" 命令。
/// 旧实现主动下发同名包会让客户端 s2c.Dispatch 抛
/// "No command named: GirlRush_LevelFail"，所以这里只记录，不再下发。
/// </summary>
[CallGSApi("GirlRush_LevelFail")]
public class GirlRush_LevelFail : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
