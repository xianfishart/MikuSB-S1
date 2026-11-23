using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_EnterLevel")]
public class Chapter_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (await ChapterVigor.TryConsume(connection, request, sync, "Chapter_EnterLevel"))
		{
			string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
			await CallGSRouter.SendScript(connection, "Chapter_EnterLevel", rsp, sync);
		}
	}
}
