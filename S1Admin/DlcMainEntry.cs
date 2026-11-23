using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class DlcMainEntry
{
	private const long OpenBeforeSeconds = 3600L;

	private const long OpenAfterSeconds = 2592000L;

	private static readonly object LogGate = new object();

	private static readonly int[] Delays = new int[3] { 800, 6000, 15000 };

	/// <summary>
	/// 这几期是老版本复刻：主界面「战斗」左边的版本活动入口一律整块取消。
	/// 注意 BlankDLC 是那张大图（Visibility=3 SelfHitTestInvisible，仍会绘制），
	/// 老期海报贴图在 3.2.0 客户端已缺失 → 只收 BtnDLC 会剩下一个白块。
	/// </summary>
	private static readonly HashSet<uint> HideEntryEras = new HashSet<uint> { 1u, 2u, 3u, 4u };

	public static void OnLogin(Connection connection)
	{
		int[] delays = Delays;
		foreach (int num in delays)
		{
			int ms = num;
			Task.Run(async () =>
			{
				try
				{
					await Task.Delay(ms);
					await Push(connection, "login");
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("push failed: " + ex2.Message);
				}
			});
		}
	}

	public static void ResendAll(string reason)
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
					Log("resend failed: " + ex2.Message);
				}
			});
		}
	}

	/// <summary>
	/// 该期要不要把主界面「战斗」左侧的版本活动入口整块收起：
	///   · 游戏默认（pin=99）/ 存档里没有这一期 → 收起；
	///   · 1~4 期（老版本复刻，海报贴图缺失）→ 收起；
	///   · EraLite 名单里的期 → 收起；
	///   · 其余（5 期及以后）→ 下发显示。
	/// 客户端补丁 DefaultModeWindows 里的 dmHideEntry 走的是同一个判定（通过
	/// MIKU_HIDE_ENTRY 占位符下发），两边必须一致，否则会出现「服务端刚下发显示、
	/// 补丁下一次 UI.Open 又收起来」的拉锯（第 231 节）。
	/// </summary>
	public static bool EntryHiddenFor(uint eraId)
	{
		if (DlcPin.IsDefault(eraId))
		{
			return true;
		}
		if (!GameData.DlcActivityData.ContainsKey(eraId))
		{
			return true;
		}
		return HideEntryEras.Contains(eraId) || EraLite.Is(eraId);
	}

	public static async Task Push(Connection connection, string reason)
	{
		if (connection.Player != null)
		{
			uint eraId = DlcPin.Override();
			if (EntryHiddenFor(eraId))
			{
				bool flag = DlcPin.IsDefault(eraId) || !GameData.DlcActivityData.ContainsKey(eraId);
				// 游戏默认 / 无效期数：不固定任何一期时，主界面左侧的版本活动入口必须收起，
				// 否则客户端会按本地时间自己猜一期（贴图还可能是缺的），
				// 表现出来就是战斗界面左边一块白框。（第 171 节）
				Send(connection, DumpAndCall("dlc_hide", BuildHide(eraId)));
				Log(flag ? (reason + ": 未 pin 版本（游戏默认），版本活动入口已隐藏") : $"{reason}: 第 {eraId} 期版本活动入口已隐藏（BlankDLC 大图一并收起）");
				return;
			}
			long now = DateTimeOffset.Now.ToUnixTimeSeconds();
			long start = now - 3600;
			long end = now + 2592000;
			string sig = $"dm{eraId}-{now / 86400}";
			Send(connection, DumpAndCall("dlc_fix", BuildFix(start, end, sig)));
			Log($"{reason}: 第 {eraId} 期版本活动入口已下发（窗口 {start} ~ {end}）");
		}
	}

	/// <summary>
	/// 第 355 节（2026-11-16 15:10）：切到 5 期后「入口出现 → 点进去 → 退出就消失」。
	///
	/// 原因不是窗口算错（GetCurConf 返回的确实是 now-1h ~ now+30d），而是历史遗留：
	/// 切过「游戏默认」/ 1~4 期时，BuildHide 除了收起按钮，还给 UI.Open 永久包了一层
	/// hideEntry —— 这个闭包没有任何开关，之后每次打开任意界面都会再把 BtnDLC 收起。
	/// 切到 5 期时 BuildFix 只拆了 ShowBtnDlc 的包装并显式显示一次，所以入口先出现；
	/// 用户一点进去就触发 UI.Open，旧钩子立刻又收起来，退出后自然就没了。
	///
	/// 现在做两件事：
	///   1. bMikuDlcWantShow 作为「该不该显示」的总开关（BuildHide 里置 false）；
	///   2. 新装一层 UI.Open / UI.Close 钩子，在每个界面切换之后把入口自愈回来。
	/// 钩子是后装的，包在旧 hideEntry 之外，所以同一轮里「先收后显」，最终状态是显示。
	/// 另外 dmConfFix 会自检 GetCurConf 是否还带着正确窗口 —— 万一客户端重载配置把
	/// 包装顶掉，这里会重新包一层，不再依赖 __DMV 的「一天只包一次」。
	/// </summary>
	private static string BuildFix(long start, long end, string sig)
	{
		List<string> list = new List<string>();
		list.Add($"local S, E = {start}, {end}");
		list.Add("_G.bMikuDlcWantShow = true");
		list.Add("_G.bMikuHideEntry = false");
		// 第 355 节二改：把历史遗留的钩子整层剥掉。
		// 以前 BuildHide 给 UI.Open 包过 hideEntry（无开关），客户端不重启就一直生效；
		// 再下发新脚本只会「叠加」而不会「替换」，所以这里必须先把链路上的包装剥干净再装。
		// 判据：服务端下发的脚本都是 loadstring 来的，short_src 形如 [string "local S, E = ..."]。
		// 注意客户端自己的模块同样是 loadstring（[string "D:/GAME/.../umg_main.lua"]），
		// 所以必须排除带 Game/Content/Script 的路径，否则会误剥客户端原生函数。
		// [C] 函数虽也以 "[" 开头，但没有函数型 upvalue，循环会自然退出。
		list.Add("local function dmBad(src) return src:byte(1) == 91 and not string.find(src, [[Game/Content/Script]], 1, true) end");
		// 剥层按 upvalue 名字精确匹配（oO/oC/oR/pR/oldOpen/old），pR 是 BuildHide 包
		// RefreshBtnState 用的名字，两边共用一张名单，否则互切期数会漏剥、越叠越多。
		list.Add("local function dmPeel(f) local d = 0 while type(f) == [[function]] and d ~= 8 do local inf = debug.getinfo(f, [[S]]) local src = tostring(inf and inf.short_src or [[]]) if not dmBad(src) then break end local nxt for i = 1, 12 do local nm, v = debug.getupvalue(f, i) if nm == nil then break end if type(v) == [[function]] and (nm == [[oO]] or nm == [[oC]] or nm == [[oR]] or nm == [[pR]] or nm == [[oldOpen]] or nm == [[old]]) then nxt = v break end end if type(nxt) ~= [[function]] then break end f = nxt d = d + 1 end return f, d end");
		list.Add("local function dmFindMain() local m for _,w in pairs(UI.tbWidget or {}) do if type(w) == [[table]] and tostring(w.sName) == [[main]] then m = w end end return m end");
		list.Add("local DM_BTNS = { [[BtnDLC]], [[NewDLC]], [[BlankDLC]], [[DLCLock]] }");
		list.Add("local function dmGetVis(w) return w.Visibility end");
		list.Add("local function dmConfFix()");
		list.Add("if not (DLC_Logic and type(DLC_Logic.GetCurConf) == [[function]]) then return end");
		list.Add("local ok, c = pcall(DLC_Logic.GetCurConf)");
		list.Add("if ok and type(c) == [[table]] and (tonumber(c.nEnterStartTime) or 0) >= S - 86400 then return end");
		list.Add("local D = DLC_Logic");
		list.Add("local old = D.GetCurConf");
		list.Add("D.GetCurConf = function(...) local cc = old(...) if type(cc) == [[table]] then cc.nEnterStartTime = S cc.nCloseEndTime = E end return cc end");
		list.Add("end");
		// 第 355 节三改（卡顿）：这个函数会被 RefreshBtnState / UI.Open 高频调用，
		// 必须走「极快路径」——先看缓存的主界面、再读 4 个按钮的 Visibility，
		// 只要有一个已经可见就立刻返回。遍历 tbWidget、调 GetCurConf、调 ShowBtnDlc
		// 都只在「真的被收起了」时才发生，并且整体做 0.1 秒节流（最多 10 次/秒）。
		// 第 356 节二改（2026-11-16 16:10）：顺序调整。
		// 实测 20 期扫描时发现每期都是 BtnDLC=Visible 但 m.bMikuDlcHidden 仍为 true ——
		// 因为「任一控件已可见就 return」这条极快路径排在清标记之前，标记永远留着。
		// hidden 是历史 hideEntry 包装的开关，留着就随时可能被客户端重刷时再收一次，
		// 属于「现在看着好、下次进活动又没了」的定时炸弹。
		// 现在把「清标记 + 还原 ShowBtnDlc」提到最前面（只读一个字段，几乎零成本），
		// 节流只包住真正昂贵的 ShowBtnDlc / dmConfFix。
		list.Add("local function dmShow()");
		list.Add("if _G.bMikuDlcWantShow ~= true then return end");
		list.Add("local m = _G.__dmMain");
		list.Add("if type(m) ~= [[table]] or m.BtnDLC == nil then m = dmFindMain() _G.__dmMain = m end");
		list.Add("if type(m) ~= [[table]] then return end");
		list.Add("if m.bMikuDlcHidden then m.bMikuDlcHidden = nil if type(m.bMikuDlcShow) == [[function]] then m.ShowBtnDlc = m.bMikuDlcShow end m.bMikuDlcShow = nil end");
		list.Add("for i = 1, 4 do local w = m[DM_BTNS[i]] if w ~= nil then local ok, v = pcall(dmGetVis, w) if ok and v == 0 then return end end end");
		list.Add("local t = os.clock()");
		list.Add("if t - (_G.__dmLastShow or 0) < 0.1 then return end");
		list.Add("_G.__dmLastShow = t");
		list.Add("if type(m.ShowBtnDlc) == [[function]] then pcall(m.ShowBtnDlc, m) end");
		list.Add("if t - (_G.__dmLastConf or 0) > 30 then _G.__dmLastConf = t pcall(dmConfFix) end");
		list.Add("end");
		list.Add("_G.MikuDmEnsureShow = dmShow");
		// 先剥掉历史 hideEntry（以及上一版我们自己的包装），保证链路上只有一层。
		list.Add("local dO = dmPeel(UI.Open) local dC = dmPeel(UI.Close)");
		list.Add("UI.Open = dO");
		list.Add("if type(dC) == [[function]] then UI.Close = dC end");
		list.Add("local oO = UI.Open");
		list.Add("UI.Open = function(...) local r1, r2, r3, r4 = oO(...) pcall(dmShow) return r1, r2, r3, r4 end");
		list.Add("local oC = UI.Close");
		list.Add("if type(oC) == [[function]] then UI.Close = function(...) local r1, r2, r3, r4 = oC(...) pcall(dmShow) return r1, r2, r3, r4 end end");
		// 关键：客户端自己会在 RefreshBtnState / __SVO 里按它自己的判断把入口收起，
		// 这一步往往发生在 UI.Open/Close 之后（本次实测就在退出后 1~2 秒），
		// 所以必须挂在 RefreshBtnState 的返回点再补一次显示，而不是只靠界面切换钩子。
		// 这里不做 table.pack（每次调用都建表，RefreshBtnState 调用很频繁）。
		// 注意：Lua 里 f[[str]] 是「以长字符串为唯一参数的函数调用」，不是下标！
		// 上一版写成 package.loaded[[UMG.Main.umg_main]] 会直接运行时报
		// "attempt to call a table value (field 'loaded')"，后面的钩子全部装不上。
		list.Add("local M = package.loaded[\"UMG.Main.umg_main\"]");
		list.Add("if type(M) == [[table]] and type(M.RefreshBtnState) == [[function]] then local pR = dmPeel(M.RefreshBtnState) M.RefreshBtnState = pR local oR = M.RefreshBtnState M.RefreshBtnState = function(self, ...) local r1, r2, r3 = oR(self, ...) pcall(dmShow) return r1, r2, r3 end end");
		list.Add("pcall(dmConfFix)");
		list.Add("pcall(dmShow)");
		list.Add("_G.__DMV = [[" + sig + "]]");
		list.Add("_G.bMikuDmShowHook = true");
		return string.Join(" ", list);
	}

	/// <summary>
	/// 轻量复刻期（1~4 期）：本客户端里这些期的版本活动主界面跑不通，所以入口
	/// 直接按「游戏默认」处理——把主界面的版本活动按钮收起来，并包住
	/// ShowBtnDlc，避免游戏自己重刷时又冒出来。
	/// </summary>
	private static string BuildHide(uint eraId)
	{
		// 第 355 节四改（2026-11-16 15:35）：切回第 4 期时入口没被收起，只剩一个白框。
		//
		// 两个独立原因：
		//   1. 白框本身：第 4 期的入口海报贴图在 3.2.0 客户端里就不存在，
		//      客户端日志有 "Couldn't find file for package /Game/UI/Picture/DLC04/gui_dlc04_btn001_01_Mul"。
		//      只要入口还画着就一定是白块 —— 所以这条不是「修图片」，而是必须把入口彻底收起。
		//   2. 收起没生效：老版 hideEntry 有两个致命点
		//      （a）只有「任意一个按钮当前 Visible」时才动手；切期瞬间往往是刚被客户端重刷过、
		//          Visibility 已经是别的值，于是整段跳过；
		//      （b）0.1 秒节流是「进函数先占坑」，客户端紧接着调 ShowBtnDlc 时包装层里的
		//          hideEntry 会被节流直接挡住，之后没有任何补收机会 → 白框一直挂着。
		// 现在改成：不节流、只要不是全部 Collapsed 就无条件收；并且和 BuildFix 对称，
		// 同时钩 UI.Open / UI.Close / umg_main.RefreshBtnState，客户端每次重刷后都会再收一次。
		// 外层套 pcall，失败时把错误写进 Config/Logs 便于下次定位。
		string script = """
local okAll, errAll = pcall(function()
_G.bMikuDlcWantShow = false
_G.bMikuHideEntry = true
local LOG = [[D:/DSH/.tools/_dlchide.log]]
local function dmMark(s) local f = io.open(LOG, [[a]]) if f then f:write(os.date([[%H:%M:%S]]) .. [[ ]] .. s .. [[\n]]) f:close() end end
dmMark([[BEGIN era=ERAID]])
local function dmFindMain() if type(UI) ~= [[table]] then return nil end local m for _,w in pairs(UI.tbWidget or {}) do if type(w) == [[table]] and tostring(w.sName) == [[main]] then m = w end end return m end
// 第 520 节（2026-11-22 23:10）：反汇编 ShowBtnDlc 发现这个客户端（3.6.0）的
// 入口控件是 Image_48/ImgLock，预告条是 BtnPreview（conf.sPreview 非空时
// SetVisibleOrCollapsed(BtnPreview, CheckShowPreview())），倒计时在 TipTime、
// 文案在 TextBlock_82 —— 老的四控件列表只收 BtnDLC 一族，BtnPreview 漏收，
// 游戏默认模式下「自在跨日还」预告条因此常驻主界面。补全收起名单。
local DM_BTNS = { [[BlankDLC]], [[BtnDLC]], [[NewDLC]], [[DLCLock]], [[Image_48]], [[ImgLock]], [[BtnPreview]], [[TipTime]], [[TextBlock_82]] }
local function dmGetMain() local m = _G.__dmMain if type(m) ~= [[table]] or m.BtnDLC == nil then m = dmFindMain() _G.__dmMain = m end return m end
local function dmAllCollapsed(m)
	if type(m) ~= [[table]] then return true end
	for i = 1, #DM_BTNS do local w = m[DM_BTNS[i]] if w ~= nil then local ok, v = pcall(function() return w.Visibility end) if ok and v ~= 1 then return false end end end
	return true
end
local function hideEntry()
	if _G.bMikuDlcWantShow == true then return end
	local m = dmGetMain()
	if type(m) ~= [[table]] then return end
	if dmAllCollapsed(m) then return end
	for i = 1, #DM_BTNS do local w = m[DM_BTNS[i]] if w ~= nil then pcall(WidgetUtils.Collapsed, w) end end
end
local function dmWrapShow(m)
	if type(m) ~= [[table]] or type(m.ShowBtnDlc) ~= [[function]] then return end
	if m.__dmHideWrap == true and m.ShowBtnDlc == m.__dmHideShow then return end
	m.__dmHideWrap = true
	local old = m.ShowBtnDlc
	m.ShowBtnDlc = function(self, ...) local a, b, c = old(self, ...) pcall(hideEntry) return a, b, c end
	m.__dmHideShow = m.ShowBtnDlc
end
local function dmEnsure()
	if _G.bMikuDlcWantShow == true then return end
	local m = dmGetMain()
	if type(m) ~= [[table]] then return end
	dmWrapShow(m)
	if dmAllCollapsed(m) then return end
	for i = 1, #DM_BTNS do local w = m[DM_BTNS[i]] if w ~= nil then pcall(WidgetUtils.Collapsed, w) end end
end
_G.MikuDmEnsureHide = dmEnsure
local function dmBad(src) return src:byte(1) == 91 and not string.find(src, [[Game/Content/Script]], 1, true) end
local function dmPeel(f)
	local d = 0
	while type(f) == [[function]] and d ~= 8 do
		local inf = debug.getinfo(f, [[S]])
		local src = tostring(inf and inf.short_src or [[]])
		if not dmBad(src) then break end
		local nxt
		for i = 1, 12 do local nm, v = debug.getupvalue(f, i) if nm == nil then break end if type(v) == [[function]] and (nm == [[oO]] or nm == [[oC]] or nm == [[oR]] or nm == [[pR]] or nm == [[oldOpen]] or nm == [[old]]) then nxt = v break end end
		if type(nxt) ~= [[function]] then break end
		f = nxt d = d + 1
	end
	return f, d
end
if type(UI) == [[table]] and type(UI.Open) == [[function]] then local oO = dmPeel(UI.Open) UI.Open = function(...) local a, b, c, d1, e1 = oO(...) pcall(dmEnsure) return a, b, c, d1, e1 end if type(UI.Close) == [[function]] then local oC = dmPeel(UI.Close) UI.Close = function(...) local a, b, c, d1, e1 = oC(...) pcall(dmEnsure) return a, b, c, d1, e1 end end else dmMark([[no-UI era=ERAID]]) end
local M = package.loaded["UMG.Main.umg_main"]
if type(M) == [[table]] and type(M.RefreshBtnState) == [[function]] then local pR = dmPeel(M.RefreshBtnState) M.RefreshBtnState = function(self, ...) local a, b, c = pR(self, ...) pcall(dmEnsure) return a, b, c end end
pcall(dmEnsure)
dmMark([[END era=ERAID want=]] .. tostring(_G.bMikuDlcWantShow))
end)
if not okAll then
	local f = io.open([[D:/DSH/.tools/_dlchide.log]], [[a]])
	if f then f:write(os.date([[%H:%M:%S]]) .. [[ ERROR ]] .. tostring(errAll) .. "\n") f:close() end
end
""";
		string oneLine = string.Join(" ", script.Split('\n').Select(delegate(string s)
		{
			return s.Trim();
		}).Where(delegate(string s)
		{
			return s.Length > 0;
		}));
		return oneLine.Replace("ERAID", eraId.ToString());
	}

	/// <summary>
	/// 第 356 节（2026-11-16 15:50）：坐实「下发脚本从来没执行」的根因。
	///
	/// 客户端收到 NtfCallScript 后统一走 Json.lua 的 decode，这个解码器碰到 '&lt;' 就
	/// attempt to call a nil value —— 整包丢弃，连 pcall 都进不去，服务端这边完全无声。
	/// 我们的 dmPeel 里原本写着 "d &lt; 8"，于是 BuildFix / BuildHide 这两份脚本
	/// 上线以来从没在客户端跑过一次（之前看到的日志全是手工 dofile 出来的）。
	///
	/// 治标是把 '&lt;' 换成 '~='，但脚本里任何一处比较都可能再踩同一个坑，
	/// 所以改成：脚本先写到本机文件，只下发一句 pcall(dofile, [[路径]])。
	/// 走网线的永远只有短 ASCII 语句，脚本多大、含什么字符都不会再被卡住。
	///
	/// 2026-11-23（第 527 节补充）：客户端侧 <c>dofile</c> 这条路失效了 ——
	/// 同一条连接、同一个 GM 通道，DlcClientPatches 的内联脚本照跑（wishyears.log 有更新），
	/// 而这里下发的 pcall(dofile, …) 连脚本第一行都没执行（_dlchide.log 全天无记录），
	/// 表现就是「切到 1~4 期，版本活动入口没被收起」。因为 io 库确认可用（客户端能写日志），
	/// 改成 io.open + loadstring 读同一份文件再执行，绕开 dofile。
	/// </summary>
	private static string DumpAndCall(string name, string lua)
	{
		string dir = "D:/DSH/.tools";
		string path = Path.Combine(dir, "_" + name + ".lua").Replace('\\', '/');
		try
		{
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, lua, new System.Text.UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			Log($"dump {name} 失败: {ex.Message}");
		}
		return "pcall(function() local f = io.open([[" + path + "]], [[r]]) if f == nil then return end local s = f:read([[*a]]) f:close() local fn = loadstring(s) if fn then fn() end end)";
	}

	private static void Send(Connection conn, string lua)
	{
		string arg = new JsonObject { ["code"] = lua }.ToJsonString();
		CallGSRouter.SendScript(conn, "GM", arg).GetAwaiter().GetResult();
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "dlcmain.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
