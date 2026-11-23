using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_UpdateFormation")]
public class FarmWarLogic_UpdateFormation : CallGSHandler<FarmWarUpdateFormationParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarUpdateFormationParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (!GameData.FarmWarLevelData.ContainsKey(request.LevelId))
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen", "FarmWarLogic_UpdateFormation"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		string json = request.Team?.ToJsonString() ?? "[]";
		FarmWarActivityService.SetFormation(player, request.LevelId, json, sync);
		return Task.FromResult(CallGSResult.Ok("{}", sync, "FarmWarLogic_UpdateFormation"));
	}
}
