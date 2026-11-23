using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SaveTrialCard")]
public class BagRogue_SaveTrialCard : CallGSHandler<BagRogueTrialCardParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueTrialCardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player != null)
		{
			List<uint> trials = request.Trials;
			if (trials != null && trials.Count > 0)
			{
				uint[] array = new uint[3] { 9u, 10u, 11u };
				NtfSyncPlayer sync = new NtfSyncPlayer();
				for (int i = 0; i < array.Length && i < request.Trials.Count; i++)
				{
					PlayerAttr attr = player.Attributes.Set(142u, array[i], request.Trials[i]);
					player.Attributes.SyncTo(sync, attr);
				}
				DatabaseHelper.SaveDatabaseType(player.Data);
				return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
			}
		}
		return Task.FromResult(CallGSResult.Ok());
	}
}
