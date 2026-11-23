using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTower_EnterLevel")]
public class ClimbTower_EnterLevel : CallGSHandler<ClimbTowerEnterLevelParam>
{
	private static readonly Logger Log = new Logger("ClimbTower_EnterLevel");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerEnterLevelParam request)
	{
		if (request.Id == 0 || !GameData.ClimbTowerBattleData.ContainsKey(request.Id))
		{
			Log.Warn($"ClimbTower_EnterLevel: battle {request.Id} is not in climbtower_battle.json.");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = num }));
	}
}
