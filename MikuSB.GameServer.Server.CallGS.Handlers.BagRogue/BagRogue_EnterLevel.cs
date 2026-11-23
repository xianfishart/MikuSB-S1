using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_EnterLevel")]
public class BagRogue_EnterLevel : CallGSHandler<BagRogueEnterLevelParam>
{
	private static readonly Logger Log = new Logger("BagRogue_EnterLevel");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEnterLevelParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		if (!GameData.BagRogueLevelData.ContainsKey(request.Id))
		{
			if (!GameData.BagRogueLevelData.Values.Any((BagRogueLevelExcel level) => level.Id == request.Id))
			{
				Log.Warn($"BagRogue_EnterLevel: stage {request.Id} is not in level.json (rows={GameData.BagRogueLevelData.Count}, keys={string.Join(",", GameData.BagRogueLevelData.Keys.Take(6))}).");
				return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
			}
			Log.Warn($"BagRogue_EnterLevel: stage {request.Id} found by row Id but not by dictionary key (count={GameData.BagRogueLevelData.Count}).");
		}
		uint num = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = num }));
	}
}
