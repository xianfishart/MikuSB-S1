using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_QuickGetReward")]
public class RichManLoveLogic_QuickGetReward : CallGSHandler<RichmanIdListParam>
{
	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanIdListParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (uint id in request.Ids)
		{
			if (!GameData.ActivityQuestData.TryGetValue(id, out ActivityQuestExcel quest))
			{
				continue;
			}
			JsonArray result = await AchievementClaimService.TryClaimAsync(rewardRows: AchievementClaimService.ReadRewardRows(quest.Rewards), player: player, groupId: 7u, id: id, checkValue: quest.CheckValue, sync: sync);
			if (result == null)
			{
				continue;
			}
			foreach (JsonNode row in result)
			{
				if (row != null)
				{
					granted.Add(row.DeepClone());
				}
			}
			quest = null;
		}
		return CallGSResult.Ok(new JsonObject { ["tbAwards"] = granted }, sync);
	}
}
