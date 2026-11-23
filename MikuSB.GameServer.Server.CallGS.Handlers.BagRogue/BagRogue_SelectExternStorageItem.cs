using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SelectExternStorageItem")]
public class BagRogue_SelectExternStorageItem : CallGSHandler<BagRogueSelectExternParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSelectExternParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num = 0u;
		List<uint> list = request.UniqueIds ?? new List<uint>();
		for (int i = 0; i < list.Count && i < 5; i++)
		{
			num |= (list[i] & 0x3F) << i * 6;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(142u, 16u, num);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
