using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛任务状态（属性组 163）。编码与 133 一致：bit0 = 已领奖，bit1..31 = 进度。
///
/// 客户端侧对应 VirCaptureTask.lua：
///   IsFinished(cfg)  = GetBits(attr, …) &gt;= cfg.nCheckValue
///   IsPreFinished    = 所有 tbPreId 的 CheckMissionReward == STATUS_GOT（**已领奖**，不只是完成）
///   GetCurMissionList= 主线/支线/随机里「前置已领 + 配置条件满足」的条目
/// 所以只要前置那条没被领奖，后面整条链在客户端就是不显示的 —— 之前「蓝图岛没有任务」就是这个原因。
/// </summary>
internal static class VirCaptureBuildTaskState
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildTask");

	public const uint Gid = VirCaptureBuildTaskTable.GroupId;

	private const uint ProgressMask = 0x7FFFFFFFu;

	public static uint GetProgress(PlayerInstance player, uint taskId)
	{
		return (player.GetAttr(Gid, taskId) & 0xFFFFFFFEu) >> 1;
	}

	public static bool IsClaimed(PlayerInstance player, uint taskId)
	{
		return (player.GetAttr(Gid, taskId) & 1u) != 0;
	}

	public static bool WriteProgress(PlayerInstance player, uint taskId, uint progress, NtfSyncPlayer? sync)
	{
		if (progress > ProgressMask)
		{
			progress = ProgressMask;
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, taskId);
		uint next = (attr.Val & 1u) | (progress << 1);
		if (next == attr.Val)
		{
			return false;
		}
		attr.Val = next;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return true;
	}

	public static bool MarkClaimed(PlayerInstance player, uint taskId, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, taskId);
		uint next = attr.Val | 1u;
		if (next == attr.Val)
		{
			return false;
		}
		attr.Val = next;
		player.Attributes.SyncTo(sync, attr);
		return true;
	}

	/// <summary>
	/// 引导占位任务：vircapture_task.json 里 Name / Describe / Rewards 全空的那些（例如 100），
	/// 官方是进岛时直接置成「已完成且已领」，否则主线 101 的 IsPreFinished 永远 false，
	/// 整条 76 条主线在客户端一条都不显示。这里在登录/进入新星开拓时补一次，幂等。
	/// </summary>
	public static int EnsurePlaceholderTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (!row.IsPlaceholder || row.PreId.Count != 0)
			{
				continue;
			}
			uint want = (row.CheckValue << 1) | 1u;
			PlayerAttr attr = player.Attributes.GetOrCreate(Gid, row.Id);
			if (attr.Val == want)
			{
				continue;
			}
			attr.Val = want;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 503 节：蓝图岛自己的奖励编码 = 「奖励种类 + 变长参数」，一条可连写多组。
	/// 运行时实证（cash.json 的 GDPL 对上了）：
	///   1 道具： [1, genre, detail, particular, level, count] —— 共 6 个数，
	///            [[1,4,1,6,14,500]] = GDPL(4,1,6,14) ×500 = 蓝图币 500（cash 55）；
	///   2 增益： [2, mapId, buffId]                     —— 3 个；
	///   3 岛等级：[3, mapId, level]                      —— 3 个；
	///   4 送员工：[4, staffId]；
	///   5 招人：  [5, count]；
	///   6 解锁建筑：[6, buildId, ?]。
	/// 502 那版按「固定三元组」拆，把 6 个数的道具奖励拆成 (1,4,1)+(6,14,500)，
	/// 结果 1 被当成「未实现」只记日志 —— 领了任务却一个蓝图币都不到账。
	/// </summary>
	private static List<uint[]>? ParseIslandRewards(JsonArray array)
	{
		List<uint> flat = new List<uint>();
		foreach (JsonNode? node in array)
		{
			if (node is JsonArray inner)
			{
				foreach (JsonNode? cell in inner)
				{
					if (cell == null || !uint.TryParse(cell.ToJsonString().Trim('"'), out uint v))
					{
						return null;
					}
					flat.Add(v);
				}
			}
			else
			{
				if (node == null || !uint.TryParse(node.ToJsonString().Trim('"'), out uint v))
				{
					return null;
				}
				flat.Add(v);
			}
		}
		if (flat.Count == 0)
		{
			return null;
		}
		List<uint[]> groups = new List<uint[]>();
		int i = 0;
		while (i < flat.Count)
		{
			int need = flat[i] switch
			{
				1u => 5,
				2u => 2,
				3u => 2,
				4u => 1,
				5u => 1,
				6u => 2,
				_ => -1
			};
			if (need < 0 || i + need >= flat.Count)
			{
				return null;
			}
			uint[] group = new uint[need + 1];
			for (int k = 0; k <= need; k++)
			{
				group[k] = flat[i + k];
			}
			groups.Add(group);
			i += need + 1;
		}
		return groups;
	}

	/// <summary>发奖：数组式（种类+变长参数）与对象式（{"4":121} 这种）分开处理。</summary>
	public static async Task<JsonArray> GrantAsync(PlayerInstance player, VirCaptureBuildTaskRow row, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		if (row.Rewards is JsonArray array)
		{
			List<uint[]>? groups = ParseIslandRewards(array);
			if (groups != null)
			{
				foreach (uint[] g in groups)
				{
					uint kind = g[0];
					switch (kind)
					{
					case 1u:
					{
						// [1, genre, detail, particular, level, count]
						RewardManager.RewardEntry entry = new RewardManager.RewardEntry(g[1], g[2], g[3], g[4], g[5]);
						if (await player.RewardManager.GrantRewardAsync(entry, sync))
						{
							granted.Add(entry.ToJson());
							Log.Info($"[VirCapture] task {row.Id} reward item gdpl={g[1]},{g[2]},{g[3]},{g[4]} x{g[5]} uid={player.Uid}");
						}
						else
						{
							Log.Warn($"[VirCapture] task {row.Id} reward item gdpl={g[1]},{g[2]},{g[3]},{g[4]} x{g[5]} grant failed.");
						}
						break;
					}
					case 2u:
						// 岛内全局增益：161 的 GlobalBuff1..20（偏移 11..30）。buffId → 槽位还没对上，先只记。
						Log.Warn($"[VirCapture] task {row.Id} reward buff map={g[1]} buff={g[2]} not implemented.");
						break;
					case 3u:
						// 岛等级 +N：属性组 128 / GetMapDataStartId(mapId)+Off_MapLevel(8)。
						VirCaptureLevelState.UpgradeMapLevel(player, g[1], g[2], sync);
						granted.Add(new JsonObject
						{
							["nId"] = g[1],
							["nType"] = 3
						});
						break;
					case 4u:
						if (g[1] != 0u && VirCaptureStaffState.Grant(player, g[1], sync))
						{
							granted.Add(new JsonObject
							{
								["nId"] = g[1],
								["nType"] = 4
							});
						}
						break;
					case 5u:
						for (uint i = 0; i < g[1]; i++)
						{
							uint staffId = VirCaptureStaffState.PickRandomFromPool(player, 326u);
							if (staffId == 0u || !VirCaptureStaffState.Grant(player, staffId, sync))
							{
								break;
							}
							granted.Add(new JsonObject
							{
								["nId"] = staffId,
								["nType"] = 5
							});
						}
						break;
					default:
						Log.Warn($"[VirCapture] task {row.Id} reward kind={kind} args=({string.Join(",", g)}) not implemented.");
						break;
					}
				}
				return granted;
			}
			List<List<uint>> rows = RewardText.Parse(array.ToJsonString());
			JsonArray result = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
			if (result != null)
			{
				foreach (JsonNode? node in result)
				{
					granted.Add(node?.DeepClone());
				}
			}
			return granted;
		}
		if (row.Rewards is JsonObject obj)
		{
			foreach (KeyValuePair<string, JsonNode?> pair in obj)
			{
				if (!uint.TryParse(pair.Key, out uint kind))
				{
					continue;
				}
				uint value = (uint)(pair.Value?.GetValue<int>() ?? 0);
				if (value == 0)
				{
					continue;
				}
				if (kind == 1u)
				{
					// 奖励类型 1 = 道具：value 是道具 ID，数量 1。
					List<List<uint>> rows = RewardText.Parse("[[1," + value + ",1]]");
					JsonArray result = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
					if (result != null)
					{
						foreach (JsonNode? node in result)
						{
							granted.Add(node?.DeepClone());
						}
					}
				}
				else if (kind == 4u)
				{
					// 奖励类型 4 = 免费员工：value 是员工 ID（主线 101 就是 {"4":121}）。
					// 「新到岗员工」那个提示框显示的就是它。
					if (VirCaptureStaffState.Grant(player, value, sync))
					{
						JsonObject got = new JsonObject
						{
							["nId"] = value,
							["nType"] = 4
						};
						granted.Add(got);
					}
				}
				else if (kind == 5u)
				{
					// 奖励类型 5 = 招募（随机出 value 名）。
					for (uint i = 0; i < value; i++)
					{
						uint staffId = VirCaptureStaffState.PickRandomFromPool(player, 326u);
						if (staffId == 0u || !VirCaptureStaffState.Grant(player, staffId, sync))
						{
							break;
						}
						JsonObject got = new JsonObject
						{
							["nId"] = staffId,
							["nType"] = 5
						};
						granted.Add(got);
					}
				}
				else
				{
					// 2 增益 / 3 岛等级 / 6 解锁建筑：都是蓝图岛自己的系统，
					// 本服还没有对应实现，先只记日志，避免发错东西。
					Log.Warn($"[VirCapture] task {row.Id} reward kind={kind} value={value} not implemented (island system).");
				}
			}
		}
		return granted;
	}

	/// <summary>
	/// 第 500 节：蓝图岛的「到达/劝降点位」任务（Type 18）在客户端的 Param 里绑着一个点位，
	/// 例如主线 101 → Param [326, 512]、QuestTarget {326:512}。
	/// 玩家在点位上完成劝降后客户端本该自己上报 DoSomething，但劝降请求一旦被服务端拒掉，
	/// 上报链就断了（就是「点劝降弹请求参数错误、任务永远 0/1」）。
	/// 这里在捕获成功时兜一层：把绑在该点位上的任务直接推到 CheckValue。
	/// 每个点位只绑一条任务（116 个点位无一重复），并且要求前置已领，不会串任务。
	/// </summary>
	public static int AdvanceByRegion(PlayerInstance player, uint regionId, NtfSyncPlayer? sync)
	{
		if (regionId == 0u)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 18u || row.Param.Count < 2)
			{
				continue;
			}
			if (row.Param[0] != 326u || row.Param[1] != regionId)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			bool preOk = true;
			foreach (uint pre in row.PreId)
			{
				if (pre != 0u && !IsClaimed(player, pre))
				{
					preOk = false;
					break;
				}
			}
			if (!preOk)
			{
				continue;
			}
			WriteProgress(player, row.Id, row.CheckValue, sync);
			changed++;
		}
		return changed;
	}

	/// <summary>
	/// 第 511 节：Type 7 = 「送达型」隐藏任务。
	/// Param[0] 是触发它的主线任务 ID；自身没有文案、没有 PreID，奖励是对象式 {"4": 员工 ID}：
	///   122 ← 119「岛屿升级」剧情  → 员工 401 佩芙（官服过完剧情就弹「员工已到岗」那张卡）
	///   230 ← 227 剧情            → 员工 402 铠恩
	///   328 ← 325 剧情            → 员工 403 潘克
	/// 本服原来一条都没写，所以剧情演完什么都没给。这里只把进度推到 CheckValue，
	/// 领取走正常的 VirCaptureTask_GetReward（tbRewards 里有 nType=4，客户端才弹员工卡）。
	/// triggerTaskId = 0 表示全表扫一遍（登录自愈用）。
	/// </summary>
	public static int AdvanceTriggerTasks(PlayerInstance player, uint triggerTaskId, NtfSyncPlayer? sync)
	{
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 7u || row.Param.Count < 1)
			{
				continue;
			}
			uint trigger = row.Param[0];
			if (trigger == 0u || (triggerTaskId != 0u && trigger != triggerTaskId))
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!IsClaimed(player, trigger))
			{
				continue;
			}
			if (!WriteProgress(player, row.Id, row.CheckValue, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} (trigger {trigger} claimed) -> {row.CheckValue}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		return changed;
	}

	/// <summary>
	/// 第 511 节：Type 7 的奖励要「送达」给玩家。
	/// 这类任务没有卡面、玩家点不到领取，所以由服务端代领：
	/// 满足条件就写完成 → 发奖 → 标记已领，并把发出去的东西返回给调用方。
	/// GetReward 会把它们并进同一条回包（客户端据此弹「员工已到岗」），
	/// 登录同步就只做到账（属性同步 162，员工列表里能看到人）。
	/// </summary>
	public static async Task<JsonArray> ClaimTriggerTasksAsync(PlayerInstance player, uint triggerTaskId, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 7u || row.Param.Count < 1)
			{
				continue;
			}
			uint trigger = row.Param[0];
			if (trigger == 0u || (triggerTaskId != 0u && trigger != triggerTaskId))
			{
				continue;
			}
			if (IsClaimed(player, row.Id))
			{
				continue;
			}
			if (!IsClaimed(player, trigger))
			{
				continue;
			}
			WriteProgress(player, row.Id, row.CheckValue, sync);
			JsonArray rows;
			try
			{
				rows = await GrantAsync(player, row, sync);
			}
			catch (System.Exception ex)
			{
				// 登录早期 RewardManager 还没就绪时可能抛，别让整条 CheckOpenAct 跟着挂掉，
				// 下次登录/领奖还会再试一次（这条任务此时仍未标记已领）。
				Log.Warn($"[VirCapture] task {row.Id} auto claim failed: {ex.Message}");
				continue;
			}
			MarkClaimed(player, row.Id, sync);
			foreach (JsonNode? node in rows)
			{
				granted.Add(node?.DeepClone());
			}
			Log.Info($"[VirCapture] task {row.Id} (trigger {trigger}) auto claimed, rewards={rows.ToJsonString()} uid={player.Uid}");
		}
		return granted;
	}

	/// <summary>前置任务是否全部已领奖（客户端的 IsPreFinished 口径）。</summary>
	public static bool PreClaimed(PlayerInstance player, VirCaptureBuildTaskRow row)
	{
		foreach (uint pre in row.PreId)
		{
			if (pre != 0u && !IsClaimed(player, pre))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// 第 502 节：Type 2 = 「建造某栋建筑」（Param[0] = 建筑 ID，CheckValue = 需要达到的等级，主线那 10 条都是 1）。
	/// 客户端只在自己完整走完建造流程时才上报进度；调试/直接调用建出来的建筑不会推进，
	/// 而这类任务（如主线 103「前往建造海滩别墅」）带 bForbidOperate=true，会把所有建筑的「使用」锁死，
	/// 于是「建筑已建好但任务 0/1、任务又禁止使用建筑」形成死循环。这里按 161 实际等级兜底推进。
	/// </summary>
	public static int AdvanceByBuild(PlayerInstance player, uint buildId, NtfSyncPlayer? sync)
	{
		if (buildId == 0u)
		{
			return 0;
		}
		uint level = VirCaptureBuildState.GetLevel(player, buildId);
		if (level == 0u)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 2u || row.Param.Count < 1)
			{
				continue;
			}
			if (row.Param[0] != buildId)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (level < row.CheckValue || !PreClaimed(player, row))
			{
				continue;
			}
			WriteProgress(player, row.Id, row.CheckValue, sync);
			Log.Info($"[VirCapture] task {row.Id} (build {buildId} lv{level}) -> {row.CheckValue}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 504 节：Type 16 = 「某栋建筑达到 N 级」的评级条件（主线 107 的子任务 108/109）。
	/// 108 = 建筑 1（海滩别墅）达到 4 级、109 = 建筑 2（豪华餐厅）达到 4 级。
	/// 客户端 HUD 上「岛屿升级条件：海滩别墅达到 0/4 级」读的就是 163/108 的进度；
	/// 建筑等级在 161 里已经正确（10031=5），但没人把等级写进任务进度，
	/// 所以建筑升到 5 级、条件还是 0/4，评级嘉奖永远凑不齐。
	/// 这里按 161 实际等级兜底推进（不要求建筑刚好升到目标级，登岛补一次即可对齐）。
	/// </summary>
	public static int AdvanceByBuildLevel(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 16u || row.Param.Count < 1 || row.Param[0] == 0u)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			uint level = VirCaptureBuildState.GetLevel(player, row.Param[0]);
			uint next = System.Math.Min(row.CheckValue, level);
			if (next == 0u || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} (build {row.Param[0]} lv{level}) -> {next}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 504 节：Type 20 = 「累计消耗 N 蓝图币」（主线 110，Param[0] = 货币 ID 55，CheckValue = 38000）。
	/// 客户端没有本地累计，服务端也没记过账，所以永远 0/38000。
	/// 记账口径 = 建筑建造/升级实际扣掉的钱；另外登录时用 161 里的等级反推一次（每级 BuildLevel 花费之和），
	/// 让已经升过级的存档也能把进度补上。
	/// </summary>
	public static int AddSpend(PlayerInstance player, uint cashId, uint amount, NtfSyncPlayer? sync)
	{
		if (player == null || cashId == 0u || amount == 0u)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 20u || row.Param.Count < 1 || row.Param[0] != cashId)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || !PreClaimed(player, row))
			{
				continue;
			}
			uint progress = GetProgress(player, row.Id);
			if (progress >= row.CheckValue)
			{
				continue;
			}
			uint next = System.Math.Min(row.CheckValue, progress + amount);
			if (next == progress || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} spend {progress}+{amount} -> {next}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>按 161 里的建筑等级推算「已消耗的蓝图币」，用于补登历史进度（只补不退）。</summary>
	public static uint EstimateBuildSpend(PlayerInstance player, uint cashId)
	{
		uint spent = 0u;
		foreach (VirCaptureBuildingRow cfg in VirCaptureBuildTable.All.Values)
		{
			if (cfg.Id == 0u)
			{
				continue;
			}
			uint level = VirCaptureBuildState.GetLevel(player, cfg.Id);
			for (uint lv = 2u; lv <= level; lv++)
			{
				spent += cfg.CostToReach(lv);
			}
		}
		return spent;
	}

	/// <summary>登录 / 进岛时把 Type 20 的累计消耗按建筑等级补齐（幂等，只增不减）。</summary>
	public static int SyncSpendTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		uint cashId = VirCaptureStaffTable.CashId(326u);
		if (cashId == 0u)
		{
			return 0;
		}
		uint spent = EstimateBuildSpend(player, cashId);
		if (spent == 0u)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 20u || row.Param.Count < 1 || row.Param[0] != cashId)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || !PreClaimed(player, row))
			{
				continue;
			}
			uint progress = GetProgress(player, row.Id);
			uint next = System.Math.Min(row.CheckValue, spent);
			if (next <= progress || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} spend restored -> {next}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 504 节：Type 15 = 合集任务（Param 全是子任务 ID，CheckValue = 子任务个数）。
	/// 主线 107「评级嘉奖」= 108+109+110，HUD 上那三条「岛屿升级条件」就是它的子任务。
	/// 子任务由上面的类型推进，合集本身按「已完成子任务数」写进度，否则凑齐了也不会变成可领。
	/// </summary>
	public static int AdvanceComposite(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 15u || row.Param.Count == 0)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			uint done = 0u;
			foreach (uint sub in row.Param)
			{
				if (sub != 0u && VirCaptureBuildTaskTable.TryGet(sub, out VirCaptureBuildTaskRow subRow)
					&& GetProgress(player, sub) >= subRow.CheckValue)
				{
					done++;
				}
			}
			uint next = System.Math.Min(row.CheckValue, done);
			if (next == 0u || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} composite -> {next}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>把「建筑已经建好」的 Type 2 任务全部补一遍（登录 / 进岛时调用，幂等）。</summary>
	public static int SyncBuildTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		HashSet<uint> buildIds = new HashSet<uint>();
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type == 2u && row.Param.Count >= 1 && row.Param[0] != 0u)
			{
				buildIds.Add(row.Param[0]);
			}
		}
		int changed = 0;
		foreach (uint buildId in buildIds)
		{
			changed += AdvanceByBuild(player, buildId, sync);
		}
		return changed;
	}

	/// <summary>
	/// 第 503 节：Type 12 = 「完成 N 次经营」（Param[0] = 地图 ID，CheckValue = 次数）。
	/// 客户端对 Type 12 既不发 DoSomething，也没有本地判定（CheckMissionCanFinish 只在对话/花钱类里调），
	/// 所以「开始经营」永远停在 0/1、也领不到奖。这里按 161 的 TotalOperate 兜底推进，
	/// 口径是「累计经营次数」而不是单次动作（和客户端 GetTotalOperate 一致）。
	/// </summary>
	public static int AdvanceByOperate(PlayerInstance player, uint mapId, uint totalOperate, NtfSyncPlayer? sync)
	{
		if (mapId == 0u || totalOperate == 0u)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 12u || row.Param.Count < 1 || row.Param[0] != mapId)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			uint next = System.Math.Min(row.CheckValue, totalOperate);
			if (next == 0u)
			{
				continue;
			}
			if (WriteProgress(player, row.Id, next, sync))
			{
				Log.Info($"[VirCapture] task {row.Id} (operate {next}/{row.CheckValue}) uid={player.Uid}");
				changed++;
			}
			else if (GetProgress(player, row.Id) < next)
			{
				changed++;
			}
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>登录 / 进岛时按已有的累计经营次数补一遍 Type 12 任务（幂等）。</summary>
	public static int SyncOperateTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		uint total = VirCaptureBuildState.GetTotalOperate(player);
		if (total == 0u)
		{
			return 0;
		}
		return AdvanceByOperate(player, 326u, total, sync);
	}

	/// <summary>
	/// 第 507 节：Type 19 = 「在某栋建筑完成 N 次经营」（主线 112「一个大目标？」就是它）。
	/// Param = [建筑ID, 目标等级(配置里 -1 表示不限，读进内存是 0), 经营效果等级(0 = 不限)]，
	/// 文案见 TxtBuildMain1_9_Activedes「完成1次经营」、TxtBuildExtra1_Activedes「…完成后完成{1}/{2}次经营」。
	///
	/// 客户端对 Type 19（tbMissionType[19].Type = 9）只在「经营结算」里比对参数后上报，
	/// 而本服的营业是整岛结算（VirCaptureBuild_OperateBuilding 只收 nMapId），
	/// 一次营业等于所有已建建筑各营业一次，所以这里按「累计经营次数 + 建筑等级」兜底：
	/// 只要对应建筑已建成、等级满足、且累计经营次数 &gt;= 1，就把这条直接推到 CheckValue。
	/// 玩家已经营业过 5 次但 163/112 一直是 0，主线 112→123→113→… 整段卡死，就是缺这段。
	/// 第 517 节：Param[2]（经营效果等级）接入 VirCaptureBuildState.ComputeEffectGrade：
	/// 4001~4011 要求「卓越级」（Param[2] = 2/3），员工/建筑两侧进度条任一侧不到就不推进。
	/// </summary>
	public static int AdvanceByOperateBuilding(PlayerInstance player, uint totalOperate, NtfSyncPlayer? sync)
	{
		if (player == null || totalOperate == 0u)
		{
			return 0;
		}
		int changed = 0;
		uint effectGrade = 0u;
		bool gradeComputed = false;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 19u || row.Param.Count < 1 || row.Param[0] == 0u)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			uint needLevel = (row.Param.Count > 1) ? row.Param[1] : 0u;
			uint needQuality = (row.Param.Count > 2) ? row.Param[2] : 0u;
			if (needQuality != 0u)
			{
				if (!gradeComputed)
				{
					effectGrade = VirCaptureBuildState.ComputeEffectGrade(player);
					gradeComputed = true;
				}
				if (effectGrade < needQuality)
				{
					continue;
				}
			}
			uint level = VirCaptureBuildState.GetLevel(player, row.Param[0]);
			if (level == 0u || (needLevel != 0u && level < needLevel))
			{
				continue;
			}
			if (WriteProgress(player, row.Id, row.CheckValue, sync))
			{
				Log.Info($"[VirCapture] task {row.Id} (operate build {row.Param[0]} lv{level} total={totalOperate}) -> {row.CheckValue}/{row.CheckValue} uid={player.Uid}");
				changed++;
			}
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 507 节：Type 27 = 「完成 N 次岛屿评级指标」（主线 113「岛屿评级指标」→ 传说中的厨师那条线）。
	/// 「岛屿评级指标」就是 BigType 4（额外任务）那一批，例如 1001「岛屿评级指标1」
	/// （在轻风广场建造海滩别墅后完成 1 次经营）、1002「岛屿评级指标2」（豪华餐厅）。
	/// Param[0] 就是 4 = BIGTYPE_EXTRA，CheckValue = 需要完成的指标个数（1/2/3/4）。
	/// 客户端只在指标面板里手动上报，本服没接那条链路，所以这里按「已完成的额外任务数」兜底。
	/// </summary>
	public static int AdvanceByIndicator(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 27u || row.Param.Count < 1 || row.Param[0] == 0u)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			uint done = 0u;
			foreach (VirCaptureBuildTaskRow extra in VirCaptureBuildTaskTable.All.Values)
			{
				if (extra.BigType != row.Param[0] || extra.Id == row.Id)
				{
					continue;
				}
				if (GetProgress(player, extra.Id) >= extra.CheckValue)
				{
					done++;
				}
			}
			uint next = System.Math.Min(row.CheckValue, done);
			if (next == 0u || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} indicator -> {next}/{row.CheckValue} (extra done={done}) uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 507 节：把所有「派生型」任务串起来跑（经营 / 建筑等级 / 合集 / 评级指标）。
	/// 这几类互相嵌套（112 完成后 123 才开、123 完成后 1001 才算、1001 完成 113 才够 1 个指标），
	/// 单趟跑完会漏掉后一层，所以最多迭代 3 轮，直到没有变化为止。
	/// </summary>
	public static int SyncDerived(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int total = 0;
		for (int pass = 0; pass < 3; pass++)
		{
			int changed = AdvanceByOperateBuilding(player, VirCaptureBuildState.GetTotalOperate(player), sync);
			changed += AdvanceByBuildLevel(player, sync);
			changed += AdvanceComposite(player, sync);
			changed += AdvanceByIndicator(player, sync);
			// 第 512 节：岛屿等级达标 → 区域解锁条件任务（5000/5001/5002）置已领。
			changed += SyncMapLevelTasks(player, sync);
			total += changed;
			if (changed == 0)
			{
				break;
			}
		}
		return total;
	}

	/// <summary>
	/// 第 510 节：Type 18 = 「到某点位」任务，点位完成状态记在 128 组（SetPointState 写 2）。
	/// 剧情触发点（region_info 的 RegionType 24 / PalCartoonTrigger）播完动画只发一条
	/// VirCaptureLevel_SaveFightData，客户端从不补 VirCaptureTask_DoSomething；
	/// 而点位一旦是完成态，重进图也不会再触发，任务就永久停在 0/1（主线 119 → 200 整段断掉）。
	/// 这里在登录/进岛时按点位状态补一遍，幂等。
	/// </summary>
	public static int SyncRegionTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 18u || row.Param.Count < 2 || row.Param[1] == 0u)
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			if (!PreClaimed(player, row))
			{
				continue;
			}
			// 501~2500 的点位在 128 里只存 1 个 bit（SetPointState 写 2 也只是置位），
			// GetPointState 读回来是 0/1，所以这里判「非 0 就算到过」。
			if (VirCaptureStateHelper.GetPointState(player, row.Param[0], row.Param[1]) == 0u)
			{
				continue;
			}
			if (!WriteProgress(player, row.Id, row.CheckValue, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} (point {row.Param[1]} done) -> {row.CheckValue}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 512 节：Type 5 = 「岛屿等级门槛」任务（本表只有 5000/5001/5002 三条）。
	/// Param[0] = 地图 ID（326 = 蓝图岛），CheckValue = 需要的岛屿等级；
	/// 它们同时是 level_region_control.json 里区域解锁的条件：
	///   区域 2 → Condition [5000]（岛屿升到 2 级）
	///   区域 3 → Condition [5001]（3 级）
	///   区域 4 → Condition [5002]（4 级）
	///
	/// 客户端 PalClient.CheckRegionControlIsUnlock 要求「条件任务已领取」
	/// （VirCaptureTask.IsReceive → 163 的 bit0），而服务端从来没人写过这三条，
	/// 于是岛屿已经 2 级、区域 2 仍显示「暂时无法解锁 / 岛屿升级至2级」。
	///
	/// 这三条没有 Name、没有奖励，只在区域面板里充当解锁条件，所以等级达标后
	/// 直接写「完成 + 已领」（值 = CheckValue&lt;&lt;1 | 1），幂等。
	/// </summary>
	public static int SyncMapLevelTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (row.Type != 5u || row.Param.Count < 1 || row.Param[0] == 0u)
			{
				continue;
			}
			if (IsClaimed(player, row.Id))
			{
				continue;
			}
			uint level = VirCaptureLevelState.GetMapLevel(player, row.Param[0]);
			if (level < row.CheckValue)
			{
				continue;
			}
			PlayerAttr attr = player.Attributes.GetOrCreate(Gid, row.Id);
			uint want = (row.CheckValue << 1) | 1u;
			if (attr.Val == want)
			{
				continue;
			}
			attr.Val = want;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
			Log.Info($"[VirCapture] task {row.Id} (map {row.Param[0]} lv {level} >= {row.CheckValue}) auto claimed uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>
	/// 第 516 节：岛屿升级条件里的两条计数任务（主线 313/314，BigType 5）。
	/// 客户端映射（vircapture_task_type.json）：Type 6 → nType 3、Type 26 → nType 14，
	/// 都 NoCheck=1 —— 客户端纯读 163 的进度，服务端不写就永远 0：
	///   Type 6（313）：「累计聘用 N 位员工」，进度 = 已拥有员工数（本服不删员工，等价于累计聘用）。
	///   Type 26（314，Param=[326, 3, 4]）：「海之滨（区域 3）累计 N 幢达到 4 级的建筑」，
	///     进度 = 该区域建筑等级 ≥ Param[2] 的幢数。
	/// 幂等，登录/进岛/升级建筑后都会跑一遍。
	/// </summary>
	public static int SyncStaffBuildTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		uint ownedStaff = (uint)VirCaptureStaffState.OwnedCount(player);
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			uint progress;
			if (row.Type == 6u)
			{
				progress = ownedStaff;
			}
			else if (row.Type == 26u && row.Param.Count >= 3)
			{
				uint count = 0;
				foreach (VirCaptureBuildingRow cfg in VirCaptureBuildTable.All.Values)
				{
					if (cfg.Area != row.Param[1] || (row.Param[0] != 0u && cfg.MapId != row.Param[0]))
					{
						continue;
					}
					if (VirCaptureBuildState.GetLevel(player, cfg.Id) >= row.Param[2])
					{
						count++;
					}
				}
				progress = count;
			}
			else
			{
				continue;
			}
			if (IsClaimed(player, row.Id) || GetProgress(player, row.Id) >= row.CheckValue)
			{
				continue;
			}
			uint next = System.Math.Min(row.CheckValue, progress);
			if (next == 0u || !WriteProgress(player, row.Id, next, sync))
			{
				continue;
			}
			Log.Info($"[VirCapture] task {row.Id} (type {row.Type}) -> {next}/{row.CheckValue} uid={player.Uid}");
			changed++;
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>按 CheckValue 截断并推进一条任务的进度，返回推进后的值。</summary>
	public static uint Advance(PlayerInstance player, VirCaptureBuildTaskRow row, uint step, NtfSyncPlayer? sync)
	{
		uint progress = GetProgress(player, row.Id);
		if (progress >= row.CheckValue)
		{
			return progress;
		}
		uint next = progress + step;
		if (next > row.CheckValue)
		{
			next = row.CheckValue;
		}
		WriteProgress(player, row.Id, next, sync);
		return next;
	}

	/// <summary>登录/进岛时把「已满足条件但客户端不会自己上报」的任务补一下（占位 + 建筑 + 经营 + 评级条件 + 合集）。</summary>
	public static int SyncAll(PlayerInstance player, NtfSyncPlayer? sync)
	{
		int changed = EnsurePlaceholderTasks(player, sync);
		changed += SyncBuildTasks(player, sync);
		changed += SyncOperateTasks(player, sync);
		changed += AdvanceByBuildLevel(player, sync);
		changed += SyncSpendTasks(player, sync);
		changed += AdvanceComposite(player, sync);
		changed += SyncRegionTasks(player, sync);
		// 第 511 节：119/227/325 这类剧情任务领掉后，对应的「送达型」隐藏任务（Type 7）置完成。
		// 奖励由 VirCapture_CheckOpenAct / VirCaptureTask_GetReward 代领（见 ClaimTriggerTasksAsync）。
		changed += AdvanceTriggerTasks(player, 0u, sync);
		// 第 516 节：岛屿升级条件的「累计聘用员工 / 区域建筑等级」计数任务（Type 6 / Type 26）。
		changed += SyncStaffBuildTasks(player, sync);
		changed += SyncDerived(player, sync);
		return changed;
	}

	public static bool AnyClaimable(PlayerInstance player)
	{
		foreach (VirCaptureBuildTaskRow row in VirCaptureBuildTaskTable.All.Values)
		{
			if (!IsClaimed(player, row.Id) && GetProgress(player, row.Id) >= row.CheckValue)
			{
				return true;
			}
		}
		return false;
	}
}
