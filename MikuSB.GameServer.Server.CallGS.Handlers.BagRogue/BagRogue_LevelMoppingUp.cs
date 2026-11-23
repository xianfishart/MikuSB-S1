using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_LevelMoppingUp")]
public class BagRogue_LevelMoppingUp : CallGSHandler<BagRogueEnterLevelParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEnterLevelParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		BagRogueNodeExcel bagRogueNodeExcel = BagRogueService.CurrentNode(player);
		JsonArray jsonArray = new JsonArray();
		if (bagRogueNodeExcel != null)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			foreach (List<uint> item in BagRogueService.NodeRewardRows(bagRogueNodeExcel))
			{
				JsonArray jsonArray2 = new JsonArray();
				foreach (uint item2 in item)
				{
					jsonArray2.Add(item2);
				}
				jsonArray.Add(jsonArray2);
			}
			BagRogueService.MarkCurrentNodeDone(player, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbAward"] = jsonArray }, sync));
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbAward"] = jsonArray }));
	}
}
