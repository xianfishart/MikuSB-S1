using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_EnterLevel")]
public class FarmWarLogic_EnterLevel : CallGSHandler<FarmWarEnterLevelParam>
{
	private static readonly Logger Log = new Logger("FarmWarLogic_EnterLevel");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarEnterLevelParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (!GameData.FarmWarLevelData.ContainsKey(request.Id))
		{
			FarmWarActivityService.LogUnknownStage(Api, request.Id);
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen", "FarmWarLogic_EnterLevel"));
		}
		uint num = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
		Log.Info($"FarmWar enter stage={request.Id} team={request.TeamId} seed={num} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = num }.ToJsonString(), null, "FarmWarLogic_EnterLevel"));
	}
}
