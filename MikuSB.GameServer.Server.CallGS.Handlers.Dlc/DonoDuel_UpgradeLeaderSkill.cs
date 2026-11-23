using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_UpgradeLeaderSkill")]
public class DonoDuel_UpgradeLeaderSkill : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_UpgradeLeaderSkill");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		uint skillId = DonoDuelGrowth.ReadUint(DonoDuelGrowth.ParseObject(param)?["nSkillId"]);
		if (skillId == 0 || !GameData.DonoDuelLeaderSkillData.TryGetValue(skillId, out DonoDuelLeaderSkillExcel leaderSkill))
		{
			Log.Warn($"Unknown Donoduel leader skill {skillId} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		IReadOnlyList<uint> pairs = leaderSkill.ConditionPairs();
		for (int index = 0; index + 1 < pairs.Count; index += 2)
		{
			uint[] condition = new uint[2]
			{
				pairs[index],
				pairs[index + 1]
			};
			if (!DonoDuelGrowth.ConditionMet(player, condition))
			{
				Log.Info($"Donoduel leader skill {skillId} still locked for uid={player.Uid}.");
				return DonoDuelGrowth.Bad("error.condition_limit");
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelBag.SetSlot(player, 4u, skillId, sync);
		DonoDuelBag.Save(player);
		// 第 470 节：105「携带食指技后和安卡希雅聊聊」的前半段就是装备队长技能，
		// 客户端 DoUpgradeLeaderSkill 同样不带任务包，这里补推进。
		int advanced = DonoDuelTaskState.AdvanceObjectiveMissions(player, DonoDuelTaskState.StoryTalkObjectiveId, sync);
		if (advanced > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"Donoduel leader skill advanced {advanced} story mission(s) for uid={player.Uid}.");
		}
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel leader skill {skillId} equipped by uid={player.Uid}.");
		return DonoDuelGrowth.Ok();
	}
}
