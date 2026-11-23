using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_VisitShop")]
public class RogueLogic_VisitShop : CallGSHandler<RogueNodeParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueNodeParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		RogueMapExcel rogueMapExcel = RogueService.FindNode(RogueService.CurrentMap(player), request.Node);
		if (rogueMapExcel == null || rogueMapExcel.Node != 3)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		RogueActivityExcel rogueActivityExcel = RogueService.SelectActivity(DateTime.Now);
		if (rogueActivityExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		RogueService.EnsureShopGoods(player, rogueActivityExcel, request.Node, sync);
		RogueService.SetNodeState(player, request.Node, 1u, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
