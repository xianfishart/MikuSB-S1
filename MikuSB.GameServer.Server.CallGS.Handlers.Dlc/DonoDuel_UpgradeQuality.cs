using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_UpgradeQuality")]
public class DonoDuel_UpgradeQuality : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_UpgradeQuality");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		uint uniqueId = DonoDuelGrowth.ReadUint(DonoDuelGrowth.ParseObject(param)?["nUniqueId"]);
		if (!DonoDuelBag.HasCard(player, uniqueId))
		{
			Log.Warn($"Donoduel quality upgrade for unknown card {uniqueId} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		uint quality = DonoDuelBag.GetQuality(player, uniqueId);
		uint target = quality + 1;
		if (target > DonoDuelConfig.QualityMax())
		{
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		foreach (IReadOnlyList<uint> condition in DonoDuelConfig.QualityUnlockConditions(target))
		{
			if (DonoDuelGrowth.ConditionMet(player, condition))
			{
				continue;
			}
			Log.Info($"Donoduel card {uniqueId} quality {target} locked for uid={player.Uid} (condition [{string.Join(',', condition)}]).");
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		uint cost = DonoDuelConfig.QualityCost(target);
		if (cost == 0)
		{
			Log.Warn($"No quality price for target quality {target} (uid={player.Uid}).");
			return DonoDuelGrowth.Bad();
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!PortCurrency.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out string error))
		{
			return DonoDuelGrowth.Bad(error);
		}
		DonoDuelBag.SetQuality(player, uniqueId, target, sync);
		uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
		for (uint position = 1u; position <= 3; position++)
		{
			uint cap = DonoDuelGrowth.AttrCap(templateId, target, position);
			if (cap != 0)
			{
				uint current = DonoDuelBag.GetAttr(player, uniqueId, position);
				if (current > cap)
				{
					DonoDuelBag.SetAttr(player, uniqueId, position, cap, sync);
				}
			}
		}
		// 第 483 节（追加）：潜能（品质）提升同样要推进「养成之路2 绿色潜能」这类任务。
		int grown = DonoDuelTaskState.RecalcGrowthTasks(player, sync);
		if (grown > 0)
		{
			Log.Info($"Donoduel quality upgrade advanced {grown} growth mission(s) for uid={player.Uid}.");
		}
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} quality {quality} -> {target} (cost {cost}) for uid={player.Uid}.");
		return DonoDuelGrowth.Ok();
	}
}
