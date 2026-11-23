using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_FinishNode")]
public class RogueLogic_FinishNode : CallGSHandler<RogueFinishNodeParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueFinishNodeParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		RogueMapExcel rogueMapExcel = RogueService.FindNode(RogueService.CurrentMap(player), request.Node);
		if (rogueMapExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (request.State == 1)
		{
			RogueService.SetNodeState(player, request.Node, 1u, sync);
			uint num = RogueService.NewSeed();
			DatabaseHelper.SaveDatabaseType(player.Data);
			JsonArray argument = new JsonArray
			{
				new JsonObject { ["nSeed"] = num }
			};
			return Task.FromResult(CallGSResult.Ok(argument, sync));
		}
		RogueService.SetNodeState(player, request.Node, 2u, sync);
		if (rogueMapExcel.Node == 3)
		{
			RogueActivityExcel rogueActivityExcel = RogueService.SelectActivity(DateTime.Now);
			if (rogueActivityExcel != null)
			{
				RogueService.EnsureShopGoods(player, rogueActivityExcel, request.Node, sync);
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonArray argument2 = new JsonArray
		{
			new JsonObject
			{
				["nSeed"] = 0,
				["Awards"] = new JsonArray()
			}
		};
		return Task.FromResult(CallGSResult.Ok(argument2, sync));
	}
}
