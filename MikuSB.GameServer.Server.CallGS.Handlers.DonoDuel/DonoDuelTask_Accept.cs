using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuelTask_Accept")]
public class DonoDuelTask_Accept : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint missionId = DonoDuelTaskStore.ReadUint(request["nId"]);
		if (missionId == 0 || !GameData.DonoDuelTaskData.ContainsKey(missionId))
		{
			await CallGSRouter.SendScript(connection, "DonoDuelTask_Accept", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelTaskStore.SetAccepted(player, missionId, sync);
		// 第 463 节：任务 101 是「首次进入多诺卡」，接取时要把配置里的
		// Reward3 = {{1,1,1},{5,1,1},{9,1,1}} 三张初始卡发进局外背包，
		// 并自动填一次肉鸽编队，否则进关卡后局内背包为空、引导无卡可选。
		if (missionId == 101u)
		{
			MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelStarterDeck.Ensure(player, sync);
		}
		// 第 476 节：104「前往进行潜能提升」是培养教程的最后一环，但客户端教程
		// （64107 升星 / 64108 潜能与技能讲解）走完后不会替玩家点潜能，
		// 任务进度永远是 0，105 就不会接取、phase1_04 剧情也接不上。
		// 只要这个存档在培养界面动过手（升星 / 品质 / 属性 / 潜能任一），
		// 接取 104 时就把这一格补记上，剧情立刻接下去；真没动过手的仍要自己去点。
		if (missionId == 104u
			&& MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.HasGrownCards(player))
		{
			MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceObjectiveMissions(
				player,
				MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialObjectiveId,
				sync, 1u,
				MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialUpgradeKind);
		}
		// 第 483 节（追加）：301~309「养成之路」是数卡（几张 2 星 / 几张绿潜能…），
		// 接取时按背包重算一次，之前已经达标的进度立刻补上，客户端才会自动提交领奖。
		MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.RecalcGrowthTasks(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "DonoDuelTask_Accept", new JsonObject { ["nId"] = missionId }.ToJsonString(), sync);
	}
}
