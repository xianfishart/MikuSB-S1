using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Extra_Mission_OpenTask")]
public class Extra_Mission_OpenTask : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint activityId = ExtraMissionStore.ReadUint(request["nActivityId"]);
		int taskIdx = (int)ExtraMissionStore.ReadUint(request["nTaskIdx"]);
		JsonObject response = new JsonObject
		{
			["nActivityId"] = activityId,
			["nTaskIdx"] = taskIdx,
			["sAPI"] = "Extra_Mission_OpenTask"
		};
		if (activityId == 0 || taskIdx < 1 || taskIdx > 7)
		{
			response["sErr"] = "error.BadParam";
			await CallGSRouter.SendScript(connection, "Extra_Mission_OpenTask", response.ToJsonString());
			return;
		}
		JsonArray pool = ExtraMissionStore.Activity(activityId)?["CustomData1"] as JsonArray;
		if (pool == null || pool.Count < taskIdx)
		{
			response["sErr"] = "ui.TxtNotOpen";
			await CallGSRouter.SendScript(connection, "Extra_Mission_OpenTask", response.ToJsonString());
			return;
		}
		uint questId = ExtraMissionStore.ReadUint(pool[taskIdx - 1]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr slot = player.Attributes.GetOrCreate(50u, ExtraMissionStore.DiySid(activityId, taskIdx));
		slot.Val = questId;
		player.Attributes.SyncTo(sync, slot);
		ExtraMissionStore.SetFlag(player, activityId, taskIdx - 1, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "Extra_Mission_OpenTask", response.ToJsonString(), sync);
	}
}
