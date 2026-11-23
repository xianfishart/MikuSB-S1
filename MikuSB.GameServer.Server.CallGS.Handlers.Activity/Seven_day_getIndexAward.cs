using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Seven_day_getIndexAward")]
public class Seven_day_getIndexAward : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject jsonObject = JsonNode.Parse(param)?.AsObject();
		int actId = ((jsonObject != null) ? SevenDayProgress.Num(jsonObject, "actId") : 0);
		int index = ((jsonObject != null) ? SevenDayProgress.Num(jsonObject, "index") : 0);
		return SevenDayProgress.ClaimIndex(connection, actId, index);
	}
}
