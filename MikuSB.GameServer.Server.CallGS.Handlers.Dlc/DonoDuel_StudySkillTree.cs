using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_StudySkillTree")]
public class DonoDuel_StudySkillTree : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_StudySkillTree");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		uint nodeId = DonoDuelGrowth.ReadUint(DonoDuelGrowth.ParseObject(param)?["nSkillId"]);
		if (nodeId == 0 || !GameData.DonoDuelSkillTreeData.TryGetValue(nodeId, out DonoDuelSkillTreeExcel node))
		{
			Log.Warn($"Unknown Donoduel skill tree node {nodeId} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		if (HasNode(player, nodeId))
		{
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		if (node.Prerequisite != 0 && !HasNode(player, node.Prerequisite))
		{
			Log.Info($"Donoduel skill tree node {nodeId} needs node {node.Prerequisite} first (uid={player.Uid}).");
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		var (moneyType, amount) = node.Consume;
		if (amount == 0)
		{
			return DonoDuelGrowth.Bad();
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!PortCurrency.TrySpend(player, (moneyType == 0) ? DonoDuelGrowth.SeasonCurrency(player) : moneyType, amount, sync, out string error))
		{
			return DonoDuelGrowth.Bad(error);
		}
		uint sid = SidOf(nodeId);
		int bit = BitOf(nodeId);
		uint current = player.Attributes.GetValue(185u, sid);
		DonoDuelBag.SetSlot(player, sid, current | (uint)(1 << bit), sync);
		DonoDuelBag.Save(player);
		// 第 477 节：激活探索加速包（技能树节点）要推进 107「激活1个探索加速包」。
		// 客户端 DonoDuelSkillTree 只发本接口，不会自己涨任务进度，
		// 不补这一句 107 会永远停在 0/1，108 不接取、phase1_07 剧情也不播。
		DonoDuelTaskState.AdvanceObjectiveMissions(player, DonoDuelTaskState.BoostPackObjectiveId, sync);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel skill tree node {nodeId} studied (cost {amount}) for uid={player.Uid}.");
		return DonoDuelGrowth.Ok();
	}

	private static bool HasNode(PlayerInstance player, uint nodeId)
	{
		uint value = player.Attributes.GetValue(185u, SidOf(nodeId));
		return (value & (uint)(1 << BitOf(nodeId))) != 0;
	}

	private static uint SidOf(uint nodeId)
	{
		return 91 + (nodeId - 1) / 30;
	}

	private static int BitOf(uint nodeId)
	{
		return (int)((nodeId - 1) % 30);
	}
}
