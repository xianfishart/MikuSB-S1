using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_UpgradeAttr")]
public class DonoDuel_UpgradeAttr : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_UpgradeAttr");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject root = DonoDuelGrowth.ParseObject(param);
		uint uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
		uint position = DonoDuelGrowth.ReadUint(root?["nPos"]);
		bool flag = !DonoDuelBag.HasCard(player, uniqueId);
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((position < 1 || position > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			Log.Warn($"Donoduel attribute upgrade for unknown card {uniqueId} pos={position} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		uint quality = DonoDuelBag.GetQuality(player, uniqueId);
		var (cost, gainMin, gainMax) = DonoDuelConfig.AttrGain(position, quality);
		if (cost == 0 || gainMax == 0)
		{
			Log.Warn($"No attribute gain row for quality {quality} pos={position} (uid={player.Uid}).");
			return DonoDuelGrowth.Bad();
		}
		uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
		uint cap = DonoDuelGrowth.AttrCap(templateId, quality, position);
		if (cap == 0)
		{
			Log.Warn($"Hero card {templateId} has no ceiling for pos={position} quality={quality}.");
			return DonoDuelGrowth.Bad();
		}
		uint current = DonoDuelBag.GetAttr(player, uniqueId, position);
		if (current >= cap)
		{
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!PortCurrency.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out string error))
		{
			return DonoDuelGrowth.Bad(error);
		}
		uint gain = (uint)Random.Shared.Next((int)gainMin, (int)(Math.Max(gainMin, gainMax) + 1));
		uint updated = Math.Min(current + gain, cap);
		DonoDuelBag.SetAttr(player, uniqueId, position, updated, sync);
		DonoDuelBag.Save(player);
		// 第 470 节：剧情任务 104「前往进行潜能提升」只有服务端能推进
		// （客户端 DoUpgradeAttr 只发 UpgradeAttr，不带任务包）。
		// 推进后客户端会自己接取 105 并播 phase1_04，剧情才能接下去。
		// 第 492 节：Objectives=4 上「潜能」和「升星」共用目标号，
		// 这里必须只推 Param[1]=1（潜能），否则点一次潜能会把「累计提升星级」也算上。
		int advanced = DonoDuelTaskState.AdvanceObjectiveMissions(player, DonoDuelTaskState.PotentialObjectiveId, sync, 1u, DonoDuelTaskState.PotentialUpgradeKind);
		if (advanced > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"Donoduel potential upgrade advanced {advanced} story mission(s) for uid={player.Uid}.");
		}
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} attribute {position} {current} -> {updated} (+{gain}, cap {cap}, cost {cost}) for uid={player.Uid}.");
		return DonoDuelGrowth.Ok();
	}
}
