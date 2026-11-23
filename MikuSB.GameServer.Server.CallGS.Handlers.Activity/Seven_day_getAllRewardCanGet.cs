using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Seven_day_getAllRewardCanGet")]
public class Seven_day_getAllRewardCanGet : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject jsonObject = JsonNode.Parse(param)?.AsObject();
		if (jsonObject == null)
		{
			return Task.CompletedTask;
		}
		return SevenDayProgress.ClaimAll(connection, jsonObject);
	}
}
