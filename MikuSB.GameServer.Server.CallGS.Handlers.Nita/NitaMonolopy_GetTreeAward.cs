using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

[CallGSApi("NitaMonolopy_GetTreeAward")]
public class NitaMonolopy_GetTreeAward : CallGSHandler<NitaMonolopyGetTreeAwardParam>
{
	private static readonly Logger Log = new Logger("NitaMonolopy_GetTreeAward");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyGetTreeAwardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		uint index = request.TreeIdx;
		if (index == 0 || index > GameData.NitaTreeData.Count)
		{
			return CallGSResult.Error("error.BadParam");
		}
		NitaTreeExcel node = GameData.NitaTreeData[(int)(index - 1)];
		uint currentEx = player.Attributes.GetValue(159u, 1u);
		if (node.Ex == 0 || node.Ex > currentEx)
		{
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		uint claimSid = 10 + index;
		if (player.Attributes.GetValue(159u, claimSid) != 0)
		{
			return CallGSResult.Error("achievement.RewardGeted");
		}
		List<uint[]> rows = DlcRuntimeOverrides.ReadRewardRows(node.RewardsRaw);
		if (rows.Count == 0)
		{
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		PlayerAttr attr = player.Attributes.Set(159u, claimSid, 1u);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"Nita tree claim: idx={index}, ex={node.Ex}/{currentEx}, {granted.Count} reward row(s) for uid={player.Uid}.");
		return CallGSResult.Ok(new JsonObject
		{
			["nTreeIdx"] = index,
			["tbRewards"] = granted
		}, sync);
	}
}
