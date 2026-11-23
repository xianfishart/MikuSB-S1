using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTask_DoGiveUp")]
public class VirCaptureTask_DoGiveUp : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject jsonObject = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint num = VirCaptureTaskStore.ReadUint(jsonObject["nMissionId"]);
		if (num == 0)
		{
			return CallGSRouter.SendScript(connection, "VirCaptureTask_DoGiveUp", "{\"sError\":\"error.BadParam\"}");
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		// 第 497 节：蓝图岛任务在 163，原实现写死 133，放弃会把「开拓目标」里同号条目清掉。
		uint groupId = VirCaptureBuildTaskTable.TryGet(num, out _) ? VirCaptureBuildTaskTable.GroupId : 133u;
		PlayerAttr orCreate = player.Attributes.GetOrCreate(groupId, num);
		orCreate.Val = 0u;
		player.Attributes.SyncTo(ntfSyncPlayer, orCreate);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return CallGSRouter.SendScript(connection, "VirCaptureTask_DoGiveUp", "{}", ntfSyncPlayer);
	}
}
