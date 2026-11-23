using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_CheckOpenAct")]
public class BagRogue_CheckOpenAct : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		BagRogueScheduleExcel bagRogueScheduleExcel = ((player == null) ? BagRogueService.CurrentSchedule : BagRogueService.ScheduleOf(player));
		if (player == null || bagRogueScheduleExcel == null)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(142u, 1u, bagRogueScheduleExcel.Id);
		player.Attributes.SyncTo(sync, attr);
		if (BagRogueService.AlignEraMap(player, bagRogueScheduleExcel, sync))
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		if (BagRogueService.RepairAdventureNodes(player, sync))
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		if (player.Attributes.GetValue(142u, 4u) != 0 && BagRogueService.RepairNodeTable(player, sync))
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		if (BagRogueService.EnsureRelics(player, sync))
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		DlcClientPatches.Push(context.Connection, "bagrogue-open");
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["bOpen"] = true,
			["nId"] = bagRogueScheduleExcel.Id,
			["nStartTime"] = num - 3600,
			["nEndTime"] = num + 2592000
		}, sync));
	}
}
