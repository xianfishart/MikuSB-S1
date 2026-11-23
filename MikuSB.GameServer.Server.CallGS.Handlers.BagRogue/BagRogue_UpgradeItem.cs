using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_UpgradeItem")]
public class BagRogue_UpgradeItem : CallGSHandler<BagRogueUpgradeParam>
{
	private static readonly Logger Log = new Logger("BagRogue_UpgradeItem");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueUpgradeParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint uniqueA = request.UniqueA;
		uint uniqueB = request.UniqueB;
		if (uniqueA == 0 || uniqueB == 0 || uniqueA == uniqueB || uniqueA > 100 || uniqueB > 100)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint sid = BagRogueService.BagItemSid(uniqueA);
		uint num = BagRogueService.BagItemSid(uniqueB);
		uint value = player.Attributes.GetValue(142u, sid);
		uint value2 = player.Attributes.GetValue(142u, num);
		uint num2 = value & 0xFFFF;
		uint num3 = value2 & 0xFFFF;
		if (num2 == 0 || num3 == 0)
		{
			Log.Info($"BagRogue_UpgradeItem: empty slot a={uniqueA}({num2}) b={uniqueB}({num3}) uid={player.Uid}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint? value3 = BagRogueService.JigsawUpgradeId(num2);
		uint? num4 = BagRogueService.JigsawUpgradeId(num3);
		if (!value3.HasValue || !num4.HasValue)
		{
			Log.Info($"BagRogue_UpgradeItem: max star tpA={num2} tpB={num3} uid={player.Uid}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		(uint, uint)? tuple = BagRogueService.JigsawMeta(num2);
		(uint, uint)? tuple2 = BagRogueService.JigsawMeta(num3);
		if (tuple?.Item1 != tuple2?.Item1)
		{
			Log.Info($"BagRogue_UpgradeItem: subId mismatch tpA={num2}({tuple?.Item1}) tpB={num3}({tuple2?.Item1}) uid={player.Uid}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(142u, sid, (uniqueA << 16) | value3.Value);
		player.Attributes.SyncTo(sync, attr);
		BagRogueService.SetJigsawRikiFlag(player, value3.Value, sync);
		PlayerAttr attr2 = player.Attributes.Set(142u, num, 0u);
		player.Attributes.SyncTo(sync, attr2);
		PlayerAttr attr3 = player.Attributes.Set(142u, num + 1, 0u);
		player.Attributes.SyncTo(sync, attr3);
		PlayerAttr attr4 = player.Attributes.Set(142u, num + 2, 0u);
		player.Attributes.SyncTo(sync, attr4);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"BagRogue_UpgradeItem: slot {uniqueA} tpId {num2}->{value3} consumed slot {uniqueB} tpId {num3} uid={player.Uid}");
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nUniqueA"] = uniqueA,
			["nUniqueB"] = uniqueB
		}, sync));
	}
}
