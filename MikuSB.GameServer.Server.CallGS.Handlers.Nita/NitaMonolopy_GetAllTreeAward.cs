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

[CallGSApi("NitaMonolopy_GetAllTreeAward")]
public class NitaMonolopy_GetAllTreeAward : CallGSHandler<NitaMonolopyEmptyParam>
{
	private static readonly Logger Log = new Logger("NitaMonolopy_GetAllTreeAward");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyEmptyParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray claimedIdx = new JsonArray();
		List<uint[]> rows = new List<uint[]>();
		uint currentEx = player.Attributes.GetValue(159u, 1u);
		List<NitaTreeExcel> trees = GameData.NitaTreeData;
		for (int i = 0; i < trees.Count; i++)
		{
			uint nodeIndex = (uint)(i + 1);
			NitaTreeExcel node = trees[i];
			if (node.Ex == 0 || node.Ex > currentEx)
			{
				continue;
			}
			uint claimSid = 10 + nodeIndex;
			if (player.Attributes.GetValue(159u, claimSid) == 0)
			{
				List<uint[]> nodeRows = DlcRuntimeOverrides.ReadRewardRows(node.RewardsRaw);
				if (nodeRows.Count != 0)
				{
					rows.AddRange(nodeRows);
					PlayerAttr attr = player.Attributes.Set(159u, claimSid, 1u);
					player.Attributes.SyncTo(sync, attr);
					claimedIdx.Add(nodeIndex);
				}
			}
		}
		if (claimedIdx.Count == 0)
		{
			return CallGSResult.Ok(new JsonObject
			{
				["tbTreeIdx"] = claimedIdx,
				["tbRewards"] = new JsonArray()
			});
		}
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"Nita tree bulk claim: {claimedIdx.Count} node(s), {granted.Count} reward row(s) for uid={player.Uid}.");
		return CallGSResult.Ok(new JsonObject
		{
			["tbTreeIdx"] = claimedIdx,
			["tbRewards"] = granted
		}, sync);
	}
}
