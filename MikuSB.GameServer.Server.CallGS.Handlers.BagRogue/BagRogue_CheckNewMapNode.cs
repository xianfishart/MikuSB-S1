using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_CheckNewMapNode")]
public class BagRogue_CheckNewMapNode : CallGSHandler
{
	private static readonly Logger Log = new Logger("BagRogue_CheckNewMapNode");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Ok());
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		BagRogueScheduleExcel bagRogueScheduleExcel = BagRogueService.ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			Log.Warn("BagRogue: no period configured, route not generated.");
			return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
		}
		bool flag = BagRogueService.AlignEraMap(player, bagRogueScheduleExcel, sync);
		if (!flag && player.Attributes.GetValue(142u, 4u) == 0)
		{
			BagRogueService.StartRoute(player, bagRogueScheduleExcel, sync);
			flag = true;
		}
		if (!flag && BagRogueService.RepairAdventureNodes(player, sync))
		{
			flag = true;
		}
		if (!flag && BagRogueService.RepairNodeTable(player, sync))
		{
			flag = true;
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
