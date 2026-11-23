using System.Threading.Tasks;
using MikuSB.GameServer.Game.FarmWar;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Settlement;

[CallGSApi("FarmWarLogic_LevelFail")]
public class FarmWarLogicLevelFailHandler : GenericLevelFailHandler, ICallGSHandler
{
	// 第 380 节（2026-11-17）：失败时客户端同样会把本局见到的培育包状态塞进 "F"，
	// 不记的话百科「正面效果」只有通关局才解锁，很多条目会长期空着。
	public new Task Handle(Connection connection, string param, ushort seqNo)
	{
		if (connection?.Player != null && !string.IsNullOrWhiteSpace(param))
		{
			try
			{
				if (JToken.Parse(param) is JObject obj && obj["F"] is JToken buffs)
				{
					FarmWarActivityService.RecordBuffStates(connection.Player, buffs, null);
				}
			}
			catch
			{
				// 上报包格式异常不影响失败流程本身
			}
		}
		return Task.CompletedTask;
	}
}
