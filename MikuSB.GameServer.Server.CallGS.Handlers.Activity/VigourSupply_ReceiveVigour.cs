using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("VigourSupply_ReceiveVigour")]
public class VigourSupply_ReceiveVigour : CallGSHandler<VigourSupplyReceiveParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, VigourSupplyReceiveParam request)
	{
		PlayerInstance player = context.Connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!GameData.VigourSupplyData.TryGetValue(request.Id, out VigourSupplyExcel value))
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		if (VigourSupplyService.IsClaimed(player, request.Id))
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtOverGet"));
		}
		if (!VigourSupplyService.InWindow(value, DateTime.Now))
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		JsonArray jsonArray = new JsonArray { 4u, value.VigourNum };
		player.AddCurrency(4u, value.VigourNum, sync);
		VigourSupplyService.MarkClaimed(player, request.Id, sync);
		VigourSupplyService.Seed(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Logger.GetByClassName().Info($"VigourSupply meal {request.Id}: uid={player.Uid} +{value.VigourNum} vigour.");
		return Task.FromResult(CallGSResult.Ok(jsonArray.ToJsonString(), sync, "VigourSupply_ReceiveVigour"));
	}
}
