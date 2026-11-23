using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_ReviveRole")]
public class RogueLogic_ReviveRole : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		RogueActivityExcel rogueActivityExcel = RogueService.SelectActivity(DateTime.Now);
		if (rogueActivityExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject jsonObject = RogueService.ReadObject(player, 1u);
		uint num = RogueService.Field(jsonObject, "nReviveTimes");
		uint num2 = ((rogueActivityExcel.Prices.Count > (int)num) ? rogueActivityExcel.Prices[(int)num] : 0u);
		if (num2 != 0 && !RogueCurrency.TrySpend(player, RogueService.CurrencyId, num2, sync, out string error))
		{
			return Task.FromResult(CallGSResult.Error(error, sync));
		}
		RogueService.ReviveFirstDead(player, sync);
		RogueService.SetField(jsonObject, "nReviveTimes", num + 1);
		RogueService.Write(player, 1u, jsonObject, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
