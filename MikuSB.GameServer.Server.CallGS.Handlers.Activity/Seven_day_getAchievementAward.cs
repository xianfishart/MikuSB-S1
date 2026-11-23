using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Seven_day_getAchievementAward")]
public class Seven_day_getAchievementAward : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject jsonObject = JsonNode.Parse(param)?.AsObject();
		int achievementId = ((jsonObject != null) ? SevenDayProgress.Num(jsonObject, "achievementId") : 0);
		return SevenDayProgress.ClaimAchievement(connection, achievementId);
	}
}
