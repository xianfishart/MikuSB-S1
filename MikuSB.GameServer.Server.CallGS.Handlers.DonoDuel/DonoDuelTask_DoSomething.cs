using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuelTask_DoSomething")]
public class DonoDuelTask_DoSomething : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint missionId = DonoDuelTaskStore.ReadUint(request["nCheckTaskId"]);
		if (missionId == 0 || !GameData.DonoDuelTaskData.TryGetValue(missionId, out DonoDuelTaskExcel config))
		{
			await CallGSRouter.SendScript(connection, "DonoDuelTask_DoSomething", "{}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelTaskStore.SetAccepted(player, missionId, sync);
		DonoDuelTaskStore.SetProgress(player, missionId, config.CheckValue, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "DonoDuelTask_DoSomething", new JsonObject { ["nId"] = missionId }.ToJsonString(), sync);
	}
}
