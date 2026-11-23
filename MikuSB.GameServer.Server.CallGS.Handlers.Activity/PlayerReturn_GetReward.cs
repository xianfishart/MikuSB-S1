using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("PlayerReturn_GetReward")]
public class PlayerReturn_GetReward : CallGSHandler<PlayerReturnGetRewardParam>
{
	private static readonly Logger Log = new Logger("PlayerReturn_GetReward");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, PlayerReturnGetRewardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerReturnService.Ensure(player, sync);
		uint actId = PlayerReturnService.GetActId(player);
		if (actId == 0 || (request.ActId != 0 && request.ActId != actId))
		{
			Log.Warn($"Return reward: request for act {request.ActId} but stored act is {actId} for uid={player.Uid}.");
			return CallGSResult.Error("error.BadParam", sync);
		}
		JsonObject payload = new JsonObject { ["bShowReward"] = false };
		JsonArray signDays = new JsonArray();
		switch (request.Type)
		{
		case 1u:
		{
			JsonArray granted3 = await PlayerReturnService.ClaimFirstAsync(player, actId, sync);
			payload["bShowReward"] = granted3.Count > 0;
			payload["tbRewards"] = granted3;
			break;
		}
		case 2u:
			var (granted2, days) = await PlayerReturnService.ClaimSignAsync(player, actId, sync);
			foreach (uint day in days)
			{
				signDays.Add(day);
			}
			payload["tbRewards"] = granted2;
			break;
		case 3u:
		{
			JsonArray granted = await PlayerReturnService.ClaimMissionsAsync(player, actId, request.Quick, request.MissionId, sync);
			payload["bShowReward"] = granted.Count > 0;
			payload["tbRewards"] = granted;
			break;
		}
		default:
			Log.Warn($"Return reward: unknown nType {request.Type} for uid={player.Uid}.");
			return CallGSResult.Error("error.BadParam", sync);
		}
		payload["tbSignDay"] = signDays;
		return CallGSResult.Ok(payload, sync);
	}
}
