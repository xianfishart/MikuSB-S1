using System.Collections.Generic;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

internal static class DonoDuelTaskState
{
	private const uint GroupId = 186u;

	private const int ProgressShift = 2;

	private const uint ProgressMask = 1073741823u;

	public const uint ArenaClearObjectiveId = 1u;

	public const uint RogueCardObjectiveId = 2u;

	/// <summary>升星 / 潜能（养成之路 301~309、402~405）：数「达到阈值的卡有几张」。</summary>
	public const uint GrowthObjectiveId = 3u;

	public const uint PotentialObjectiveId = 4u;

	/// <summary>
	/// 第 492 节：Objectives=4 的 Param 第二项区分「升星」和「潜能提升」。
	/// 605~608 是「累计提升 N 次星级」(Param=[-1,2])，609~612 是「累计提升 N 次潜能」(Param=[-1,1])。
	/// 两者都挂在 Objectives=4 上，不按 Param 过滤就会互相串进度。
	/// </summary>
	public const int StarUpgradeKind = 2;

	public const int PotentialUpgradeKind = 1;

	/// <summary>收集任务（Type=3，收集详情里的「技多不压身 1~4」）：累计嵌入技能 N 次。</summary>
	public const uint SkillEmbedObjectiveId = 7u;

	/// <summary>收集任务的 Type 值；这批任务在客户端恒被视为「已接取」。</summary>
	public const uint AtlasTaskType = 3u;

	public const uint StoryTalkObjectiveId = 5u;

	public const uint BoostPackObjectiveId = 8u;

	/// <summary>出阵指定卡、在指定关卡里累计击倒 N 个目标（403 / 406）。</summary>
	public const uint ArenaKillObjectiveId = 9u;

	/// <summary>持有数量（613~620：累计拥有 N 张 5 星 / 红色潜能的卡）。</summary>
	public const uint OwnObjectiveId = 6u;

	private const uint StoryTaskType = 1u;

	/// <summary>「Reward3 漏发补偿」一次性标记（185/697，698 已被 DonoDuelStarterDeck 占用）。</summary>
	private const uint Reward3BackfillSid = 697u;

	/// <summary>
	/// 第 484 节补丁上线前「已领奖但一张卡都没发」的任务。
	/// 这批任务的卡奖励只在 Reward3，而旧领奖逻辑只读 Reward1/Reward2。
	/// 按今天的服务端日志核对：这两条在 11:5x 被客户端自动领取（186 = 7），
	/// 当时返回的 tbRewards 是空的，所以既没有卡也没有「获得多诺卡」弹窗。
	/// </summary>
	private static readonly uint[] Reward3UnpaidTasks = new uint[] { 201u, 202u };

	/// <summary>
	/// 第 488 节：「挑战之路1」首通奖励（501，Reward3 = 重型武装兽 ×1）的补偿标记。
	/// 501 的 Reward2 是空串，旧领奖逻辑既不读 Reward2 也不读 Reward3，
	/// 所以 186/501 早早变成 7（已领取）但背包里一张卡都没有 —— 玩家打完挑战模式
	/// 第一关什么都不弹，正是这一条。
	/// </summary>
	private const uint ArenaReward3BackfillSid = 699u;

	private static readonly uint[] ArenaReward3UnpaidTasks = new uint[] { 501u };

	public static uint ReadMissionId(string param)
	{
		try
		{
			uint result;
			return (JsonNode.Parse(param) is JsonObject jsonObject && uint.TryParse(jsonObject["nId"]?.ToString(), out result)) ? result : 0u;
		}
		catch
		{
			return 0u;
		}
	}

	public static uint ReadCheckTaskId(string param)
	{
		try
		{
			uint result;
			return (JsonNode.Parse(param) is JsonObject jsonObject && uint.TryParse(jsonObject["nCheckTaskId"]?.ToString(), out result)) ? result : 0u;
		}
		catch
		{
			return 0u;
		}
	}

	public static List<uint> ReadIdList(string param)
	{
		List<uint> list = new List<uint>();
		try
		{
			if (!(JsonNode.Parse(param) is JsonObject jsonObject) || !(jsonObject["tbIdList"] is JsonArray jsonArray))
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (uint.TryParse(item?.ToString(), out var result) && result != 0)
				{
					list.Add(result);
				}
			}
		}
		catch
		{
		}
		return list;
	}

	public static uint CheckValue(uint id)
	{
		DonoDuelStoryTaskExcel value;
		return (!GameData.DonoDuelStoryTaskData.TryGetValue(id, out value)) ? 1u : ReadUint(value.CheckValue, 1u);
	}

	public static uint GetProgress(PlayerInstance player, uint id)
	{
		return (player.Attributes.GetValue(186u, id) >> 2) & 0x3FFFFFFF;
	}

	public static int AdvanceArenaClearMissions(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		if (levelId == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Objectives) == 1 && ParamsContain(donoDuelStoryTaskExcel2.Param, levelId) && !IsReceived(player, id))
			{
				if (!IsAccepted(player, id))
				{
					MarkAccepted(player, id, sync);
				}
				uint num3 = CheckValue(id);
				if (GetProgress(player, id) < num3)
				{
					MarkProgress(player, id, num3, sync);
				}
				num++;
			}
		}
		return num;
	}

	/// <summary>
	/// 第 489 节：Objectives=9「出阵【指定多诺卡】，在【指定关卡】中累计击倒 N 个敌方目标」。
	///
	/// 现象：任务 403（重型武装兽 / 5518 巅峰模式 / 0/5）打完一局进度不动。
	/// 根因：客户端 <c>DonoDuelArena.Req_LevelSettlement</c> 把本局击杀明细放进
	/// 请求的 <c>tbKill</c>（来源 <c>DonoDuelMgr:GetKillsData()</c>），
	/// 而服务端 <c>DonoDuelArenaSettlement</c> 只读 nID / nStar，tbKill 被整包丢掉，
	/// 属性 186/403 永远是 0 → 客户端 CheckMissionCanFinish 永远为假。
	///
	/// Param 格式：[模板ID（-1 = 任意卡）, 关卡ID]。
	/// 「出阵」按巅峰对决出战阵容（185/14~17）判断，不看整包。
	/// </summary>
	public static int AdvanceArenaKillTasks(PlayerInstance player, uint levelId, int kills, NtfSyncPlayer? sync)
	{
		if (levelId == 0 || kills <= 0)
		{
			return 0;
		}
		bool formationEmpty = MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel.DonoDuelFormation.FormationIsEmpty(player, 2u);
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Objectives) != ArenaKillObjectiveId || IsReceived(player, id))
			{
				continue;
			}
			int paramLevel = ReadParamAt(donoDuelStoryTaskExcel2.Param, 1);
			if (paramLevel > 0 && (uint)paramLevel != levelId)
			{
				continue;
			}
			int paramTemplate = ReadParamAt(donoDuelStoryTaskExcel2.Param, 0);
			if (paramTemplate > 0 && !formationEmpty
				&& !MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel.DonoDuelFormation.FormationHasTemplate(player, 2u, (uint)paramTemplate))
			{
				continue;
			}
			uint preId = ReadUint(donoDuelStoryTaskExcel2.PreId);
			bool accepted = IsAccepted(player, id);
			if (preId == 0)
			{
				if (!accepted)
				{
					continue;
				}
			}
			else if (!IsReceived(player, preId))
			{
				continue;
			}
			uint check = CheckValue(id);
			uint progress = GetProgress(player, id);
			if (progress >= check)
			{
				continue;
			}
			if (!accepted)
			{
				MarkAccepted(player, id, sync);
			}
			long next = (long)progress + kills;
			MarkProgress(player, id, (uint)System.Math.Min(next, (long)check), sync);
			num++;
		}
		return num;
	}

	/// <summary>
	/// 第 456 节：把「已经通关、但任务进度还停在 0」的竞技场关卡补记到剧情任务上。
	///
	/// 客户端 <c>DonoDuelTask:CheckMissionAutoGetAward</c> 只认「状态 = 可提交」的任务，
	/// 而可提交的判定来自属性组 186 的进度位；进度位不上来，就一直不会去
	/// <c>ReqGetReward</c>，也就永远不会用 CartoonMgr 播 <c>sSubmitStory</c> 那段剧情。
	/// 老存档里关卡早打完了，这里按「通关记录（属性 22/关卡）」补记一次。
	/// </summary>
	public static int BackfillPassedArena(PlayerInstance player, NtfSyncPlayer? sync)
	{
		NtfSyncPlayer target = sync ?? new NtfSyncPlayer();
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Objectives) != ArenaClearObjectiveId || IsReceived(player, id))
			{
				continue;
			}
			uint num2 = ParamLevelId(donoDuelStoryTaskExcel2.Param);
			if (num2 == 0 || !player.IsLevelReallyPassed(num2))
			{
				continue;
			}
			if (!IsAccepted(player, id))
			{
				MarkAccepted(player, id, target);
			}
			uint num3 = CheckValue(id);
			if (GetProgress(player, id) < num3)
			{
				MarkProgress(player, id, num3, target);
				num++;
			}
		}
		return num;
	}

	/// <summary>
	/// 第 484 节（补）：把「补丁上线前领过奖、但奖励只在 Reward3」的任务重新打开成可领取，
	/// 让客户端按修好的流程再领一次 —— 卡会真正进背包，同时弹出「获得多诺卡」。
	///
	/// 现象：探索多诺卡打完第 2 阶第 4 区 / 第 3 阶第 4 区，官服会弹「获得多诺卡」
	/// （3 张 / 2 张），本服什么都不弹。那两张不是房间掉落，而是剧情任务
	///   201「到达第2阶第3区」 Reward3 = 3 张
	///   202「到达第3阶第3区」 Reward3 = 2 张
	/// 今天 11:5x 客户端自动领取时（186 = 7），服务端还没发 Reward3，
	/// 返回的 tbRewards 是空的 → 没有卡、也没有弹窗。这里只做一次，进度位不动。
	/// </summary>
	public static int ReopenUnpaidReward3Tasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player.Attributes.GetValue(185u, Reward3BackfillSid) != 0)
		{
			return 0;
		}
		int num = 0;
		foreach (uint id in Reward3UnpaidTasks)
		{
			if (!IsReceived(player, id))
			{
				continue;
			}
			if (!IsAccepted(player, id))
			{
				MarkAccepted(player, id, sync);
			}
			PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, id);
			orCreate.Val &= 0xFFFFFFFDu;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
			num++;
		}
		PlayerAttr attr = player.Attributes.Set(185u, Reward3BackfillSid, 1u);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return num;
	}

	private static uint ParamLevelId(JToken? param)
	{
		return (param is JArray { Count: not 0 } jArray) ? ReadUint(jArray[0]) : 0u;
	}

	/// <summary>
	/// 第 488 节：把「挑战之路1」（关卡 5513）首通任务 501 重新打开成可领取。
	/// 与 201/202 同理：ReopenUnpaidReward3Tasks 用的是 185/697，那批已经执行过，
	/// 这里换 185/699 落闸，只做一次。客户端重新领奖时会真正发卡并弹「获得多诺卡」。
	/// </summary>
	public static int ReopenArenaReward3Tasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player.Attributes.GetValue(185u, ArenaReward3BackfillSid) != 0)
		{
			return 0;
		}
		int num = 0;
		foreach (uint id in ArenaReward3UnpaidTasks)
		{
			if (!IsReceived(player, id))
			{
				continue;
			}
			if (!IsAccepted(player, id))
			{
				MarkAccepted(player, id, sync);
			}
			PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, id);
			orCreate.Val &= 0xFFFFFFFDu;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
			num++;
		}
		PlayerAttr attr = player.Attributes.Set(185u, ArenaReward3BackfillSid, 1u);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return num;
	}

	public static int AdvanceRogueMissions(PlayerInstance player, uint floor, uint area, bool runFinished, NtfSyncPlayer sync)
	{
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Objectives) != 2 || IsReceived(player, id))
			{
				continue;
			}
			// 前置任务没领奖的不能抢跑：108「完成1次多诺卡探索」Pre=107（激活探索加速包），
			// 提前把它标成可提交会整段跳过 104~107 的引导。
			uint preId = ReadUint(donoDuelStoryTaskExcel2.PreId);
			if (preId != 0 && !IsReceived(player, preId))
			{
				continue;
			}
			int num3 = ReadParamAt(donoDuelStoryTaskExcel2.Param, 0);
			int num4 = ReadParamAt(donoDuelStoryTaskExcel2.Param, 1);
			bool num5;
			if (num3 >= 0)
			{
				if (num3 != 0)
				{
					if (floor < (uint)num3)
					{
						continue;
					}
					if (num4 > 0)
					{
						num5 = area >= (uint)num4;
						goto IL_0094;
					}
				}
				goto IL_00a3;
			}
			num5 = runFinished;
			goto IL_0094;
			IL_00a3:
			Complete(player, id, sync);
			num++;
			continue;
			IL_0094:
			if (!num5)
			{
				continue;
			}
			goto IL_00a3;
		}
		return num;
	}

	/// <summary>
	/// 第 470 节：把「潜能提升 / 队长技能」这类由玩家在培养界面上做的动作记到剧情任务上。
	///
	/// 客户端培养界面（umg 的星级提升 / 潜能提升 / 技能重塑）走的是
	/// <c>DonoDuel.DoUpgradeAttr</c> / <c>DoUpgradeLeaderSkill</c>，反汇编确认这两个函数
	/// 只发 <c>DonoDuel_UpgradeAttr</c> / <c>DonoDuel_UpgradeLeaderSkill</c>，
	/// 不会额外发任何任务相关的包；而客户端自己的 CheckMissionCanFinish 只按「已有进度」
	/// 判断（tbFnCompare 里只有 1 号函数），根本不会因为做了动作而涨进度。
	/// 所以进度必须由服务端在收到升级请求时推进，否则
	///   104「前往进行潜能提升 0/1」永远不动 → 105 不会自动接取
	///   → 挂在 105 上的 phase1_04 剧情永远不播，教程走完剧情就断了。
	///
	/// 只推进「前置任务已领奖」或「已经被接取」的任务，避免把未解锁的支线（605~612）抢跑。
	///
	/// 第 492 节：
	///   · paramKind：Objectives=4 上「升星」和「潜能」共用目标号，
	///     调用方必须传 StarUpgradeKind / PotentialUpgradeKind，否则升一次星会把
	///     「累计提升 N 次潜能」也一起加，反之亦然。
	///   · 收集任务（Type=3，601~604 / 605~608 / 609~612）不写 186 的接取位
	///     （客户端 DonoDuelTask:IsAccept 对 BIGTYPE_RIKI 直接 return true），
	///     如果这里还按「已接取」过滤，它们永远不会涨进度 → 收集奖励永远「未完成」。
	/// </summary>
	public static int AdvanceObjectiveMissions(PlayerInstance player, uint objectiveId, NtfSyncPlayer sync, uint step = 1, int paramKind = 0)
	{
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Objectives) != objectiveId || IsReceived(player, id))
			{
				continue;
			}
			if (paramKind != 0 && ReadParamAt(donoDuelStoryTaskExcel2.Param, 1) != paramKind)
			{
				continue;
			}
			uint preId = ReadUint(donoDuelStoryTaskExcel2.PreId);
			bool accepted = IsAccepted(player, id);
			bool atlas = ReadUint(donoDuelStoryTaskExcel2.Type) == AtlasTaskType;
			if (atlas)
			{
				// 收集任务没有前置链，客户端也不发接取请求，服务端直接当成已接取。
			}
			else if (preId == 0)
			{
				// 没有前置的支线任务由客户端自己开启，服务端不主动接。
				if (!accepted)
				{
					continue;
				}
			}
			else if (!IsReceived(player, preId))
			{
				continue;
			}
			uint checkValue = CheckValue(id);
			uint progress = GetProgress(player, id);
			if (progress >= checkValue)
			{
				continue;
			}
			if (!accepted)
			{
				MarkAccepted(player, id, sync);
			}
			uint next = progress + step;
			if (next > checkValue)
			{
				next = checkValue;
			}
			MarkProgress(player, id, next, sync);
			num++;
		}
		return num;
	}

	/// <summary>
	/// 第 483 节（追加）：养成类剧情任务（Objectives=3 升星 / 潜能，Objectives=6 持有数量）
	/// 的进度只能由服务端按背包重算。
	///
	/// 现象：做完「养成之路1」剧情没拿到奖励（气泡弹了卡，背包里没有）。
	/// 根因两条，都要修：
	///   1) 客户端做升星 / 升品质只发 <c>DonoDuel_UpgradeStar</c> / <c>DonoDuel_UpgradeQuality</c>，
	///      不带任何任务包；这两个目标又没有服务端推进，属性 186/301 永远是 0 进度，
	///      客户端 <c>CheckMissionAutoGetAward</c> 认为任务没完成，不会去领奖。
	///   2) 301 这类支线的卡牌奖励放在 Reward3（带星级 / 品质），
	///      而领奖处理只读 Reward1（货币）+ Reward2（卡模板），
	///      所以就算领了也一张卡都不发（301 的 Reward1 恰好是空的 → 完全没有奖励）。
	///
	/// 参数格式（donoduel_story_task.Param）：
	///   [模板ID 或 -1 表示任意卡, 目标星级 或 1 表示不限, 目标品质 或 1 表示不限]
	/// 例：301 = [-1,2,1]（任意 5 张 2 星）、302 = [-1,1,2]（任意 5 张绿色潜能）、
	///     402 = [2,3,1]（指定的重型武装兽 3 星）。
	///
	/// 只推进「玩家已经接取」的任务：没接就替玩家接会让客户端跳过接取剧情。
	/// </summary>
	public static int RecalcGrowthTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		int num = 0;
		uint cardCount = System.Math.Min(player.Attributes.GetValue(185u, 2u), 350u);
		if (cardCount == 0)
		{
			return 0;
		}
		foreach (var (id, donoDuelStoryTaskExcel) in GameData.DonoDuelStoryTaskData)
		{
			uint objectives = ReadUint(donoDuelStoryTaskExcel.Objectives);
			if (objectives != GrowthObjectiveId && objectives != OwnObjectiveId)
			{
				continue;
			}
			if (IsReceived(player, id))
			{
				continue;
			}
			// 第 492 节：收集任务（Type=3，613~620「累计拥有 N 张 5 星 / 红色潜能」）
			// 客户端恒视为已接取，186 里没有接取位，这里不能按接取位过滤。
			if (!IsAccepted(player, id) && ReadUint(donoDuelStoryTaskExcel.Type) != AtlasTaskType)
			{
				continue;
			}
			int templateId = ReadParamAt(donoDuelStoryTaskExcel.Param, 0);
			int star = ReadParamAt(donoDuelStoryTaskExcel.Param, 1);
			int quality = ReadParamAt(donoDuelStoryTaskExcel.Param, 2);
			if (star <= 1 && quality <= 1)
			{
				continue;
			}
			uint need = CheckValue(id);
			uint have = 0u;
			for (uint uniqueId = 1u; uniqueId <= cardCount && have < need; uniqueId++)
			{
				if (DonoDuelBag.GetStar(player, uniqueId) == 0)
				{
					continue;
				}
				if (templateId >= 0 && DonoDuelBag.GetTemplateId(player, uniqueId) != (uint)templateId)
				{
					continue;
				}
				if (star > 1 && DonoDuelBag.GetStar(player, uniqueId) < (uint)star)
				{
					continue;
				}
				if (quality > 1 && DonoDuelBag.GetQuality(player, uniqueId) < (uint)quality)
				{
					continue;
				}
				have++;
			}
			if (have > need)
			{
				have = need;
			}
			if (GetProgress(player, id) < have)
			{
				MarkProgress(player, id, have, sync);
				num++;
			}
		}
		return num;
	}

	/// <summary>
	/// 第 492 节：收集任务里「累计做 N 次」的计数目标补记（老存档用）。
	///
	/// 现象：收集详情一整页（技多不压身 1~4 / 多诺卡培养大师 1~N）全是 0，
	/// 右侧恒为「未完成」，收集奖励点不动。
	/// 根因：601~604 是 Objectives=7（嵌技能次数）、605~608 是 Objectives=4 + Param[-1,2]（升星次数），
	/// 这三个动作客户端都只发自己的请求包（DonoDuel_UpgradeSkill / _UpgradeStar / _UpgradeAttr），
	/// 服务端从来没记过次数，属性 186/60x 永远是 0。
	///
	/// 计数口径（都给下限，宁少不多）：
	///   嵌技能次数 = 背包里非空技能槽的数量；
	///   升星次数   = Σ(星级 - 1)（发卡时星级为 1，每升一次加 1）；
	///   潜能次数   = Σ 三围里「高于 herocard 初始值」的条数（每次潜能提升只加一条）。
	/// </summary>
	public static int BackfillCounterTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint cardCount = System.Math.Min(player.Attributes.GetValue(185u, 2u), 350u);
		if (cardCount == 0)
		{
			return 0;
		}
		uint skills = 0;
		uint starSteps = 0;
		uint potentialSteps = 0;
		for (uint uniqueId = 1u; uniqueId <= cardCount; uniqueId++)
		{
			uint star = DonoDuelBag.GetStar(player, uniqueId);
			if (star == 0)
			{
				continue;
			}
			for (int slot = 1; slot <= 3; slot++)
			{
				if (DonoDuelBag.GetSkill(player, uniqueId, slot) != 0)
				{
					skills++;
				}
			}
			if (star > 1)
			{
				starSteps += star - 1;
			}
			uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
			if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
			{
				continue;
			}
			if (DonoDuelBag.GetAttr(player, uniqueId, 1u) > heroCard.AttackInitial)
			{
				potentialSteps++;
			}
			if (DonoDuelBag.GetAttr(player, uniqueId, 2u) > heroCard.DefenseInitial)
			{
				potentialSteps++;
			}
			if (DonoDuelBag.GetAttr(player, uniqueId, 3u) > heroCard.HealthInitial)
			{
				potentialSteps++;
			}
		}
		int num = 0;
		num += RaiseAtlasCounter(player, SkillEmbedObjectiveId, 0, skills, sync);
		num += RaiseAtlasCounter(player, PotentialObjectiveId, StarUpgradeKind, starSteps, sync);
		num += RaiseAtlasCounter(player, PotentialObjectiveId, PotentialUpgradeKind, potentialSteps, sync);
		return num;
	}

	/// <summary>
	/// 把收集任务的进度抬到 min(已有计数, CheckValue)（只多不少，不会把更高进度改小）。
	/// </summary>
	private static int RaiseAtlasCounter(PlayerInstance player, uint objectiveId, int paramKind, uint value, NtfSyncPlayer? sync)
	{
		if (value == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel.Objectives) != objectiveId || IsReceived(player, id))
			{
				continue;
			}
			if (ReadUint(donoDuelStoryTaskExcel.Type) != AtlasTaskType)
			{
				continue;
			}
			if (paramKind != 0 && ReadParamAt(donoDuelStoryTaskExcel.Param, 1) != paramKind)
			{
				continue;
			}
			uint target = System.Math.Min(value, CheckValue(id));
			if (GetProgress(player, id) >= target)
			{
				continue;
			}
			if (!IsAccepted(player, id))
			{
				MarkAccepted(player, id, sync);
			}
			MarkProgress(player, id, target, sync);
			num++;
		}
		return num;
	}

	/// <summary>
	/// 第 470 节：判断这个存档是否已经做过潜能提升（老存档补记用）。
	///
	/// 发卡时属性被写成 herocard 的 *_Initial（见 DonoDuelBag.Grant），
	/// 之后每升一次潜能只会往上加，所以「属性 &gt; 初始值」就等于「做过潜能提升」。
	/// </summary>
	public static bool HasUpgradedPotential(PlayerInstance player)
	{
		for (uint uniqueId = 1u; uniqueId <= 350; uniqueId++)
		{
			if (DonoDuelBag.GetStar(player, uniqueId) == 0)
			{
				continue;
			}
			uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
			if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
			{
				continue;
			}
			if (DonoDuelBag.GetAttr(player, uniqueId, 1u) > heroCard.AttackInitial
				|| DonoDuelBag.GetAttr(player, uniqueId, 2u) > heroCard.DefenseInitial
				|| DonoDuelBag.GetAttr(player, uniqueId, 3u) > heroCard.HealthInitial)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 第 476 节：判断这个存档是否「已经按培养教程动过手」。
	///
	/// 现象：64107/64108 把升星 / 嵌技能 / 品质 / 潜能提升逐条讲完，玩家照着做完
	/// 升星和嵌技能就退出了培养界面；而剧情任务 104 的目标是「前往进行潜能提升」，
	/// 客户端不会自己涨进度，于是任务栏永远停在 0/1，105 不接取、
	/// 挂在 105 上的 phase1_04 也永远不播 —— 看起来就是「培养教程走完剧情断了」。
	///
	/// 判据（任一成立即视为已培养）：星级 &gt; 1、品质 &gt; 1、任一属性 &gt; 初始值。
	/// 初始卡由 DonoDuelBag.GrantCards 写成 star=1 / quality=1 / *_Initial，
	/// 所以没动过手的存档不会被误判。
	/// </summary>
	public static bool HasGrownCards(PlayerInstance player)
	{
		uint num = System.Math.Min(player.Attributes.GetValue(185u, 2u), 350u);
		for (uint uniqueId = 1u; uniqueId <= num; uniqueId++)
		{
			if (DonoDuelBag.GetStar(player, uniqueId) == 0)
			{
				continue;
			}
			if (DonoDuelBag.GetStar(player, uniqueId) > 1 || DonoDuelBag.GetQuality(player, uniqueId) > 1)
			{
				return true;
			}
			uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
			if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
			{
				continue;
			}
			if (DonoDuelBag.GetAttr(player, uniqueId, 1u) > heroCard.AttackInitial
				|| DonoDuelBag.GetAttr(player, uniqueId, 2u) > heroCard.DefenseInitial
				|| DonoDuelBag.GetAttr(player, uniqueId, 3u) > heroCard.HealthInitial)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 第 477 节：判断这个存档是否已经激活过探索加速包（技能树节点）。
	///
	/// 节点位存在 185/91 起的位图里（见 DonoDuel_StudySkillTree.SidOf/BitOf），
	/// 老存档激活过节点但 107 还停在 0/1，登录时按这个判据补记。
	/// </summary>
	public static bool HasBoostPack(PlayerInstance player)
	{
		for (uint nodeId = 1u; nodeId <= 60u; nodeId++)
		{
			uint sid = 91u + (nodeId - 1u) / 30u;
			int bit = (int)((nodeId - 1u) % 30u);
			if ((player.Attributes.GetValue(185u, sid) & (uint)(1 << bit)) != 0)
			{
				return true;
			}
		}
		return false;
	}

	public static int AdvanceStoryActions(PlayerInstance player, NtfSyncPlayer sync)
	{
		int num = 0;
		foreach (var (id, donoDuelStoryTaskExcel2) in GameData.DonoDuelStoryTaskData)
		{
			if (ReadUint(donoDuelStoryTaskExcel2.Type) != 1 || IsReceived(player, id))
			{
				continue;
			}
			uint num3 = ReadUint(donoDuelStoryTaskExcel2.Objectives);
			if (num3 == 4 || num3 == 5 || num3 == 8)
			{
				uint num4 = ReadUint(donoDuelStoryTaskExcel2.PreId);
				if (num4 != 0 && IsReceived(player, num4))
				{
					Complete(player, id, sync);
					num++;
				}
			}
		}
		return num;
	}

	private static void Complete(PlayerInstance player, uint id, NtfSyncPlayer sync)
	{
		if (!IsAccepted(player, id))
		{
			MarkAccepted(player, id, sync);
		}
		uint num = CheckValue(id);
		if (GetProgress(player, id) < num)
		{
			MarkProgress(player, id, num, sync);
		}
	}

	private static int ReadParamAt(JToken? param, int index)
	{
		if (!(param is JArray jArray) || index >= jArray.Count)
		{
			return 0;
		}
		int result;
		return int.TryParse(jArray[index]?.ToString().Trim().Trim(new char[3] { '[', ']', '"' }), out result) ? result : 0;
	}

	private static bool ParamsContain(JToken? param, uint value)
	{
		if (!(param is JArray { Count: not 0 } jArray))
		{
			return false;
		}
		return ReadUint(jArray[0]) == value;
	}

	public static bool IsAccepted(PlayerInstance player, uint id)
	{
		return (player.Attributes.GetValue(186u, id) & 1) != 0;
	}

	public static bool IsReceived(PlayerInstance player, uint id)
	{
		return (player.Attributes.GetValue(186u, id) & 2) != 0;
	}

	public static void MarkAccepted(PlayerInstance player, uint id, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, id);
		orCreate.Val |= 1u;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, orCreate);
		}
	}

	public static void MarkProgress(PlayerInstance player, uint id, uint progress, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, id);
		orCreate.Val = (orCreate.Val & 3) | ((progress & 0x3FFFFFFF) << 2);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, orCreate);
		}
	}

	public static void MarkReceived(PlayerInstance player, uint id, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, id);
		orCreate.Val |= 2u;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, orCreate);
		}
	}

	public static List<IReadOnlyList<uint>> ReadRewardRows(JToken? node)
	{
		List<IReadOnlyList<uint>> list = new List<IReadOnlyList<uint>>();
		if (!(node is JArray { Count: not 0 } jArray))
		{
			return list;
		}
		if (jArray[0] is JArray)
		{
			foreach (JToken item in jArray)
			{
				if (item is JArray row && TryReadRow(row, out uint[] values))
				{
					list.Add(values);
				}
			}
			return list;
		}
		if (TryReadRow(jArray, out uint[] values2))
		{
			list.Add(values2);
		}
		return list;
	}

	private static bool TryReadRow(JArray row, out uint[] values)
	{
		values = new uint[row.Count];
		for (int i = 0; i < row.Count; i++)
		{
			if (!uint.TryParse(row[i]?.ToString(), out var result))
			{
				return false;
			}
			values[i] = result;
		}
		return values.Length >= 5 && values[4] != 0;
	}

	public static uint ReadUint(JToken? node, uint fallback = 0u)
	{
		string s = node?.ToString().Trim().Trim(new char[3] { '[', ']', '"' }) ?? string.Empty;
		uint result;
		return uint.TryParse(s, out result) ? result : fallback;
	}

	public static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
	}
}
