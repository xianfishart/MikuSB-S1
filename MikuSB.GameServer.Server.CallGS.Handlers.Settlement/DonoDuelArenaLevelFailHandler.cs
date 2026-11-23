using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Settlement;

/// <summary>
/// 第 489 节：巅峰对决失败结算。
///
/// 客户端 <c>DonoDuelArena.Req_LevelFail</c> 同样带 <c>tbKill</c>，
/// 「累计击倒 N 个目标」这类任务（Objectives=9）失败局也应该计数，
/// 所以失败包不再直接丢空 Ack，而是把击杀数交给
/// <c>DonoDuelArenaSettlement.TrackKills</c> 推进任务。
/// </summary>
[CallGSApi("DonoDuelArena_LevelFail")]
public class DonoDuelArenaLevelFailHandler : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel.DonoDuelFormation.ReadUint(request["nID"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (levelId != 0)
		{
			MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel.DonoDuelArenaSettlement.TrackKills(player, levelId, request, victory: false, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "DonoDuelArena_LevelFail", "{}", sync);
	}
}
