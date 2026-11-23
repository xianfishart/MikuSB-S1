using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichmanLogic_ReceiveVoteveReward")]
public class RichmanLogic_ReceiveVoteveReward : CallGSHandler<RichmanVoteRewardParam>
{
	private static readonly Logger Log = new Logger("RichmanLogic_ReceiveVoteveReward");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanVoteRewardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		if (!GameData.ChessRichmanAwardData.TryGetValue(request.NpcId, out ChessRichmanAwardExcel tier))
		{
			return CallGSResult.Error("error.BadParam");
		}
		if (ChessActivityService.IsVoteRewarded(player, request.NpcId))
		{
			return CallGSResult.Error("achievement.RewardGeted");
		}
		if (ChessActivityService.Votes(player, request.NpcId) < tier.Vote)
		{
			Log.Warn($"uid={player.Uid} claimed vote reward {request.NpcId} below the threshold ({ChessActivityService.Votes(player, request.NpcId)}/{tier.Vote}).");
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		IReadOnlyList<uint> award = tier.Award;
		if (award.Count >= 4)
		{
			IReadOnlyList<IReadOnlyList<uint>> rows = new _003C_003Ez__ReadOnlySingleElementList<IReadOnlyList<uint>>(new _003C_003Ez__ReadOnlyArray<uint>(new uint[5]
			{
				award[0],
				award[1],
				award[2],
				award[3],
				1u
			}));
			foreach (JsonNode row in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
			{
				if (row != null)
				{
					granted.Add(row.DeepClone());
				}
			}
		}
		ChessActivityService.MarkVoteRewarded(player, request.NpcId, sync);
		return CallGSResult.Ok(new JsonObject
		{
			["tbAward"] = granted,
			["nId"] = request.NpcId
		}, sync);
	}
}
