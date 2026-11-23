using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Server.CallGS.Handlers.Chapter;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_EnterLevel")]
public class Daily_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (await ChapterVigor.TryConsume(connection, request, sync, "Daily_EnterLevel"))
		{
			string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
			await CallGSRouter.SendScript(connection, "Daily_EnterLevel", rsp, sync);
		}
	}
}
