using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_LockSkill")]
public class DonoDuel_LockSkill : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_LockSkill");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject root = DonoDuelGrowth.ParseObject(param);
		uint uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
		uint position = DonoDuelGrowth.ReadUint(root?["nLockSkillPos"]);
		bool locked = DonoDuelGrowth.ReadBool(root?["bLock"]);
		bool flag = !DonoDuelBag.HasCard(player, uniqueId);
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((position < 1 || position > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			Log.Warn($"Donoduel skill lock for card {uniqueId} slot {position} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		uint flags = DonoDuelBag.GetFlags(player, uniqueId);
		uint bit = (uint)(1 << (int)position);
		flags = (locked ? (flags | bit) : (flags & ~bit));
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelBag.SetFlags(player, uniqueId, flags, sync);
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} skill slot {position} {(locked ? "locked" : "unlocked")} by uid={player.Uid} (flags={flags}).");
		return DonoDuelGrowth.Ok(new JsonObject
		{
			["nUniqueId"] = uniqueId,
			["nLockSkillPos"] = position
		});
	}
}
