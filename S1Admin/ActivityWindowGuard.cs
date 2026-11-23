using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class ActivityWindowGuard
{
	private const uint GroupId = 50u;

	private const uint IdPosStep = 10u;

	private const long NoExpiryThreshold = 3786825600L;

	private const long LongSpanSeconds = 94608000L;

	private static readonly object LogGate = new object();

	private static readonly int[] LoginDelays = new int[3] { 9000, 22000, 40000 };

	public static void OnLogin(Connection connection)
	{
		int[] loginDelays = LoginDelays;
		foreach (int num in loginDelays)
		{
			int delay = num;
			Task.Run(async () =>
			{
				try
				{
					await Task.Delay(delay);
					await Push(connection, $"login@{delay}");
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("login push failed: " + ex2.Message);
				}
			});
		}
	}

	// free_items（特别邀请）/ signin_monthly（花朝月夜等签到板）类活动：
	// 登录自动弹窗且客户端缺奖励配置（「活动奖励配置错误」），2026-11-10 起
	// 整体压制（不再打开 / 主动关闭），参照第七十节的处理口径。
	//
	// 但 free_items 里混着两类完全不同的东西（第 364 节）：
	//   · 特别邀请（template_13~20，ModeId 1028）：97/124/174/232/304/356/557，
	//     就是上面说的会弹窗缺奖励的那批，继续压制；
	//   · 妮塔之邀（activity 283，ModeId 1035，常驻 05/01 起）：正经能玩的棋盘活动，
	//     它的 Class 也是 free_items，被按 Class 一刀切压掉后卡片显示「未开启」。
	// 因此 free_items 改为**按 ID 白名单**压制，只压真正的特别邀请。
	private static readonly HashSet<uint> SuppressedFreeItemIds = new HashSet<uint> { 97u, 124u, 174u, 232u, 304u, 356u, 557u };

	private static readonly string[] SuppressedClasses = new string[] { "signin_monthly" };

	// 第 423 节：拼图（jigsaw_puzzle，启忆录）类活动不受「期数窗口」约束。
	//
	// 病根：本期 DLC 窗口的时间是被改写成「现在 ~ 现在+30 天」的（DlcPin 钉期），
	// 而拼图活动自己的 Excel 窗口还写着历史日期（390 = 2026-04-02 ~ 2026-05-14）。
	// 于是登录体检 ActivityWindowGuard.Push/Apply 把它的开放时间「还原」成历史值，
	// 客户端 DlcFuncBase.CheckIsUnlock 判成 Lock，DLC 窗口里那张「适格启忆录」卡片
	// 一直是灰的、点进去提示「暂未开放」。
	//
	// 拼图活动只可能从对应期的 DLC 窗口里点开（卡片本身由 DLC 期数配置决定），
	// 所以这里放开期数校验不会让它在别的期冒出来。
	private static readonly string[] AlwaysOpenClasses = new string[] { "jigsaw_puzzle" };

	// 第 428 / 429 / 430 节：这几张活动只该在指定期数里出现，别期一律关掉。
	//   · 33 / 34（组 33，藏锋 / 芙提雅-缄默）        → 第 2 期；
	//   · 100 / 101 / 102（组 100，琴诺-悖谬 / 羽蜕 / 苔丝）→ 第 7 期；
	//   · 390 / 602（组 390，适格启忆录两条）          → 第 33 / 36 期；
	//   · 283（组 283，妮塔之邀）                      → 第 27 期；
	//   · 69（组 69，斑斓漫想 / 七日签到皮肤）         → 空列表 = 任何期数都不开（第 431 节）。
	//   · 71（组 71，感知巡游 energy_exchange）        → 第 27 期（第 432 节）。
	//   · 80（组 80，荣光盛宴 / 老签到板，2024 年那期） → 空列表 = 全期退役（第 436 节）。
	//   · 88 + 88 组其余 8 条（逆焰勇进等 2024 年签到板） → 空列表 = 全期退役（第 437 节）。
	//   · 38（组 38，旧墟新痕 / 2023-09 那期 7 日签到板） → 空列表 = 全期退役（第 443 节）；
	//     组 38 内只有 38 这一条（42 属同组但另立一行「逐日沐潮」，不在此列）。
	// 第 430 节更正：第 429 节按「组号=角色」猜，把 33/34 与 100 的期数写反了。
	// 服务端不做「一刀切退役」，而是按期放行：命中本期的照常开窗（390/602 走
	// IsAlwaysOpenClass 那条 jigsaw 特例），不在本期的强制回配置时间并拒绝刷新请求。
	// 客户端侧由第 57 条补丁 GachaTryOldHide 用同一张表拦截。
	private static readonly Dictionary<uint, uint[]> EraGatedActivities = new Dictionary<uint, uint[]>
	{
		{ 33u, new uint[] { 2u } },
		{ 34u, new uint[] { 2u } },
		{ 100u, new uint[] { 7u } },
		{ 101u, new uint[] { 7u } },
		{ 102u, new uint[] { 7u } },
		{ 390u, new uint[] { 33u, 36u } },
		{ 602u, new uint[] { 33u, 36u } },
		{ 283u, new uint[] { 27u } },
		{ 69u, new uint[0] },
		{ 71u, new uint[] { 27u } },
		{ 80u, new uint[0] },
		{ 88u, new uint[0] },
		{ 108u, new uint[0] },
		{ 112u, new uint[0] },
		{ 120u, new uint[0] },
		{ 148u, new uint[0] },
		{ 150u, new uint[0] },
		{ 164u, new uint[0] },
		{ 171u, new uint[0] },
		{ 172u, new uint[0] },
		{ 38u, new uint[0] }
	};

	/// <summary>该活动是否属于「按期数门控」的那几张（第 429 节）。</summary>
	public static bool IsEraGated(uint activityId)
	{
		return EraGatedActivities.ContainsKey(activityId);
	}

	/// <summary>该活动在当前期数是否应被关掉（默认模式 era=0 视为关）。</summary>
	public static bool IsRetired(uint activityId, uint eraId)
	{
		if (!EraGatedActivities.TryGetValue(activityId, out uint[] eras))
		{
			return false;
		}
		if (eraId == 0)
		{
			return true;
		}
		return Array.IndexOf(eras, eraId) < 0;
	}

	public static bool IsAlwaysOpenClass(ActivityExcel conf)
	{
		return conf?.Class != null && Array.IndexOf(AlwaysOpenClasses, conf.Class) >= 0;
	}

	public static bool IsSuppressedClass(ActivityExcel conf)
	{
		if (conf == null)
		{
			return false;
		}
		if (Array.IndexOf(SuppressedClasses, conf.Class) >= 0)
		{
			return true;
		}
		return conf.Class == "free_items" && SuppressedFreeItemIds.Contains(conf.Id);
	}

	public static bool IsEraActivity(long start, long end, long floor, long ceil)
	{
		if (start <= 0)
		{
			return true;
		}
		if (end >= 3786825600u)
		{
			return start >= floor && (ceil <= 0 || start < ceil);
		}
		if (end > 0 && end - start >= 94608000)
		{
			return true;
		}
		return start >= floor && (ceil <= 0 || start < ceil);
	}

	public static int Apply(PlayerInstance player)
	{
		long num = DlcClientPatches.ActivationFloor(DlcPin.Override());
		if (num <= 0)
		{
			return 0;
		}
		long num2 = DlcClientPatches.ActivationCeil(DlcPin.Override());
		// 本期「队员试用」的活动由 Push 里的 ForceOpen 负责开，
		// 登录体检时不能再按「非本期」把它们还原成过期（第 173 节）。
		HashSet<uint> trialIds = EraTrialIds(DlcPin.Override());
		int num3 = 0;
		List<string> list = new List<string>();
		foreach (ActivityExcel value2 in GameData.ActivityData.Values)
		{
			// 期数门控优先于 dlc_activities.TryIds：第 2 期的 TryIds 恰好是 33/34，
			// 但这两个被门控到第 7 期，登录体检不能再按「本期试用」放过（第 429 节）。
			if (trialIds.Contains(value2.Id) && !IsRetired(value2.Id, DlcPin.Override()))
			{
				continue;
			}
			// 退役活动：开放时间一律回到配置值（已过期），覆盖历史上被开过的值（第 428 节）。
			if (IsRetired(value2.Id, DlcPin.Override()))
			{
				long retiredStart = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value2.StartTime));
				if (retiredStart > 0 && retiredStart <= uint.MaxValue)
				{
					uint rsid = value2.Id * 10;
					uint rcur = player.Attributes.GetValue(50u, rsid);
					if (rcur != (uint)retiredStart)
					{
						player.Attributes.GetOrCreate(50u, rsid).Val = (uint)retiredStart;
						num3++;
						if (list.Count < 40)
						{
							list.Add($"{value2.Id}:{rcur}->{retiredStart}(retired)");
						}
					}
				}
				continue;
			}
			// 期数门控命中本期：开放时间推到现在，别期（IsRetired 为真）上面已经回配置值了（第 429 节）。
			if (IsEraGated(value2.Id))
			{
				uint gsid = value2.Id * 10;
				uint gwant = (uint)(DateTimeOffset.Now.ToUnixTimeSeconds() - 86400);
				uint gcur = player.Attributes.GetValue(50u, gsid);
				if (gcur != gwant)
				{
					player.Attributes.GetOrCreate(50u, gsid).Val = gwant;
					num3++;
					if (list.Count < 40)
					{
						list.Add($"{value2.Id}:{gcur}->{gwant}(gate-open)");
					}
				}
				continue;
			}
			long end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value2.EndTime));
			long num4 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value2.StartTime));
			if (IsAlwaysOpenClass(value2))
			{
				// 拼图类：登录数据下发之前就把开放时间写成本刻，
				// 客户端登录时算出的「已开放列表」才会包含它（卡片一次到位）。
				uint openSid = value2.Id * 10;
				uint openWant = (uint)(DateTimeOffset.Now.ToUnixTimeSeconds() - 86400);
				uint openCur = player.Attributes.GetValue(50u, openSid);
				if (openCur == 0 || openCur + 604800 < openWant)
				{
					player.Attributes.GetOrCreate(50u, openSid).Val = openWant;
					num3++;
				}
				continue;
			}
			if (num4 <= 0 || num4 > uint.MaxValue || (!IsSuppressedClass(value2) && IsEraActivity(num4, end, num, num2)))
			{
				continue;
			}
			uint sid = value2.Id * 10;
			uint value = player.Attributes.GetValue(50u, sid);
			if (value != 0 && value != (uint)num4)
			{
				player.Attributes.GetOrCreate(50u, sid).Val = (uint)num4;
				num3++;
				if (list.Count < 40)
				{
					list.Add($"{value2.Id}:{value}->{num4}");
				}
			}
		}
		if (num3 > 0)
		{
			Log($"第 {DlcPin.Override()} 期 floor={num} ceil={num2} 还原 {num3} 个非本期活动的开放时间 {string.Join(",", list)}");
		}
		return num3;
	}

	public static void PushAllOnline(string reason)
	{
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			SocketConnection value = item.Value;
			Connection conn = value as Connection;
			if (conn == null || conn.Player == null)
			{
				continue;
			}
			Task.Run(async () =>
			{
				try
				{
					await Push(conn, reason);
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log(reason + ": 同步失败 " + ex2.Message);
				}
			});
		}
	}

	public static async Task Push(Connection connection, string reason)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint eraId = DlcPin.Override();
		long floor = DlcClientPatches.ActivationFloor(eraId);
		if (floor <= 0)
		{
			return;
		}
		long ceil = DlcClientPatches.ActivationCeil(eraId);
		HashSet<uint> trialIds = EraTrialIds(eraId);
		long now = DateTimeOffset.Now.ToUnixTimeSeconds();
		long openStart = now - 86400;
		long openEnd = now + 5184000;
		int opened = 0;
		int closed = 0;
		foreach (ActivityExcel conf in GameData.ActivityData.Values)
		{
			long start = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.StartTime));
			long end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.EndTime));
			if (start > 0 && start <= uint.MaxValue)
			{
				uint sid = conf.Id * 10;
				PlayerAttr attr = player.Attributes.GetOrCreate(50u, sid);
				uint current = attr.Val;
				// 退役活动：永不 open，属性回配置值并明确回一次「按配置「结束」的窗口（第 428 节）。
				if (IsRetired(conf.Id, eraId))
				{
					if (current != (uint)start)
					{
						attr.Val = (uint)start;
						await SendOpen(connection, conf.Id, start, end);
						closed++;
					}
					continue;
				}
				// 期数门控命中本期：Excel 窗口是历史值，不能按 start >= floor 判，直接开（第 429 节）。
				if (IsEraGated(conf.Id))
				{
					if (current != (uint)openStart)
					{
						attr.Val = (uint)openStart;
						await SendOpen(connection, conf.Id, openStart, openEnd);
						opened++;
					}
					continue;
				}
				bool suppress = IsSuppressedClass(conf);
				bool alwaysOpen = IsAlwaysOpenClass(conf);
				if (!suppress && (alwaysOpen || (start >= floor && (ceil <= 0 || start < ceil))))
				{
					attr.Val = (uint)openStart;
					await SendOpen(connection, conf.Id, openStart, openEnd);
					opened++;
				}
				else if (!alwaysOpen && (suppress || !IsEraActivity(start, end, floor, ceil)) && current != 0 && current != (uint)start)
				{
					attr.Val = (uint)start;
					await SendOpen(connection, conf.Id, start, 0L);
					closed++;
				}
			}
		}
		// 本期「队员试用」：DLC 配置里的 tbTryID 指向的试用活动（第 1 期是 18 / 25）
		// 常常比版本开服时刻早开几个小时（18 是 2023-08-07 04:00，floor 是同日 10:00），
		// 被 start >= floor 挡掉后活动列表里就没有这一项。这里按 TryID 强制开
		//（只开试用活动本身，组根 12 不开，否则会多出一张空白的「队员试用」卡）。（第 173 节）
		foreach (uint tid in trialIds)
		{
			// 门控到别的期数的试用不在本期强开（第 429 节）。
			if (IsRetired(tid, eraId))
			{
				continue;
			}
			if (!GameData.ActivityData.TryGetValue(tid, out ActivityExcel tconf))
			{
				Log($"{reason}: 第 {eraId} 期 试用 {tid} 没有活动配置");
				continue;
			}
			int n1 = await ForceOpen(player, connection, tconf, openStart, openEnd);
			Log($"{reason}: 第 {eraId} 期 试用 {tid} group={GroupIdOf(tconf)} 开={n1}");
			opened += n1;
		}
		if (opened == 0 && closed == 0)
		{
			Log($"{reason}: 第 {eraId} 期无需调整");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (PlayerAttr attr2 in player.Data.Attrs.Where((PlayerAttr x) => x.Gid == 50))
		{
			sync.Custom[player.ToPackedAttrKey(50u, attr2.Sid)] = attr2.Val;
			sync.Custom[player.ToShiftedAttrKey(50u, attr2.Sid)] = attr2.Val;
		}
		await connection.SendPacket(1004, sync, 0);
		Log($"{reason}: 第 {eraId} 期 floor={floor} ceil={ceil} 开 {opened} 关 {closed}");
		// 第 423 节：DLC 窗口在登录 1.5 秒就弹出来了，比这里改属性早得多，
		// 于是「适格启忆录」这类靠属性判定的卡片会一直停在锁定样式。
		// 属性改完之后补一次重绘，让卡片当场翻成已开启。
		if (opened > 0 || closed > 0)
		{
			await RedrawDlcCards(connection);
		}
	}

	/// <summary>让客户端把已打开的 DLC 主窗口的功能卡片重跑一遍动态信息。</summary>
	private static Task RedrawDlcCards(Connection connection)
	{
		const string code = "local function mikuDlcRedraw()\n"
			+ "if type(UI) ~= [[table]] or type(UI.tbWidget) ~= [[table]] then return end\n"
			+ "local n = 0\n"
			+ "for _, w in pairs(UI.tbWidget) do\n"
			+ "if type(w) == [[table]] and type(w.sName) == [[string]] then\n"
			+ "local s = string.lower(w.sName)\n"
			+ "if string.sub(s, 1, 3) == [[dlc]] and string.find(s, [[main]], 1, true) and type(w.UpdateBtnDynamicInfo) == [[function]] then\n"
			+ "pcall(function() w:UpdateBtnDynamicInfo() end)\n"
			+ "n = n - (-1)\n"
			+ "end\n"
			+ "end\n"
			+ "end\n"
			+ "if n == 0 then return end\n"
			+ "end\n"
			+ "pcall(mikuDlcRedraw)\n";
		string arg = new JsonObject { ["code"] = code }.ToJsonString();
		return CallGSRouter.SendScript(connection, "GM", arg);
	}


	/// <summary>管理接口用：把每期的 TryID 与配置缺失情况打成 JSON。</summary>
	public static string DescribeTrialIds()
	{
		JsonArray eras = new JsonArray();
		foreach (KeyValuePair<uint, DlcActivityExcel> kv in GameData.DlcActivityData.OrderBy((KeyValuePair<uint, DlcActivityExcel> x) => x.Key))
		{
			JsonObject item = new JsonObject();
			item["era"] = (long)kv.Key;
			JsonArray ids = new JsonArray();
			JsonArray missing = new JsonArray();
			if (kv.Value.TryIds != null)
			{
				foreach (uint tid in kv.Value.TryIds)
				{
					ids.Add((long)tid);
					if (!GameData.ActivityData.ContainsKey(tid))
					{
						missing.Add((long)tid);
					}
				}
			}
			item["tryIds"] = ids;
			item["missingActivity"] = missing;
			eras.Add(item);
		}
		JsonObject root = new JsonObject();
		root["eras"] = eras;
		return root.ToJsonString();
	}

	private static HashSet<uint> EraTrialIds(uint eraId)
	{
		HashSet<uint> set = new HashSet<uint>();
		try
		{
			if (DlcPin.IsDefault(eraId) || !GameData.DlcActivityData.TryGetValue(eraId, out DlcActivityExcel era) || era.TryIds == null)
			{
				return set;
			}
			foreach (uint tid in era.TryIds)
			{
				set.Add(tid);
			}
		}
		catch
		{
		}
		return set;
	}

	private static async Task<int> ForceOpen(PlayerInstance player, Connection connection, ActivityExcel conf, long start, long end)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(50u, conf.Id * 10u);
		if (attr.Val == (uint)start)
		{
			return 0;
		}
		attr.Val = (uint)start;
		await SendOpen(connection, conf.Id, start, end);
		return 1;
	}

	private static uint GroupIdOf(ActivityExcel conf)
	{
		try
		{
			object raw = conf.GroupId;
			if (raw == null)
			{
				return 0u;
			}
			string text = raw.ToString();
			if (string.IsNullOrEmpty(text))
			{
				return 0u;
			}
			return uint.TryParse(text.Trim(), out uint gid) ? gid : 0u;
		}
		catch
		{
			return 0u;
		}
	}

	private static Task SendOpen(Connection connection, uint id, long startTime, long endTime)
	{
		string arg = new JsonObject
		{
			["nId"] = id,
			["nStartTime"] = startTime,
			["nEndTime"] = endTime,
			["nCloseFlag"] = 0
		}.ToJsonString();
		return CallGSRouter.SendScript(connection, "ActivityQuest_Open", arg);
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "activitywindow.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
