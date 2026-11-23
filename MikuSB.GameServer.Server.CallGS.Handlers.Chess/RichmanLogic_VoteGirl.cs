using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichmanLogic_VoteGirl")]
public class RichmanLogic_VoteGirl : CallGSHandler<RichmanVoteParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanVoteParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint npcId = request.NpcId;
		bool flag = ((npcId > 8 || npcId == 0) ? true : false);
		if (flag || request.Count == 0)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		ChessActivityService.AddVotes(player, request.NpcId, request.Count, sync);
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nId"] = request.NpcId,
			["nNum"] = request.Count
		}, sync));
	}
}
