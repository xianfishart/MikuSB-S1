using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_UpgradeSkill")]
public class DonoDuel_UpgradeSkill : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_UpgradeSkill");

	private const uint SkillLockBitBase = 1u;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject root = DonoDuelGrowth.ParseObject(param);
		uint uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
		uint skillId = DonoDuelGrowth.ReadUint(root?["nSkillId"]);
		uint position = DonoDuelGrowth.ReadUint(root?["nPos"]);
		bool resetAll = DonoDuelGrowth.ReadBool(root?["bResetAll"]);
		if (!DonoDuelBag.HasCard(player, uniqueId))
		{
			Log.Warn($"Donoduel skill learn for unknown card {uniqueId} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
		if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out DonoDuelHeroCardExcel heroCard))
		{
			return DonoDuelGrowth.Bad();
		}
		uint[] skills = new uint[3]
		{
			DonoDuelBag.GetSkill(player, uniqueId, 1),
			DonoDuelBag.GetSkill(player, uniqueId, 2),
			DonoDuelBag.GetSkill(player, uniqueId, 3)
		};
		uint flags = DonoDuelBag.GetFlags(player, uniqueId);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (resetAll)
		{
			uint lockedSlots = 0u;
			for (int slot = 0; slot < 3; slot++)
			{
				if ((flags & (uint)(1 << 1 + slot)) != 0)
				{
					lockedSlots++;
				}
				else
				{
					skills[slot] = 0u;
				}
			}
			uint cost = DonoDuelConfig.SkillReshapingCost(lockedSlots);
			if (cost != 0 && !PortCurrency.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), cost, sync, out string resetError))
			{
				return DonoDuelGrowth.Bad(resetError);
			}
			Log.Info($"Donoduel card {uniqueId} skill reshape ({lockedSlots} locked slot(s), cost {cost}) for uid={player.Uid}.");
		}
		else
		{
			if ((position < 1 || position > 3) ? true : false)
			{
				return DonoDuelGrowth.Bad();
			}
			if (!GameData.DonoDuelHeroCardSkillData.ContainsKey(skillId))
			{
				Log.Warn($"Donoduel skill {skillId} does not exist (uid={player.Uid}).");
				return DonoDuelGrowth.Bad();
			}
			uint star = DonoDuelBag.GetStar(player, uniqueId);
			uint required = heroCard.SkillUnlockStar((int)position);
			if (required != 0 && star < required)
			{
				Log.Info($"Donoduel card {uniqueId} skill slot {position} needs star {required} (has {star}).");
				return DonoDuelGrowth.Bad("error.condition_limit");
			}
			skills[position - 1] = skillId;
		}
		DonoDuelBag.SetSkills(player, uniqueId, skills[0], skills[1], skills[2], sync);
		// 第 492 节：收集任务「技多不压身 1~4」（Type=3 / Objectives=7，累计嵌入技能 N 次）。
		// 客户端 DoUpgradeSkill 只发本请求，不带任何任务包，服务端不计数就永远是 0/N，
		// 右侧恒为「未完成」→ 收集奖励点不动。重塑（bResetAll）不算「嵌入」，不计数。
		if (!resetAll)
		{
			int advanced = DonoDuelTaskState.AdvanceObjectiveMissions(player, DonoDuelTaskState.SkillEmbedObjectiveId, sync);
			if (advanced > 0)
			{
				Log.Info($"Donoduel skill embed advanced {advanced} collect mission(s) for uid={player.Uid}.");
			}
		}
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} skills [{skills[0]}, {skills[1]}, {skills[2]}] {(resetAll ? "(reshaped)" : $"slot {position} learned {skillId}")} for uid={player.Uid}.");
		return DonoDuelGrowth.Ok();
	}
}
