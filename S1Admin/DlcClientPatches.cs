using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class DlcClientPatches
{
	private static readonly char[] ForbiddenPatchChars = new char[6] { '+', '<', '>', '&', '\'', '"' };

	private static readonly object LogGate = new object();

	// 第 425 节：实测 0ms 下发也没用 —— 客户端把 GM 补丁排到主界面就绪后才执行
	// （2500ms 下发要 6.9 秒才落地），赶不上登录早期那批 1010 空名包，恢复三相。
	private static readonly int[] Delays = new int[3] { 2500, 9000, 20000 };

	/// <summary>
	/// Rogue3SeasonOpen：悖论迷宫的「验证战场」模式默认锁定。
	/// 客户端 uw_rogue_eventitem.RefeshUI 判定：Cfg.nSeasonLimit 大于 0 且
	/// Rogue3DLogic.CheckSeasonOpenConfig() 返回 nil 时，直接盖一层 PanelLock，
	/// 文案 tip 是 rogue3.other_modeLockTip（悖论迷宫-多维解集开启后解锁）。
	/// 而 CheckSeasonOpenConfig 内部走 GetCurSeasonConfig，取的是赛季表里
	/// 时间窗覆盖当前时刻的那一期；本地赛季表最新一期是 7（2025-10 开、2025-11 关），
	/// 早已过期，于是永远 nil。
	/// 这里把最新一期（赛季 7，nType=3=ScoreModel）的时间窗挪到「现在 ~ 30 天后」，
	/// 并给 CheckSeasonOpenConfig 包一层兜底：原函数返回 nil 时直接返回这一期。
	/// 效果：验证战场卡片不再盖锁；Rogue3DScoreModel.IsOpenSeason() 也返回 true，
	/// 点卡片能正常进 RogueWarMain。赛季内其它入口（商店/技能）跟着一起可见。
	/// </summary>
	public const string Rogue3SeasonOpen = "local function mikuSeasonFix()\nif type(_G.Rogue3DLogic) ~= [[table]] then return end\nlocal L = Rogue3DLogic\nif type(L.tbSeason) ~= [[table]] then return end\nlocal pick = L.tbSeason[#L.tbSeason]\nif type(pick) ~= [[table]] then\npick = nil\nfor _, v in pairs(L.tbSeason) do\nif type(v) == [[table]] and v.nType == L.SeasonType.ScoreModel then pick = v end\nend\nend\nif type(pick) ~= [[table]] then return end\nlocal now = GetTime()\nif type(now) ~= [[number]] or now == 0 then return end\nlocal off1 = 86400\nlocal off2 = -2592000\npick.nStartTime = now - off1\npick.nEndTime = now - off2\n_G.MikuSeasonPick = pick\nif type(_G.MikuSeasonOrig) ~= [[function]] then _G.MikuSeasonOrig = L.CheckSeasonOpenConfig end\nlocal orig = _G.MikuSeasonOrig\nif type(orig) == [[function]] and L.CheckSeasonOpenConfig ~= _G.MikuSeasonWrap then\nlocal wrapped = function()\nlocal cfg, tip = orig()\nif cfg ~= nil then return cfg, tip end\nreturn _G.MikuSeasonPick, tip\nend\nL.CheckSeasonOpenConfig = wrapped\n_G.MikuSeasonWrap = wrapped\nend\nend\npcall(mikuSeasonFix)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuSeasonHook ~= true then\n_G.bMikuSeasonHook = true\nlocal oldOpen = UI.Open\nUI.Open = function(...) pcall(mikuSeasonFix) local r = table.pack(oldOpen(...)) pcall(mikuSeasonFix) return table.unpack(r, 1, r.n) end\nend";

	/// <summary>
	/// Rogue3BlessFix：增益试炼打完第一波「卡住」的根因修。
	/// 客户端 umg_fight_rogue_bless.OnOpen 第一件事就是调
	/// Rogue3DSave.GetChaosRefreshBuffTotalCount() 取本波可选增益数，
	/// 而它读的是 Rogue3DSave.tbSaveData.cache.c_chaos_buffcount；
	/// 本端「验证战场」进关卡走 Rogue3DScoreModel_EnterBuffLevel，
	/// 全程没有任何一步调用 Rogue3DSave.Load，tbSaveData 恒为 nil，
	/// 于是 OnOpen 在取缓存时就抛 attempt to index a nil value (field tbSaveData)，
	/// TotalCount 留空；界面本身照常显示（三张增益卡都在），可点确认时
	/// OnBtnClickGO 里 TotalCount 与数字比较再抛一次 —— 表现就是
	/// 「打完第一波、增益界面点不动、战斗一直暂停」。
	/// 修法：保证 tbSaveData / cache 是表（Load 之后也兜一次）；
	/// 两个 chaos 计数读取包 pcall；仅在总计缺失时补 TotalCount 兜底值，
	/// 并在 GO 抛错时记一行日志到 D:/DSH/.tools/r3bless.log。
	/// </summary>
	public const string Rogue3BlessFix = "local function mikuR3Log(s)\nlocal ok, f = pcall(io.open, [[D:/DSH/.tools/r3bless.log]], [[a]])\nif ok and f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\nend\nlocal function mikuR3Ensure()\nlocal S = _G.Rogue3DSave\nif type(S) ~= [[table]] then return end\nif type(S.tbSaveData) ~= [[table]] then S.tbSaveData = {} end\nif type(S.tbSaveData.cache) ~= [[table]] then S.tbSaveData.cache = {} end\nend\nlocal function mikuR3WrapLoad()\nlocal S = _G.Rogue3DSave\nif type(S) ~= [[table]] then return end\nif type(S.Load) ~= [[function]] or _G.bMikuR3LoadWrap == true then return end\n_G.bMikuR3LoadWrap = true\nlocal old = S.Load\nS.Load = function(...)\nlocal r = table.pack(pcall(old, ...))\nmikuR3Ensure()\nif r[1] ~= true then mikuR3Log([[Load err: ]] .. tostring(r[2])) return nil end\nreturn table.unpack(r, 2, r.n)\nend\nend\nlocal function mikuR3WrapGetters()\nlocal S = _G.Rogue3DSave\nif type(S) ~= [[table]] then return end\nlocal names = { [[GetChaosRefreshBuffTotalCount]], [[GetChaosRefreshBuffCurCount]] }\nfor _, n in ipairs(names) do\nlocal key = [[MikuR3G_]] .. n\nif type(S[n]) == [[function]] and _G[key] ~= true then\n_G[key] = true\nlocal old = S[n]\nS[n] = function(...)\nlocal ok, v = pcall(old, ...)\nif not ok then mikuR3Log(n .. [[ err: ]] .. tostring(v)) return nil end\nreturn v\nend\nend\nend\nend\nlocal function mikuR3BlessHook()\nif type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] or _G.bMikuR3BlessHook == true then return end\n_G.bMikuR3BlessHook = true\nlocal oldOpen = UI.Open\nUI.Open = function(...)\nmikuR3Ensure()\nlocal r = table.pack(oldOpen(...))\npcall(function()\nlocal u = UI.GetUI and UI.GetUI([[FightRogueBless]])\nif type(u) ~= [[table]] then return end\nif u.TotalCount == nil then\nlocal S = _G.Rogue3DSave\nlocal n = nil\nif type(S) == [[table]] and type(S.GetChaosRefreshBuffTotalCount) == [[function]] then\nlocal ok, v = pcall(S.GetChaosRefreshBuffTotalCount, S)\nif ok then n = v end\nend\nu.TotalCount = n or 1\nmikuR3Log([[TotalCount fallback ]] .. tostring(u.TotalCount))\nend\nif type(u.OnBtnClickGO) == [[function]] and u.bMikuGO ~= true then\nu.bMikuGO = true\nlocal oldGO = u.OnBtnClickGO\nu.OnBtnClickGO = function(self, ...)\nlocal ok, err = pcall(oldGO, self, ...)\nif not ok then mikuR3Log([[GO err: ]] .. tostring(err)) end\nreturn nil\nend\nend\nend)\nreturn table.unpack(r, 1, r.n)\nend\nend\npcall(mikuR3WrapLoad)\npcall(mikuR3WrapGetters)\npcall(mikuR3BlessHook)\nmikuR3Ensure()";

	/// <summary>
	/// Rogue3WarWindowFix：验证战场「蚁穴矩阵 / 骁锐强袭」的战役节点全部显示
	/// 「开放倒计时：已结束」并被锁住（第 316 节）。
	///
	/// 根因在客户端配置：score_battle.json 的 OpenTime/CloseTime 是 2025-10-16 ~ 2025-11-06，
	/// 早已过期；GlobalTimeWindows 只放行「本期 floor 之后」的窗口，这 8 条太老，被 continue 掉。
	/// 客户端把这张表读进 Rogue3DScoreModel.tbScoreBattle（nOpenTime/nCloseTime 为 unix 秒），
	/// 界面的倒计时与锁定状态都取自这里，所以直接把每行的窗口推到「昨天 ~ 30 天后」。
	/// 同时 wrap LoadScoreBattle / OnInit / UI.Open，防止重载配置时又变回过期。
	/// </summary>
	public const string Rogue3WarWindowFix = "local function mikuWarWin()\nif type(_G.Rogue3DScoreModel) ~= [[table]] then return end\nlocal M = _G.Rogue3DScoreModel\nif type(M.tbScoreBattle) ~= [[table]] then return end\nlocal now = GetTime()\nif type(now) ~= [[number]] or now == 0 then return end\nlocal bChanged = false\nfor _, c in pairs(M.tbScoreBattle) do\nif type(c) == [[table]] then\nif tonumber(c.nCloseTime) ~= 4102444800 then\nc.nOpenTime = now - 31536000\nc.nCloseTime = 4102444800\nbChanged = true\nend\nend\nend\nif bChanged then M.bMikuWarWin = true end\nend\npcall(mikuWarWin)\nif type(_G.Rogue3DScoreModel) == [[table]] and _G.bMikuWarWinHooked ~= true then\n_G.bMikuWarWinHooked = true\nlocal M = _G.Rogue3DScoreModel\nlocal oldLoad = M.LoadScoreBattle\nif type(oldLoad) == [[function]] then\nM.LoadScoreBattle = function(...) local r = oldLoad(...) pcall(mikuWarWin) return r end\nend\nlocal oldInit = M.OnInit\nif type(oldInit) == [[function]] then\nM.OnInit = function(...) local r = oldInit(...) pcall(mikuWarWin) return r end\nend\nend\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuWarWinUI ~= true then\n_G.bMikuWarWinUI = true\nlocal oldOpen = UI.Open\nUI.Open = function(...) pcall(mikuWarWin) local r = table.pack(oldOpen(...)) pcall(mikuWarWin) return table.unpack(r, 1, r.n) end\nend";
	/// <summary>
	/// 第 494 节：纷愿绮梦「纷彩世界」页签点一下就弹「暂未开放」。
	///
	/// 客户端 luckyreward.OnOpen 的判定：
	///   local cur = me:GetAttribute(DreamCard.LevelGID, 0)
	///   local period = DreamCardConf.tbChallTimeConf[cur]
	///   bChallOpen = (period ~= nil)
	/// 本端 chall_time.json 最后一期（ID 4）时间窗是 2026-01-29 ~ 2026-05-14，早已过期；
	/// 服务端已把期号写进 152/0（DreamCardAchievementService.EnsureChallPeriod），
	/// 但客户端算倒计时、判到期用的是自己包里那份 nEndTime，几天后又会锁上。
	/// 这里把最后一期窗口推到「昨天 ~ 2100」，并在 UI.Open 前后各补一次。
	/// </summary>
	public const string DreamCardChallFix = "local function mikuDcChall()\nif type(_G.DreamCardConf) ~= [[table]] then return end\nlocal tb = DreamCardConf.tbChallTimeConf\nif type(tb) ~= [[table]] then return end\nlocal last = tb[#tb]\nif type(last) ~= [[table]] then return end\nlocal now = GetTime()\nif type(now) ~= [[number]] or now == 0 then return end\nif tonumber(last.nEndTime) == 4102444800 then return end\nlast.nStartTime = now - 86400\nlast.nEndTime = 4102444800\nend\npcall(mikuDcChall)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDcChall ~= true then\n_G.bMikuDcChall = true\nlocal oldOpen = UI.Open\nUI.Open = function(...) pcall(mikuDcChall) local r = table.pack(oldOpen(...)) pcall(mikuDcChall) return table.unpack(r, 1, r.n) end\nend";


	/// <summary>
	/// 2026-11-16（第 364 节）：第 27 期版本活动里「妮塔之邀」（活动 283）显示「暂未开放」。
	///
	/// 客户端探针实测（运行时取值，见 D:/DSH/.tools/act364*.log）：
	///   Activity.tbConfig[283].tbCondition = { {26,{1,30}}, {26,{2,50101}} }
	///   Activity.CheckOpen(283) = true    ← 时间窗是好的（服务端已按 now-86400 开出）
	///   Activity.GetOpenTime(283) = 昨天
	///   Activity.IsOpen(283) = false      ← 卡片锁定的判定来源
	/// 也就是时间没问题，卡在两条 {26} 类条件（本服这类主线前置条件永远不满足）。
	///
	/// 处理：清掉 283 的 tbCondition（配置表 + 分类表两处），并把 Activity.IsOpen
	/// 对 283 直接返回 true。只影响 283 一个活动；包装挂在 Activity 模块表上，
	/// 不经过 UI.*，不会被 DlcMainEntry 的 dmPeel 剥掉。
	/// </summary>
	public const string Act283Open = "local A283LOG = [[D:/DSH/.tools/_act283.log]]\n"
		+ "local function a283log(s)\n"
		+ "local f = io.open(A283LOG, [[a]])\n"
		+ "if f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "local function a283clear()\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] then return 0 end\n"
		+ "local n = 0\n"
		+ "if type(A.tbConfig) == [[table]] then\n"
		+ "local c = A.tbConfig[283]\n"
		+ "if type(c) == [[table]] and type(c.tbCondition) == [[table]] then\n"
		+ "c.tbCondition = {}\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "if type(A.tbClassConfig) == [[table]] and type(A.tbClassConfig.free_items) == [[table]] then\n"
		+ "for _, r in ipairs(A.tbClassConfig.free_items) do\n"
		+ "if type(r) == [[table]] and tonumber(r.nId) == 283 and type(r.tbCondition) == [[table]] then\n"
		+ "r.tbCondition = {}\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return n\n"
		+ "end\n"
		+ "local function a283hook()\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] or type(A.IsOpen) ~= [[function]] then return 0 end\n"
		+ "if A.bMiku283 == true then return 0 end\n"
		+ "local a283Prev = A.IsOpen\n"
		+ "A.IsOpen = function(nId, ...)\n"
		+ "if tonumber(nId) == 283 then return true end\n"
		+ "return a283Prev(nId, ...)\n"
		+ "end\n"
		+ "A.bMiku283 = true\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283pin(obj)\n"
		+ "if type(obj) ~= [[table]] then return 0 end\n"
		+ "obj.sFuncName = [[Nita]]\n"
		+ "obj.sFuncKey = [[dlc27Nita]]\n"
		+ "obj.GetFuncConfigId = function(o) return 283 end\n"
		+ "obj.OnGetFuncConfigId = function(o) return 283 end\n"
		+ "obj.CheckIsUnlock = function(o) return 0 end\n"
		+ "obj.OnCheckIsUnlock = function(o) return true end\n"
		+ "obj.GetTimeInfo = function(o) local now = os.time() return { now - 86400, now - (-5184000), now - (-5184000) } end\n"
		+ "obj.OnGetTimeInfo = function(o) local now = os.time() return { now - 86400, now - (-5184000), now - (-5184000) } end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283obj()\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "if type(Dl) ~= [[table]] then return 0 end\n"
		+ "local o = Dl.Nita\n"
		+ "if type(o) ~= [[table]] then o = {} Dl.Nita = o end\n"
		+ "a283pin(o)\n"
		+ "local n = 1\n"
		+ "local N = _G.DlcFuncNita\n"
		+ "if type(N) == [[table]] then\n"
		+ "N.GetFuncConfigId = function(obj) return 283 end\n"
		+ "N.OnGetFuncConfigId = function(obj) return 283 end\n"
		+ "N.OnGetTimeInfo = function(obj) local now = os.time() return { now - 86400, now - (-5184000), now - (-5184000) } end\n"
		+ "if type(N.OnOpenFunc) == [[function]] and N.bMiku283Wrap ~= true then\n"
		+ "N.bMiku283Wrap = true\n"
		+ "local prevOpen = N.OnOpenFunc\n"
		+ "N.OnOpenFunc = function(self, ...)\n"
		+ "a283pin(self)\n"
		+ "return prevOpen(self, ...)\n"
		+ "end\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "return n\n"
		+ "end\n"
		+ "local function a283win()\n"
		+ "local now = os.time()\n"
		+ "return now - 86400, now - (-5184000)\n"
		+ "end\n"
		+ "local function a283unlock()\n"
		+ "local N = _G.DlcFuncNita\n"
		+ "if type(N) ~= [[table]] then return 0 end\n"
		+ "if N.bMiku283Open == true then return 0 end\n"
		+ "N.bMiku283Open = true\n"
		+ "local prevU = N.OnCheckIsUnlock\n"
		+ "if type(prevU) == [[function]] then\n"
		+ "N.OnCheckIsUnlock = function(obj, a2)\n"
		+ "if type(obj) ~= [[table]] then return true end\n"
		+ "if obj.sFuncName == [[Nita]] or obj.sFuncKey == [[dlc27Nita]] then return true end\n"
		+ "return prevU(obj, a2)\n"
		+ "end\n"
		+ "end\n"
		+ "local prevT = N.OnGetTimeInfo\n"
		+ "if type(prevT) == [[function]] then\n"
		+ "N.OnGetTimeInfo = function(obj, a2)\n"
		+ "if type(obj) == [[table]] and (obj.sFuncName == [[Nita]] or obj.sFuncKey == [[dlc27Nita]]) then\n"
		+ "local st, et = a283win()\n"
		+ "return { st, et, et }\n"
		+ "end\n"
		+ "return prevT(obj, a2)\n"
		+ "end\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283state()\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "if type(Dl) ~= [[table]] or type(Dl.CheckIsUnlock) ~= [[function]] then return 0 end\n"
		+ "if Dl.bMiku283State == true then return 0 end\n"
		+ "Dl.bMiku283State = true\n"
		+ "local prevS = Dl.CheckIsUnlock\n"
		+ "Dl.CheckIsUnlock = function(a, b)\n"
		+ "local key = a\n"
		+ "if type(a) == [[table]] then key = a.sFuncName end\n"
		+ "if key == [[Nita]] then\n"
		+ "local fs = Dl.FuncState\n"
		+ "if type(fs) == [[table]] and fs.Open ~= nil then return fs.Open end\n"
		+ "return 0\n"
		+ "end\n"
		+ "return prevS(a, b)\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283open()\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "if type(Dl) ~= [[table]] or type(Dl.OpenFunc) ~= [[function]] then return 0 end\n"
		+ "if Dl.bMiku283OpenFn == true then return 0 end\n"
		+ "Dl.bMiku283OpenFn = true\n"
		+ "local prevO = Dl.OpenFunc\n"
		+ "Dl.OpenFunc = function(a, ...)\n"
		+ "if a == [[Nita]] then\n"
		+ "a283obj()\n"
		+ "if type(Dl.Nita) == [[table]] then a283pin(Dl.Nita) end\n"
		+ "end\n"
		+ "return prevO(a, ...)\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283base()\n"
		+ "local B = _G.DlcFuncBase\n"
		+ "if type(B) ~= [[table]] or type(B.CheckIsUnlock) ~= [[function]] then return 0 end\n"
		+ "if B.bMiku283Base == true then return 0 end\n"
		+ "B.bMiku283Base = true\n"
		+ "local prevC = B.CheckIsUnlock\n"
		+ "B.CheckIsUnlock = function(self, a2)\n"
		+ "if type(self) == [[table]] and (self.sFuncName == [[Nita]] or self.sFuncKey == [[dlc27Nita]]) then\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "local fs = nil\n"
		+ "if type(Dl) == [[table]] then fs = Dl.FuncState end\n"
		+ "if type(fs) == [[table]] and fs.Open ~= nil then return fs.Open, nil end\n"
		+ "return 0, nil\n"
		+ "end\n"
		+ "return prevC(self, a2)\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283gate()\n"
		+ "local B = _G.DlcFuncBase\n"
		+ "if type(B) ~= [[table]] or type(B.OpenFunc) ~= [[function]] then return 0 end\n"
		+ "if B.bMiku283Gate == true then return 0 end\n"
		+ "B.bMiku283Gate = true\n"
		+ "local prevO = B.OpenFunc\n"
		+ "B.OpenFunc = function(self, ...)\n"
		+ "local k = [[ ]]\n"
		+ "local nm = [[ ]]\n"
		+ "pcall(function() k = tostring(self.sFuncKey) end)\n"
		+ "pcall(function() nm = tostring(self.sFuncName) end)\n"
		+ "if k == [[dlc27Nita]] or nm == [[Nita]] then\n"
		+ "pcall(a283pin, self)\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "if type(Dl) == [[table]] and type(Dl.SetFirstCheck) == [[function]] then pcall(Dl.SetFirstCheck, self.sFuncKey) end\n"
		+ "if type(self.OnOpenFunc) == [[function]] then return self:OnOpenFunc(...) end\n"
		+ "return\n"
		+ "end\n"
		+ "return prevO(self, ...)\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283btn()\n"
		+ "local B = _G.uw_dlc27_main_btn\n"
		+ "if type(B) ~= [[table]] or type(B.Init) ~= [[function]] then return 0 end\n"
		+ "if B.bMiku283Btn == true then return 0 end\n"
		+ "B.bMiku283Btn = true\n"
		+ "local prevI = B.Init\n"
		+ "B.Init = function(self, ...)\n"
		+ "local r = prevI(self, ...)\n"
		+ "if type(self) == [[table]] and self.sFuncName == [[Nita]] then\n"
		+ "local W = _G.WidgetUtils\n"
		+ "if type(W) == [[table]] then\n"
		+ "pcall(function() if self.Lock then W.Collapsed(self.Lock) end end)\n"
		+ "pcall(function() if self.PanelContent then W.SetWidgetGray(self.PanelContent, false) end end)\n"
		+ "pcall(function() if self.Tips then W.Collapsed(self.Tips) end end)\n"
		+ "end\n"
		+ "end\n"
		+ "return r\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283tpl()\n"
		+ "local T = _G.uw_activity_template31\n"
		+ "if type(T) ~= [[table]] then return 0 end\n"
		+ "if T.bMiku283Tpl == true then return 0 end\n"
		+ "T.bMiku283Tpl = true\n"
		+ "local prevL = T.ShowLock\n"
		+ "if type(prevL) == [[function]] then\n"
		+ "T.ShowLock = function(self, ...)\n"
		+ "if type(self) == [[table]] and tonumber(self.nActivityId) == 283 then self.nReceiveTime = 0 end\n"
		+ "return prevL(self, ...)\n"
		+ "end\n"
		+ "end\n"
		+ "local prevO = T.ShowOpen\n"
		+ "if type(prevO) == [[function]] then\n"
		+ "T.ShowOpen = function(self, ...)\n"
		+ "if type(self) == [[table]] and tonumber(self.nActivityId) == 283 then self.nReceiveTime = 0 end\n"
		+ "return prevO(self, ...)\n"
		+ "end\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283init()\n"
		+ "local Dl = _G.DLC_Logic\n"
		+ "if type(Dl) ~= [[table]] or Dl.bMikuNita283 == true then return 0 end\n"
		+ "Dl.bMikuNita283 = true\n"
		+ "if type(Dl.InitFuncObj) == [[function]] then\n"
		+ "local prevI = Dl.InitFuncObj\n"
		+ "Dl.InitFuncObj = function(...)\n"
		+ "local r = prevI(...)\n"
		+ "a283obj() a283clear() a283open() a283base() a283gate()\n"
		+ "return r\n"
		+ "end\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local function a283load()\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] or A.bMiku283Load == true then return 0 end\n"
		+ "A.bMiku283Load = true\n"
		+ "if type(A.LoadActivitiesConfig) == [[function]] then\n"
		+ "local prevL = A.LoadActivitiesConfig\n"
		+ "A.LoadActivitiesConfig = function(...)\n"
		+ "local r = prevL(...)\n"
		+ "a283clear() a283obj() a283open() a283base() a283gate()\n"
		+ "return r\n"
		+ "end\n"
		+ "end\n"
		+ "return 1\n"
		+ "end\n"
		+ "local a283oc, a283nc = pcall(a283clear)\n"
		+ "local a283oh, a283nh = pcall(a283hook)\n"
		+ "local a283oo, a283no = pcall(a283obj)\n"
		+ "local a283ou, a283nu = pcall(a283unlock)\n"
		+ "local a283os, a283ns = pcall(a283state)\n"
		+ "local a283ox, a283nx = pcall(a283base)\n"
		+ "local a283og, a283ng = pcall(a283gate)\n"
		+ "local a283of, a283nf = pcall(a283open)\n"
		+ "local a283ob, a283nb = pcall(a283btn)\n"
		+ "local a283ot, a283nt = pcall(a283tpl)\n"
		+ "local a283oi, a283ni = pcall(a283init)\n"
		+ "local a283ol, a283nl = pcall(a283load)\n"
		+ "a283log([[enter clear=]] .. tostring(a283oc) .. [[/]] .. tostring(a283nc) .. [[ hook=]] .. tostring(a283oh) .. [[/]] .. tostring(a283nh) .. [[ obj=]] .. tostring(a283oo) .. [[/]] .. tostring(a283no) .. [[ unlock=]] .. tostring(a283ou) .. [[/]] .. tostring(a283nu) .. [[ state=]] .. tostring(a283os) .. [[/]] .. tostring(a283ns) .. [[ base=]] .. tostring(a283ox) .. [[/]] .. tostring(a283nx) .. [[ gate=]] .. tostring(a283og) .. [[/]] .. tostring(a283ng) .. [[ open=]] .. tostring(a283of) .. [[/]] .. tostring(a283nf) .. [[ btn=]] .. tostring(a283ob) .. [[/]] .. tostring(a283nb) .. [[ tpl=]] .. tostring(a283ot) .. [[/]] .. tostring(a283nt) .. [[ init=]] .. tostring(a283oi) .. [[/]] .. tostring(a283ni) .. [[ load=]] .. tostring(a283ol) .. [[/]] .. tostring(a283nl))";

	/// <summary>
	/// 2026-11-17（第 368 节）：爆爆菜园「伙伴编队」点加入队伍提示「可携带的蔬菜伙伴已满」。
	///
	/// 真因在客户端配置 farmwar/farmwarout.txt：本端第 1 期（ID=1，即
	/// FarmWarActivity.GetActivityId() 返回 1 的那一期）的 InitialVegSlot 与 VegSlotSkill
	/// 两列全空。探针实测（D:/DSH/.tools/k371.log、k372.log）：
	///   tbActivityCfg[1].nInitialVegSlot = 1
	///   tbVegSlotSkill = {}  →  FarmWarActivity.GetUnlockSlot() = 0
	///   GetFormation() 已有 2 个伙伴  →  0 不大于 2，PlantJoinTeam 直接判「已满」返回。
	/// GetUnlockSlot 只遍历 tbVegSlotSkill（实测取 max(v[1]) 后减 3），完全不看
	/// nInitialVegSlot，也没有服务端字段能影响它，所以只能在客户端把这一列补上。
	///
	/// 实验（k372.log）：把 tbVegSlotSkill 换成第 2 期同款 {{4,10148},{5,10149},{6,10150}}
	/// 后 GetUnlockSlot() 立刻变 3；换成 {{1,..},{2,..},{3,..}} 仍是 0。
	///
	/// 处理：所有 tbVegSlotSkill 为空的期号补成第 2 期同款（3 个伙伴位），
	/// 再给 GetUnlockSlot 包一层下限 3 的兜底，防止配置晚于补丁加载。
	/// 服务端 FarmWarLogic_UpdateFormation 只校验关卡 id、不校验槽位数，加队能正常落库。
	///
	/// 第 382 节（2026-11-17）：「伙伴编队」后 3 格永久锁死。下限 3 只解决了能编队，
	/// 4/5/6 格仍解不开：原版 GetUnlockSlot 遍历 act.tbVegSlotSkill，对每对 {槽位, 战技id}
	/// 调 GetSkillCfg(战技id, 期号):CheckCondition()，成立才把槽位抬上去；而 10148/10149/10150
	/// 三条全是 Schedule=2 的战技，期1 的 tbSkill 里不存在 → GetSkillCfg 返回 nil → 永不解锁。
	/// 期1 官方 farmwarout.txt 的 VegSlotSkill / InitialVegSlot 两列本来就空（只有 1 格），
	/// 这 3 格是第 2 期才有的扩展位，官方期2 的条件是通关 8892 / 8894 / 8896
	/// （该期第一章的第 1、3、5 关），所以按同样节奏映射到期1：通关 8712 / 8714 / 8716
	/// 各开一格，最多 6 格（N_MAX_FORMATION）。
	/// </summary>
	public const string FarmWarSlotFix = "local FWLOG = [[D:/DSH/.tools/fwslot.log]]\n"
		+ "local function fwlog(s)\n"
		+ "local f = io.open(FWLOG, [[a]])\n"
		+ "if f then f:write(os.date([[%H:%M:%S]]) .. [[ ]] .. tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "local function fwPassed(id)\n"
		+ "local m = _G.me\n"
		+ "if m == nil then return false end\n"
		+ "local ok, val = pcall(function() return m:GetAttribute(22, id) end)\n"
		+ "if ok ~= true then return false end\n"
		+ "if type(val) ~= [[number]] then return false end\n"
		+ "return val ~= 0\n"
		+ "end\n"
		+ "local function fwSlotFix()\n"
		+ "local A = _G.FarmWarActivity\n"
		+ "if type(A) ~= [[table]] then return false end\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "local n = 0\n"
		+ "if type(C) == [[table]] and type(C.tbActivityCfg) == [[table]] then\n"
		+ "for _, a in pairs(C.tbActivityCfg) do\n"
		+ "if type(a) == [[table]] then\n"
		+ "local s = a.tbVegSlotSkill\n"
		+ "if type(s) ~= [[table]] or #s == 0 then\n"
		+ "a.tbVegSlotSkill = { {4, 10148}, {5, 10149}, {6, 10150} }\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "if A.bMikuFwSlot ~= true and type(A.GetUnlockSlot) == [[function]] then\n"
		+ "A.bMikuFwSlot = true\n"
		+ "local old = A.GetUnlockSlot\n"
		+ "A.GetUnlockSlot = function(...)\n"
		+ "local v = old(...)\n"
		+ "if type(v) ~= [[number]] then v = 0 end\n"
		+ "local ns = 3\n"
		+ "if fwPassed(8712) then ns = 4 end\n"
		+ "if fwPassed(8714) then ns = 5 end\n"
		+ "if fwPassed(8716) then ns = 6 end\n"
		+ "return math.min(math.max(ns, v), 6)\n"
		+ "end\n"
		+ "end\n"
		+ "fwlog([[apply cfg=]] .. tostring(n) .. [[ unlock=]] .. tostring(A.GetUnlockSlot()))\n"
		+ "return true\n"
		+ "end\n"
		+ "local fwok, fwerr = pcall(fwSlotFix)\n"
		+ "if fwok ~= true then fwlog([[ERR ]] .. tostring(fwerr)) end\n";

	/// <summary>
	/// 2026-11-17（第 368 节）：「培育历程」三页里「伙伴培育」「道具调试」两页完全空白。
	///
	/// 真因同样在客户端 farmwarout.txt：第 1 期（ID=1）7 条培育记录的 VegOrNot 列全空，
	/// 第 2 期才填了 1 / 2。而 UMG.FarmWar.umg_farmwar_grow.GetGrowPlants 的过滤是
	///   选中标签 nType == 1 时取 nVegOrNot == 1，否则取 nVegOrNot == 2；
	/// nil 两边都不等，于是两页都是空列表（界面只有背景和按钮）。
	/// 运行时实验（k374.log，直接 pcall 调 GetGrowPlants）：
	///   veg 全 nil                    → nType 1/2/3/4/5 全是 0 条
	///   type==1 给 2、其余给 1（guessA）→ nType 1 得 5 条，nType 2~5 各得 2 条
	///   全给 1                        → nType 1 得 7 条，其余 0
	///   全给 2                        → nType 1 得 0 条，其余 7 条
	/// 由此确定：nType 1 = 伙伴培育（veg 1），其它标签 = 道具调试（veg 2）。
	///
	/// 处理：凡是 nVegOrNot 为 nil 的培育行，nType==1 的补 2、其余补 1（与第 2 期同规则）。
	/// 因为配置可能在补丁之后才加载，包装 umg_farmwar_grow 的 OnOpen 与 GetGrowPlants，
	/// 每次进入/取数前先补一遍。第三页「极限培育」读 tbExtreme[1]，第 1 期原版没有这张表
	/// （ExtremeUnlockTime 也是空），属于本版设计上就没有，不在本补丁范围内。
	/// </summary>
	public const string FarmWarGrowFix = "local GWLOG = [[D:/DSH/.tools/fwgrow.log]]\n"
		+ "local function gwlog(s)\n"
		+ "local f = io.open(GWLOG, [[a]])\n"
		+ "if f then f:write(os.date([[%H:%M:%S]]) .. [[ ]] .. tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "local function gwFix()\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] then return 0 end\n"
		+ "local tb = C.tbDevelop\n"
		+ "if type(tb) ~= [[table]] then return 0 end\n"
		+ "local n = 0\n"
		+ "for _, rows in pairs(tb) do\n"
		+ "if type(rows) == [[table]] then\n"
		+ "for _, r in pairs(rows) do\n"
		+ "if type(r) == [[table]] and r.nVegOrNot == nil then\n"
		+ "if r.nType == 1 then r.nVegOrNot = 2 else r.nVegOrNot = 1 end\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return n\n"
		+ "end\n"
		+ "local function gwInstall()\n"
		+ "local gname = [[UMG.FarmWar.umg_farmwar_grow]]\n"
		+ "local G = package.loaded[gname]\n"
		+ "if type(G) ~= [[table]] then return false end\n"
		+ "if G.bMikuGrow == true then return true end\n"
		+ "G.bMikuGrow = true\n"
		+ "local oldOpen = G.OnOpen\n"
		+ "if type(oldOpen) == [[function]] then\n"
		+ "G.OnOpen = function(self, ...)\n"
		+ "pcall(gwFix)\n"
		+ "return oldOpen(self, ...)\n"
		+ "end\n"
		+ "end\n"
		+ "local oldGet = G.GetGrowPlants\n"
		+ "if type(oldGet) == [[function]] then\n"
		+ "G.GetGrowPlants = function(self, ...)\n"
		+ "pcall(gwFix)\n"
		+ "return oldGet(self, ...)\n"
		+ "end\n"
		+ "end\n"
		+ "return true\n"
		+ "end\n"
		+ "local a, b = pcall(gwFix)\n"
		+ "local c, d = pcall(gwInstall)\n"
		+ "gwlog([[fix=]] .. tostring(a) .. [[/]] .. tostring(b) .. [[ install=]] .. tostring(c) .. [[/]] .. tostring(d))";

	public const string FarmWarLevelFix = "local FWLOG = [[D:/DSH/.tools/fwlevel.log]]\n"
		+ "local function fwllog(s)\n"
		+ "local f = io.open(FWLOG, [[a]])\n"
		+ "if f then f:write(os.date([[%H:%M:%S]]) .. [[ ]] .. tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "\n"
		+ "local function fwlIsFarmLevel(id)\n"
		+ "if type(id) ~= [[number]] then return false end\n"
		+ "local L = _G.FarmWarLevel\n"
		+ "if type(L) ~= [[table]] then return false end\n"
		+ "local tb = L.tbLevel\n"
		+ "if type(tb) ~= [[table]] then return false end\n"
		+ "if tb[id] == nil then return false end\n"
		+ "return true\n"
		+ "end\n"
		+ "\n"
		+ "local function fwlInstall()\n"
		+ "local M = _G.Map\n"
		+ "if type(M) ~= [[table]] then return false end\n"
		+ "if M.bMikuFwLevelFix == true then return true end\n"
		+ "M.bMikuFwLevelFix = true\n"
		+ "local old = M.IsCanOpen\n"
		+ "if type(old) ~= [[function]] then return false end\n"
		+ "M.IsCanOpen = function(id, ...)\n"
		+ "if fwlIsFarmLevel(id) then\n"
		+ "fwllog([[canopen ]] .. tostring(id))\n"
		+ "return true\n"
		+ "end\n"
		+ "return old(id, ...)\n"
		+ "end\n"
		+ "fwllog([[install]])\n"
		+ "return true\n"
		+ "end\n"
		+ "\n"
		+ "local ok, v = pcall(fwlInstall)\n"
		+ "fwllog([[boot ]] .. tostring(ok) .. [[/]] .. tostring(v))";

	public const string FarmWarItemFix = "local FILOG = [[D:/DSH/.tools/fwitem.log]]\n"
		+ "local function filog(s)\n"
		+ "local f = io.open(FILOG, [[a]])\n"
		+ "if f then f:write(os.date([[%H:%M:%S]]) .. [[ ]] .. tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "\n"
		+ "local fiBusy = false\n"
		+ "\n"
		+ "local function fiAct()\n"
		+ "local A = _G.FarmWarActivity\n"
		+ "if type(A) ~= [[table]] then return nil end\n"
		+ "local ok, id = pcall(A.GetActivityId)\n"
		+ "if ok and type(id) == [[number]] and id ~= 0 then return id end\n"
		+ "return nil\n"
		+ "end\n"
		+ "\n"
		+ "local function fiSer(t)\n"
		+ "if type(t) ~= [[table]] then return tostring(t) end\n"
		+ "local out = [[{]]\n"
		+ "for _, cond in ipairs(t) do\n"
		+ "out = out .. [[(]]\n"
		+ "if type(cond) == [[table]] then\n"
		+ "for _, v in ipairs(cond) do out = out .. tostring(v) .. [[,]] end\n"
		+ "else\n"
		+ "out = out .. tostring(cond)\n"
		+ "end\n"
		+ "out = out .. [[)]]\n"
		+ "end\n"
		+ "return out .. [[}]]\n"
		+ "end\n"
		+ "\n"
		+ "local function fiItems(act)\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] then return 0 end\n"
		+ "if type(C.tbItem) ~= [[table]] then return 0 end\n"
		+ "local tb = C.tbItem[act]\n"
		+ "if type(tb) ~= [[table]] then return 0 end\n"
		+ "local map = {}\n"
		+ "for s, rows in pairs(C.tbItem) do\n"
		+ "if type(rows) == [[table]] then\n"
		+ "for _, r in pairs(rows) do\n"
		+ "if type(r) == [[table]] and r.nTeamOrNot == 1 and type(r.sName) == [[string]] then\n"
		+ "map[r.sName] = 1\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local n = 0\n"
		+ "for _, r in pairs(tb) do\n"
		+ "if type(r) == [[table]] and r.nTeamOrNot == nil and type(r.sName) == [[string]] and map[r.sName] == 1 then\n"
		+ "r.nTeamOrNot = 1\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "end\n"
		+ "return n\n"
		+ "end\n"
		+ "\n"
		+ "local function fiSegs()\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "local ids = {}\n"
		+ "if type(C) ~= [[table]] or type(C.tbLevel) ~= [[table]] then return {} end\n"
		+ "for id, cfg in pairs(C.tbLevel) do\n"
		+ "if type(id) == [[number]] then ids[#ids - (-1)] = id end\n"
		+ "end\n"
		+ "table.sort(ids)\n"
		+ "local segs = {}\n"
		+ "local cur = nil\n"
		+ "local prev = nil\n"
		+ "for _, id in ipairs(ids) do\n"
		+ "if cur == nil or id - prev ~= 1 then\n"
		+ "cur = {}\n"
		+ "segs[#segs - (-1)] = cur\n"
		+ "end\n"
		+ "cur[#cur - (-1)] = id\n"
		+ "prev = id\n"
		+ "end\n"
		+ "return segs\n"
		+ "end\n"
		+ "\n"
		+ "local function fiMapCond(act, cond)\n"
		+ "if type(cond) ~= [[table]] then return cond end\n"
		+ "if cond[1] ~= 1 then return cond end\n"
		+ "local lid = cond[2]\n"
		+ "if type(lid) ~= [[number]] then return cond end\n"
		+ "local segs = fiSegs()\n"
		+ "local si = nil\n"
		+ "local pos = nil\n"
		+ "for i, seg in ipairs(segs) do\n"
		+ "for j, id in ipairs(seg) do\n"
		+ "if id == lid then si = i pos = j end\n"
		+ "end\n"
		+ "end\n"
		+ "if si == nil or pos == nil then return cond end\n"
		+ "local dst = segs[act]\n"
		+ "if dst == nil then return cond end\n"
		+ "local tid = dst[pos]\n"
		+ "if type(tid) ~= [[number]] then return cond end\n"
		+ "if tid == lid then return cond end\n"
		+ "return { cond[1], tid }\n"
		+ "end\n"
		+ "\n"
		+ "local function fiFixSkill(act, sid)\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] or type(C.tbSkill) ~= [[table]] then return 0 end\n"
		+ "if type(C.tbSkill[act]) ~= [[table]] then C.tbSkill[act] = {} end\n"
		+ "local row = C.tbSkill[act][sid]\n"
		+ "if type(row) ~= [[table]] then return 0 end\n"
		+ "if row.nSchedule == act and row.bMikuCondFix == true then return 0 end\n"
		+ "local cp = {}\n"
		+ "for k, v in pairs(row) do cp[k] = v end\n"
		+ "cp.nSchedule = act\n"
		+ "local uc = {}\n"
		+ "for _, cond in ipairs(row.tbUnlockCondition or {}) do\n"
		+ "uc[#uc - (-1)] = fiMapCond(act, cond)\n"
		+ "end\n"
		+ "cp.tbUnlockCondition = uc\n"
		+ "cp.bMikuCondFix = true\n"
		+ "local mt = getmetatable(row)\n"
		+ "if mt ~= nil then setmetatable(cp, mt) end\n"
		+ "C.tbSkill[act][sid] = cp\n"
		+ "return 1\n"
		+ "end\n"
		+ "\n"
		+ "local function fiExtreme(act)\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] then return 0 end\n"
		+ "if type(C.tbExtreme) ~= [[table]] then return 0 end\n"
		+ "local n = 0\n"
		+ "local ss = nil\n"
		+ "local src = nil\n"
		+ "for s, rows in pairs(C.tbExtreme) do\n"
		+ "if type(rows) == [[table]] and s ~= act then src = rows ss = s end\n"
		+ "end\n"
		+ "if C.tbExtreme[act] == nil then\n"
		+ "if src == nil then return 0 end\n"
		+ "local dst = {}\n"
		+ "for _, r in ipairs(src) do\n"
		+ "local ids = {}\n"
		+ "for _, v in ipairs(r.tbSkillID or {}) do ids[#ids - (-1)] = v end\n"
		+ "dst[#dst - (-1)] = { nId = r.nId, nSchedule = act, tbSkillID = ids }\n"
		+ "end\n"
		+ "C.tbExtreme[act] = dst\n"
		+ "if type(C.tbSkill) ~= [[table]] then C.tbSkill = {} end\n"
		+ "if type(C.tbSkill[act]) ~= [[table]] then C.tbSkill[act] = {} end\n"
		+ "for _, r in ipairs(dst) do\n"
		+ "for _, sid in ipairs(r.tbSkillID) do\n"
		+ "if C.tbSkill[act][sid] == nil and type(C.tbSkill[ss]) == [[table]] then\n"
		+ "C.tbSkill[act][sid] = C.tbSkill[ss][sid]\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "n = n - (-1)\n"
		+ "end\n"
		+ "if type(C.tbSkill) ~= [[table]] then C.tbSkill = {} end\n"
		+ "if type(C.tbSkill[act]) ~= [[table]] then C.tbSkill[act] = {} end\n"
		+ "for _, r in ipairs(C.tbExtreme[act]) do\n"
		+ "for _, sid in ipairs(r.tbSkillID or {}) do\n"
		+ "local ok, v = pcall(fiFixSkill, act, sid)\n"
		+ "if ok and type(v) == [[number]] then n = n - (-v) end\n"
		+ "end\n"
		+ "end\n"
		+ "return n\n"
		+ "end\n"
		+ "\n"
		+ "local function fiWork(act)\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "local nItem = 0\n"
		+ "local nExt = 0\n"
		+ "local o1, v1 = pcall(fiItems, act)\n"
		+ "if type(v1) == [[number]] then nItem = v1 end\n"
		+ "local o2, v2 = pcall(fiExtreme, act)\n"
		+ "if type(v2) == [[number]] then nExt = v2 end\n"
		+ "local a = nil\n"
		+ "if type(C.tbActivityCfg) == [[table]] then a = C.tbActivityCfg[act] end\n"
		+ "if type(a) == [[table]] then\n"
		+ "local before = fiSer(a.tbExtremeUnlockTime)\n"
		+ "local dst = fiSegs()[act]\n"
		+ "local tgt = nil\n"
		+ "if dst ~= nil then tgt = dst[10] end\n"
		+ "if tgt ~= nil then\n"
		+ "a.tbExtremeUnlockTime = { { 1, tgt } }\n"
		+ "else\n"
		+ "local tb = {}\n"
		+ "for _, cond in ipairs(a.tbExtremeUnlockTime or {}) do tb[#tb - (-1)] = fiMapCond(act, cond) end\n"
		+ "a.tbExtremeUnlockTime = tb\n"
		+ "end\n"
		+ "local after = fiSer(a.tbExtremeUnlockTime)\n"
		+ "if before ~= after then\n"
		+ "filog([[condmap act=]] .. tostring(act) .. [[ ]] .. before .. [[ to ]] .. after)\n"
		+ "end\n"
		+ "end\n"
		+ "if nItem ~= 0 or nExt ~= 0 then\n"
		+ "filog([[apply act=]] .. tostring(act) .. [[ items=]] .. tostring(nItem) .. [[ extreme=]] .. tostring(nExt))\n"
		+ "end\n"
		+ "return true\n"
		+ "end\n"
		+ "\n"
		+ "local function fiApply(act)\n"
		+ "if fiBusy then return false end\n"
		+ "if type(act) ~= [[number]] then act = fiAct() end\n"
		+ "if type(act) ~= [[number]] then return false end\n"
		+ "fiBusy = true\n"
		+ "local ok, r = pcall(fiWork, act)\n"
		+ "fiBusy = false\n"
		+ "return ok\n"
		+ "end\n"
		+ "\n"
		+ "local function fiApplyAll()\n"
		+ "if fiBusy then return false end\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] then return false end\n"
		+ "if type(C.tbActivityCfg) ~= [[table]] then return false end\n"
		+ "fiBusy = true\n"
		+ "for act, a in pairs(C.tbActivityCfg) do\n"
		+ "if type(act) == [[number]] then pcall(fiWork, act) end\n"
		+ "end\n"
		+ "fiBusy = false\n"
		+ "return true\n"
		+ "end\n"
		+ "\n"
		+ "local function fiInstall()\n"
		+ "local U = _G.FarmWarActivityUtils\n"
		+ "if type(U) == [[table]] and U.bMikuItemFix ~= true then\n"
		+ "U.bMikuItemFix = true\n"
		+ "local oldSort = U.GetSortItems\n"
		+ "if type(oldSort) == [[function]] then\n"
		+ "U.GetSortItems = function(a, ...)\n"
		+ "local act = a\n"
		+ "if type(act) ~= [[number]] then act = fiAct() end\n"
		+ "if type(act) == [[number]] then pcall(fiApply, act) end\n"
		+ "return oldSort(a, ...)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local A = _G.FarmWarActivity\n"
		+ "if type(A) == [[table]] and A.bMikuItemFixV2 ~= true then\n"
		+ "A.bMikuItemFixV2 = true\n"
		+ "local oldEx = A.GetExtremeCfg\n"
		+ "if type(oldEx) == [[function]] then\n"
		+ "A.GetExtremeCfg = function(...)\n"
		+ "pcall(fiApplyAll)\n"
		+ "return oldEx(...)\n"
		+ "end\n"
		+ "end\n"
		+ "local oldCk = A.CheckExtremeSkillOpen\n"
		+ "if type(oldCk) == [[function]] then\n"
		+ "A.CheckExtremeSkillOpen = function(...)\n"
		+ "pcall(fiApplyAll)\n"
		+ "return oldCk(...)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return true\n"
		+ "end\n"
		+ "\n"
		+ "local ok2, v2 = pcall(fiInstall)\n"
		+ "local ok1, v1 = pcall(fiApply)\n"
		+ "filog([[boot v2 fix=]] .. tostring(ok1) .. [[/]] .. tostring(v1) .. [[ install=]] .. tostring(ok2) .. [[/]] .. tostring(v2))";

	public const string FarmWarPlantFix = "local MGRNAME = [[Level.FarmWar.FarmWarMgr]]\n"
		+ "local POTNAME = [[Level.FarmWar.Actors.FarmWarPlantPotV2]]\n"
		+ "local MENUNAME = [[UMG.FarmWar.umg_farmwar_fight_menu]]\n"
		+ "local LOGICNAME = [[Level.FarmWar.FarmWarLogic]]\n"
		+ "local LOG = [[D:/DSH/.tools/fwplant.log]]\n"
		+ "local PL = package.loaded\n"
		+ "local function lg(s)\n"
		+ "  local f = io.open(LOG, [[a]])\n"
		+ "  if f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "local function mod(name)\n"
		+ "  local m = PL[name]\n"
		+ "  if type(m) == [[table]] then return m end\n"
		+ "  return nil\n"
		+ "end\n"
		+ "local function act()\n"
		+ "  local a = _G.FarmWarActivity\n"
		+ "  if type(a) == [[table]] then return a end\n"
		+ "  return nil\n"
		+ "end\n"
		+ "local function ensure()\n"
		+ "  local L = _G.FarmWarLogic\n"
		+ "  if type(L) ~= [[table]] then return false, 0, 0 end\n"
		+ "  local A = act()\n"
		+ "  local uv, n = nil, 0\n"
		+ "  if A ~= nil then\n"
		+ "    local ok, t = pcall(A.GetUnlockVegetables)\n"
		+ "    if ok and type(t) == [[table]] then uv = t n = #t end\n"
		+ "  end\n"
		+ "  local nc = 0\n"
		+ "  if type(L.UnLockVegetables) == [[table]] then nc = #L.UnLockVegetables end\n"
		+ "  if nc == 0 and n ~= 0 then\n"
		+ "    local cp = {}\n"
		+ "    for i, v in ipairs(uv) do cp[i] = v end\n"
		+ "    L.UnLockVegetables = cp\n"
		+ "    nc = n\n"
		+ "  end\n"
		+ "  local nb = 0\n"
		+ "  if type(L.UnLockBaseVegetables) == [[table]] then nb = #L.UnLockBaseVegetables end\n"
		+ "  if nb == 0 then\n"
		+ "    local out = {}\n"
		+ "    if A ~= nil then\n"
		+ "      local ok2, form = pcall(A.GetFormation)\n"
		+ "      if ok2 and type(form) == [[table]] and type(form[2]) == [[table]] then\n"
		+ "        for _, id in ipairs(form[2]) do\n"
		+ "          local okc, cfg = pcall(A.GetPlantCfg, id)\n"
		+ "          if okc and type(cfg) == [[table]] and cfg.nVegetablesID ~= nil then\n"
		+ "            out[#out - (-1)] = cfg.nVegetablesID\n"
		+ "          end\n"
		+ "        end\n"
		+ "      end\n"
		+ "    end\n"
		+ "    if #out == 0 and nc ~= 0 then\n"
		+ "      local cnt = 0\n"
		+ "      for _, id in ipairs(L.UnLockVegetables) do\n"
		+ "        cnt = cnt - (-1)\n"
		+ "        if math.max(cnt, 3) == 3 then out[#out - (-1)] = id end\n"
		+ "      end\n"
		+ "    end\n"
		+ "    L.UnLockBaseVegetables = out\n"
		+ "    nb = #out\n"
		+ "  end\n"
		+ "  if type(L.VegBattleInfo) ~= [[table]] then\n"
		+ "    pcall(L.ResetVegBattleInfo)\n"
		+ "  end\n"
		+ "  return true, nc, nb\n"
		+ "end\n"
		+ "local function resetCounters(L)\n"
		+ "  if type(L) ~= [[table]] then return end\n"
		+ "  local keys = {[[LevelPlantNum]], [[LevelUpgradeNum]], [[VegKill]], [[CharKill]], [[FieldSkillCastNum]]}\n"
		+ "  for _, k in ipairs(keys) do L[k] = 0 end\n"
		+ "  if type(L.VegBattleInfo) == [[table]] then\n"
		+ "    for _, v in pairs(L.VegBattleInfo) do\n"
		+ "      if type(v) == [[table]] then\n"
		+ "        v.PlantNum = 0 v.KillNum = 0 v.EliminateNum = 0 v.KilledNum = 0\n"
		+ "        v.AttackDamage = 0 v.DefendDamage = 0 v.GenerateEnergy = 0\n"
		+ "      end\n"
		+ "    end\n"
		+ "  end\n"
		+ "end\n"
		+ "local function installMgr()\n"
		+ "  local M = mod(MGRNAME)\n"
		+ "  if M == nil then return false end\n"
		+ "  if rawget(M, [[bMikuPlantFix]]) == nil then\n"
		+ "    rawset(M, [[bMikuPlantFix]], true)\n"
		+ "    local oldInit = M.OnInit\n"
		+ "    M.OnInit = function(self, ...)\n"
		+ "      rawset(_G, [[_MikuFarmWarMgr]], self)\n"
		+ "      if type(self) == [[table]] then\n"
		+ "        rawset(self, [[OpenPlantPlane]], function(inst, tb)\n"
		+ "          local ui = inst and inst.PlantUI\n"
		+ "          if ui == nil or not IsValid(ui) then return false end\n"
		+ "          return pcall(function() ui:OpenPlantPlane(tb) end)\n"
		+ "        end)\n"
		+ "      end\n"
		+ "      local r = nil\n"
		+ "      if type(oldInit) == [[function]] then r = oldInit(self, ...) end\n"
		+ "      local ok, nc, nb = ensure()\n"
		+ "      pcall(resetCounters, _G.FarmWarLogic)\n"
		+ "      lg([[mgr init ensure=]] .. tostring(ok) .. [[ unlock=]] .. tostring(nc) .. [[ base=]] .. tostring(nb))\n"
		+ "      return r\n"
		+ "    end\n"
		+ "    local oldRefresh = M.RefreshPlantPlane\n"
		+ "    if type(oldRefresh) == [[function]] then\n"
		+ "      M.RefreshPlantPlane = function(self, ...)\n"
		+ "        pcall(ensure)\n"
		+ "        return oldRefresh(self, ...)\n"
		+ "      end\n"
		+ "    end\n"
		+ "    lg([[mgr hooked]])\n"
		+ "  end\n"
		+ "  return true\n"
		+ "end\n"
		+ "local function installPot()\n"
		+ "  local P = mod(POTNAME)\n"
		+ "  if P == nil then return false end\n"
		+ "  if rawget(P, [[bMikuSafe]]) == nil then\n"
		+ "    rawset(P, [[bMikuSafe]], true)\n"
		+ "    local old = P.TriggerInteract\n"
		+ "    if type(old) == [[function]] then\n"
		+ "      P.TriggerInteract = function(self, ...)\n"
		+ "        pcall(ensure)\n"
		+ "        local ok = pcall(old, self, ...)\n"
		+ "        if ok then return true end\n"
		+ "        local mgr = self and self.FarmWarMgr\n"
		+ "        local ui = mgr and mgr.PlantUI\n"
		+ "        if ui ~= nil and IsValid(ui) then\n"
		+ "          local ok2 = pcall(function() ui:OpenPlantPlane({ Pot = self }) end)\n"
		+ "          if ok2 then\n"
		+ "            pcall(self.ResetInteractState, self)\n"
		+ "            return true\n"
		+ "          end\n"
		+ "        end\n"
		+ "        local c = nil\n"
		+ "        pcall(function()\n"
		+ "          local pc = UE4.UGameplayStatics.GetPlayerController(GetGameIns(), 0)\n"
		+ "          c = pc:Cast(UE4.AGamePlayerController)\n"
		+ "        end)\n"
		+ "        if c ~= nil and IsValid(c) then\n"
		+ "          pcall(c.UnLockControlAndUpdateUI, c, UE4.ELockCtrlReason.Interact)\n"
		+ "        end\n"
		+ "        pcall(RuntimeState.ChangeInputMode, false)\n"
		+ "        return false\n"
		+ "      end\n"
		+ "    end\n"
		+ "    lg([[pot hooked]])\n"
		+ "  end\n"
		+ "  return true\n"
		+ "end\n"
		+ "local function installMenu()\n"
		+ "  local W = mod(MENUNAME)\n"
		+ "  if W == nil then return false end\n"
		+ "  if rawget(W, [[bMikuMenuFix]]) == nil then\n"
		+ "    rawset(W, [[bMikuMenuFix]], true)\n"
		+ "    local oldRefresh = W.RefreshPlane\n"
		+ "    if type(oldRefresh) == [[function]] then\n"
		+ "      W.RefreshPlane = function(self, ...)\n"
		+ "        local ok, nc, nb = ensure()\n"
		+ "        local r = oldRefresh(self, ...)\n"
		+ "        local n = -1\n"
		+ "        local arr = self and self.ItemArr\n"
		+ "        if type(arr) == [[table]] then n = #arr end\n"
		+ "        lg([[menu refresh ensure=]] .. tostring(ok) .. [[ unlock=]] .. tostring(nc) .. [[ base=]] .. tostring(nb) .. [[ items=]] .. tostring(n))\n"
		+ "        return r\n"
		+ "      end\n"
		+ "    end\n"
		+ "    local oldOpen = W.OpenPlantPlane\n"
		+ "    if type(oldOpen) == [[function]] then\n"
		+ "      W.OpenPlantPlane = function(self, ...)\n"
		+ "        pcall(ensure)\n"
		+ "        return oldOpen(self, ...)\n"
		+ "      end\n"
		+ "    end\n"
		+ "    lg([[menu hooked]])\n"
		+ "  end\n"
		+ "  return true\n"
		+ "end\n"
		+ "local function installLogic()\n"
		+ "  local L = mod(LOGICNAME)\n"
		+ "  if L == nil then return false end\n"
		+ "  if rawget(L, [[bMikuLogicFix]]) == nil then\n"
		+ "    rawset(L, [[bMikuLogicFix]], true)\n"
		+ "    local oldReset = L.ResetAllLevelCache\n"
		+ "    if type(oldReset) == [[function]] then\n"
		+ "      L.ResetAllLevelCache = function(...)\n"
		+ "        local r = oldReset(...)\n"
		+ "        local ok, nc, nb = ensure()\n"
		+ "        lg([[logic reset ensure=]] .. tostring(ok) .. [[ unlock=]] .. tostring(nc) .. [[ base=]] .. tostring(nb))\n"
		+ "        return r\n"
		+ "      end\n"
		+ "    end\n"
		+ "    local oldUnlock = L.ResetAllUnLockVegetables\n"
		+ "    if type(oldUnlock) == [[function]] then\n"
		+ "      L.ResetAllUnLockVegetables = function(self, ...)\n"
		+ "        local ok0 = pcall(oldUnlock, self, ...)\n"
		+ "        local ok, nc, nb = ensure()\n"
		+ "        if ok0 ~= true then\n"
		+ "          lg([[logic unlockfail ensure=]] .. tostring(ok) .. [[ unlock=]] .. tostring(nc) .. [[ base=]] .. tostring(nb))\n"
		+ "        end\n"
		+ "      end\n"
		+ "    end\n"
		+ "    lg([[logic hooked]])\n"
		+ "  end\n"
		+ "  return true\n"
		+ "end\n"
		+ "local function installBuff()\n"
		+ "  local A = _G.FarmWarActivity\n"
		+ "  if type(A) ~= [[table]] then return false end\n"
		+ "  if rawget(A, [[bMikuBuffIcon]]) == true then return true end\n"
		+ "  local old = A.GetBuffState\n"
		+ "  if type(old) ~= [[function]] then return false end\n"
		+ "  rawset(A, [[bMikuBuffIcon]], true)\n"
		+ "  A.GetBuffState = function(x)\n"
		+ "    local v = old(x)\n"
		+ "    if type(v) ~= [[number]] then return 0 end\n"
		+ "    local info = debug.getinfo(2, [[S]])\n"
		+ "    if info ~= nil then\n"
		+ "      local src = info.source\n"
		+ "      if type(src) == [[string]] then\n"
		+ "        if string.find(src, [[itembuff]]) then\n"
		+ "          if v ~= 0 then return 2 end\n"
		+ "        end\n"
		+ "      end\n"
		+ "    end\n"
		+ "    return v\n"
		+ "  end\n"
		+ "  lg([[buff icon hooked]])\n"
		+ "  return true\n"
		+ "end\n"
		+ "local function installByName(name)\n"
		+ "  if name == MGRNAME then return installMgr() end\n"
		+ "  if name == POTNAME then return installPot() end\n"
		+ "  if name == MENUNAME then return installMenu() end\n"
		+ "  if name == LOGICNAME then return installLogic() end\n"
		+ "  return nil\n"
		+ "end\n"
		+ "local function hookRequire()\n"
		+ "  if rawget(_G, [[bMikuReqPlant]]) == true then return end\n"
		+ "  local old = _G.require\n"
		+ "  if type(old) ~= [[function]] then return end\n"
		+ "  rawset(_G, [[bMikuReqPlant]], true)\n"
		+ "  _G.require = function(name, ...)\n"
		+ "    local r = old(name, ...)\n"
		+ "    if type(name) == [[string]] then installByName(name) end\n"
		+ "    if type(name) == [[string]] then if string.find(name, [[FarmWarActivity]]) then installBuff() end end\n"
		+ "    return r\n"
		+ "  end\n"
		+ "  lg([[require hooked]])\n"
		+ "end\n"
		+ "local frames = 0\n"
		+ "local tick\n"
		+ "local function tryAll()\n"
		+ "  hookRequire()\n"
		+ "  local a = installMgr()\n"
		+ "  local b = installPot()\n"
		+ "  local c = installMenu()\n"
		+ "  local d = installLogic()\n"
		+ "  local e = installBuff()\n"
		+ "  return a, b, c, d, e\n"
		+ "end\n"
		+ "tick = function()\n"
		+ "  frames = frames - (-1)\n"
		+ "  local a, b, c, d, e = tryAll()\n"
		+ "  local done = a and b and c and e\n"
		+ "  if done or math.max(frames, 3600) == frames then\n"
		+ "    lg([[install frames=]] .. tostring(frames) .. [[ done=]] .. tostring(done) .. [[ mgr=]] .. tostring(a) .. [[ pot=]] .. tostring(b) .. [[ menu=]] .. tostring(c) .. [[ logic=]] .. tostring(d) .. [[ buff=]] .. tostring(e))\n"
		+ "    return\n"
		+ "  end\n"
		+ "  if UE4 ~= nil and UE4.Timer ~= nil and type(UE4.Timer.NextFrame) == [[function]] then\n"
		+ "    UE4.Timer.NextFrame(tick)\n"
		+ "  else\n"
		+ "    lg([[install no timer frames=]] .. tostring(frames))\n"
		+ "  end\n"
		+ "end\n"
		+ "local M0 = _G.Map\n"
		+ "if type(M0) == [[table]] and rawget(M0, [[bMikuPlantOpen]]) == nil and type(M0.Open) == [[function]] then\n"
		+ "  rawset(M0, [[bMikuPlantOpen]], true)\n"
		+ "  local oldMapOpen = M0.Open\n"
		+ "  M0.Open = function(...)\n"
		+ "    local r = oldMapOpen(...)\n"
		+ "    frames = 0\n"
		+ "    tick()\n"
		+ "    return r\n"
		+ "  end\n"
		+ "end\n"
		+ "tick()";

	public const string ChessWin = "local CWLOG = [[D:/DSH/.tools/chesswin.log]]\n"
		+ "local function cwlog(s)\n"
		+ "local f = io.open(CWLOG, [[a]])\n"
		+ "if f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "local function cwnow()\n"
		+ "if type(GetTime) == [[function]] then\n"
		+ "local ok, t = pcall(GetTime)\n"
		+ "if ok and type(t) == [[number]] and t ~= 0 then return t end\n"
		+ "end\n"
		+ "return os.time()\n"
		+ "end\n"
		+ "local function cwapply(tag)\n"
		+ "local CL = _G.ChessLogic\n"
		+ "if type(CL) ~= [[table]] then return -1 end\n"
		+ "local tb = CL.tbTimeConf\n"
		+ "if type(tb) ~= [[table]] then return -2 end\n"
		+ "local id = nil\n"
		+ "if type(CL.GetOpenID) == [[function]] then\n"
		+ "local ok, v = pcall(CL.GetOpenID)\n"
		+ "if ok then id = tonumber(v) end\n"
		+ "end\n"
		+ "if not id or id == 0 then\n"
		+ "for k in pairs(tb) do\n"
		+ "if type(k) == [[number]] then\n"
		+ "if id == nil or (k ~= id and math.max(k, id) == k) then id = k end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local c = id and tb[id]\n"
		+ "if type(c) ~= [[table]] then return -3 end\n"
		+ "local now = cwnow()\n"
		+ "local b = tonumber(c.nBeginTime) or 0\n"
		+ "local e = tonumber(c.nEndTime) or 0\n"
		+ "if math.max(b, now) == now and math.max(e, now) == e then return 0 end\n"
		+ "c.nBeginTime = now - 86400\n"
		+ "c.nEndTime = now - (-2592000)\n"
		+ "cwlog([[fix ]] .. tostring(tag) .. [[ id=]] .. tostring(id) .. [[ ]] .. tostring(b) .. [[~]] .. tostring(e) .. [[ now=]] .. tostring(now))\n"
		+ "return 1\n"
		+ "end\n"
		+ "local okA, rA = pcall(cwapply, [[install]])\n"
		+ "local CL = _G.ChessLogic\n"
		+ "if type(CL) == [[table]] then\n"
		+ "if type(CL.LoadTimeConf) == [[function]] and rawget(CL, [[bMikuCw]]) ~= true then\n"
		+ "CL.bMikuCw = true\n"
		+ "local oldL = CL.LoadTimeConf\n"
		+ "CL.LoadTimeConf = function(...)\n"
		+ "local r = oldL(...)\n"
		+ "pcall(cwapply, [[load]])\n"
		+ "return r\n"
		+ "end\n"
		+ "end\n"
		+ "if type(CL.LoadConfig) == [[function]] and rawget(CL, [[bMikuCwCfg]]) ~= true then\n"
		+ "CL.bMikuCwCfg = true\n"
		+ "local oldC = CL.LoadConfig\n"
		+ "CL.LoadConfig = function(...)\n"
		+ "local r = oldC(...)\n"
		+ "pcall(cwapply, [[cfg]])\n"
		+ "return r\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuCwOpen ~= true then\n"
		+ "_G.bMikuCwOpen = true\n"
		+ "local oldOpen = UI.Open\n"
		+ "UI.Open = function(a, ...)\n"
		+ "if a == [[richman]] then pcall(cwapply, [[pre]]) end\n"
		+ "local r = table.pack(oldOpen(a, ...))\n"
		+ "if a == [[richman]] then pcall(cwapply, [[post]]) end\n"
		+ "return table.unpack(r, 1, r.n)\n"
		+ "end\n"
		+ "end\n"
		+ "cwlog([[=== chunk cw v1 ===]] .. [[ install=]] .. tostring(okA) .. [[/]] .. tostring(rA))";
	/// <summary>
	/// 2026-11-17（第 387 节）：星球守护「伙伴手册 / 助战少女」全条目上锁。
	///
	/// 真因（客户端探针实锤，非通关记录问题）：
	///   AlienDefense.GetPalUnlockState(idx, 1, false, isGirl) 的最终结果取自
	///   me:GetAttribute(GID=143, PalUnlockStart=251 + idx) 的**位图**（GirlUnlockStart=551），
	///   该位图由官方活动结算链路写入；本服 AlienDefense_SetPalFlag 是空壳，
	///   属性 143/251~300、143/551~600 全是 0 → 20 个伙伴 + 10 个少女恒 false
	///   → 界面全锁，且未解锁的卡片会显示 CheckPalCondition 的第二返回值（满足时为 nil）
	///   → 卡面出现 (NULL)（波伏娃、幻射会员两条无条件条目最明显）。
	///
	/// 修法：包装 AlienDefense.GetPalUnlockState，原实现返回 true 时直接透传；
	///   否则按当期配置 tbCondition 走「关卡通关时间」兜底判定
	///   （AlienDefenseLevel.GetLevelConfig(id):GetPassTime() > 0，实测 6231 命中、
	///     6232/6233 为 0），条件为空（#cond == 0）的默认伙伴直接视为解锁。
	///   实测第 4 期：伙伴 1~4 / 少女 1~4 解锁，5~10、5~6 仍锁（对应 6232 及以后未通关）。
	/// 注意：补丁下发时 AlienDefense 模块可能尚未 require，故同时挂 require 钩子补装；
	///       文本内不出现 '<'（客户端 Json.lua 会整包丢弃）。
	/// </summary>
	public const string AlienPalUnlockFix = "local function mpuInstall()\n"
		+ "local A = _G.AlienDefense\n"
		+ "local C = _G.AlienDefenseConfig\n"
		+ "local L = _G.AlienDefenseLevel\n"
		+ "if type(A) ~= [[table]] then return end\n"
		+ "if A.bMikuPalUnlock == true then return end\n"
		+ "if type(C) ~= [[table]] or type(L) ~= [[table]] then return end\n"
		+ "if type(A.GetPalUnlockState) ~= [[function]] then return end\n"
		+ "A.bMikuPalUnlock = true\n"
		+ "local function mpuPass(id)\n"
		+ "local okl, cfg = pcall(L.GetLevelConfig, id)\n"
		+ "if not okl or type(cfg) ~= [[table]] then return 0 end\n"
		+ "local okp, v = pcall(function() return cfg:GetPassTime() end)\n"
		+ "if not okp then return 0 end\n"
		+ "return tonumber(v) or 0\n"
		+ "end\n"
		+ "local function mpuCfg(idx, girl)\n"
		+ "local act = A.GetActId()\n"
		+ "if girl and type(C.GetGirlConfigByIdx) == [[function]] then\n"
		+ "local okg, cg = pcall(C.GetGirlConfigByIdx, act, idx)\n"
		+ "if okg and type(cg) == [[table]] then return cg end\n"
		+ "end\n"
		+ "local ok, c = pcall(C.GetPalConfigByIdx, act, idx)\n"
		+ "if ok and type(c) == [[table]] then return c end\n"
		+ "return nil\n"
		+ "end\n"
		+ "local mpuOld = A.GetPalUnlockState\n"
		+ "A.GetPalUnlockState = function(idx, a, b, girl)\n"
		+ "local ok, r = pcall(mpuOld, idx, a, b, girl)\n"
		+ "if ok and r == true then return true end\n"
		+ "local cfg = mpuCfg(idx, girl)\n"
		+ "if type(cfg) ~= [[table]] then return false end\n"
		+ "local cond = cfg.tbCondition\n"
		+ "if type(cond) ~= [[table]] or #cond == 0 then return true end\n"
		+ "local lv = tonumber(cond[2])\n"
		+ "if not lv then return false end\n"
		+ "return math.max(mpuPass(lv), 0) ~= 0\n"
		+ "end\n"
		+ "end\n"
		+ "pcall(mpuInstall)\n"
		+ "if type(require) == [[function]] and _G.bMikuPalUnlockReq ~= true then\n"
		+ "_G.bMikuPalUnlockReq = true\n"
		+ "local mpuReq = require\n"
		+ "require = function(name, ...)\n"
		+ "local r = mpuReq(name, ...)\n"
		+ "if type(_G.AlienDefense) == [[table]] then pcall(mpuInstall) end\n"
		+ "return r\n"
		+ "end\n"
		+ "end";

	public const string SeasonBossYmirScene = @"-- SeasonBossYmirScene（第 445 节）：第 24 期噬神斗场「塑界一」尤弥尔大厅预览只显示两根黑色巨柱。
-- 根因：关卡 2501/2511/2521/2531/2541 的场景配置 nSceneData=1，那份 scenedata 是给
-- 尼亚尔（600120603）按 Scale=0.42 调好的；尤弥尔(600120301) 是游戏里体型最大的泰坦，
-- 0.42 渲染直接顶出镜头，只剩两条腿的黑色剪影（2026-11-03 四十一节的「纯观感」结论作废）。
-- 对照：同一只尤弥尔在第 3 期塑界一（2721~2725）用 scenedata 7（Scale 0.032），
-- 第 30 期塑界二（3011~3015）用 13，两处都是完整泰坦。
-- 修法：把这 5 关的 nSceneData 改成 7（原生尤弥尔取景），并在关卡/场景表加载后再补一刀。
-- 注意：upvalue 名不能叫 old/oldOpen/r（DlcMainEntry 切期剥层会按这些名字剥包装）。
local LOG = [[D:/DSH/.tools/sbymir.log]]
local function sbylog(s)
    local ok, f = pcall(io.open, LOG, [[a]])
    if ok and f then
        f:write(tostring(s))
        f:write(string.char(10))
        f:close()
    end
end
local IDs = { 2501, 2511, 2521, 2531, 2541 }
local SCENE = 7
local function sbyfix()
    local L = _G.SeasonBossLogic
    if type(L) ~= [[table]] then return 0 end
    local t = L.tbLevelCfg
    if type(t) ~= [[table]] then return 0 end
    local sd = L.tbSceneDataCfg
    if type(sd) ~= [[table]] or type(sd[SCENE]) ~= [[table]] then return 0 end
    local n = 0
    for i = 1, #IDs do
        local e = t[IDs[i]]
        if type(e) == [[table]] and e.nSceneData ~= SCENE then
            e.nSceneData = SCENE
            n = n - (-1)
        end
    end
    if n ~= 0 then sbylog([[fix scene ]] .. tostring(n)) end
    return n
end
local function sbywrap(name)
    local L = _G.SeasonBossLogic
    if type(L) ~= [[table]] then return end
    if type(L[name]) ~= [[function]] then return end
    local key = [[bMikuSbYmir_]] .. name
    if _G[key] == true then return end
    _G[key] = true
    local sbyPrevFn = L[name]
    L[name] = function(...)
        local sbyPk = table.pack(sbyPrevFn(...))
        pcall(sbyfix)
        return table.unpack(sbyPk, 1, sbyPk.n)
    end
    sbylog([[wrap ]] .. name)
end
pcall(sbyfix)
pcall(sbywrap, [[LoadBossLevelCfg]])
pcall(sbywrap, [[LoadSceneDataCfg]])
pcall(sbywrap, [[LoadCfg]])
if type(_G.UI) == [[table]] and type(_G.UI.Open) == [[function]] and _G.bMikuSbYmirOpen ~= true then
    _G.bMikuSbYmirOpen = true
    local sbyPrevOpen = _G.UI.Open
    _G.UI.Open = function(...)
        pcall(sbywrap, [[LoadBossLevelCfg]])
        pcall(sbywrap, [[LoadSceneDataCfg]])
        local sbyPk2 = table.pack(sbyPrevOpen(...))
        pcall(sbyfix)
        return table.unpack(sbyPk2, 1, sbyPk2.n)
    end
end
return [[ok]]
";

	public const string SeasonBossZoneHide = "-- SeasonBossZoneDual（第 422 正式版）：噬神斗场「塑界一/塑界二」两块名称牌错开显示。\n-- 421d 反汇编 uw_seasonboss_zone.UpdateUIPos：牌 X = Boss 世界位置投影X 减 BtnZone 宽/2，\n-- Y = BtnZone 槽位Y；两个 Boss 场景配置 17/18 的 BossPosition 只差 30cm Z，投影几乎重合，\n-- 两牌完全叠死。本节实测：对 PanelZone1/PanelZone2 的 CanvasSlot SetPosition 立即渲染生效\n-- （zone 牌的原生定位机制就是 SetPosition，421 会话「SetPosition 无视觉效果」结论是对象错误）。\n-- 做法：包装主实例 UpdateUIPos/MoveUIPos/Tick，每次调用后把 PanelZone2 相对 PanelZone1\n-- 当前槽位右移 800px 同高摆放（以 P1 为基准，幂等不漂移；图 1 样式左右分开）。PanelZone1/PanelZone2 是主实例字段\n-- （UpdateZoneInfo 里 self[PanelZone..nZoneID]:Init 创建），vis=4 为渲染可见魔术值。\nlocal function mikuSbApply(inst)\nif type(inst) ~= [[table]] then return end\nlocal Z1 = inst.PanelZone1\nlocal Z2 = inst.PanelZone2\nlocal E = _G.UE4\nif type(Z1) ~= [[table]] or type(Z2) ~= [[table]] or type(E) ~= [[table]] then return end\nlocal U = E.UWidgetLayoutLibrary\nlocal F = E.FVector\nif type(U) ~= [[table]] or type(F) ~= [[table]] then return end\npcall(function()\nlocal sl1 = U.SlotAsCanvasSlot(Z1)\nlocal p1 = sl1:GetPosition()\nif not p1 then return end\nlocal sl2 = U.SlotAsCanvasSlot(Z2)\nsl2:SetPosition(F(p1.X - (-800), p1.Y, 0))\nend)\nend\nlocal function mikuSbWrapInst(w)\nif type(w) ~= [[table]] or w.bMikuSbW22 == true then return end\nw.bMikuSbW22 = true\nfor _, fn in ipairs({[[UpdateUIPos]], [[MoveUIPos]], [[Tick]]}) do\nlocal old = w[fn]\nif type(old) == [[function]] then\nw[fn] = function(s, ...)\nlocal r = old(s, ...)\npcall(function() mikuSbApply(s) end)\nreturn r\nend\nend\nend\nend\nlocal function mikuSbEach()\nfor _, w in pairs(_G.UI.tbWidget or {}) do\nif type(w) == [[table]] and tostring(w.sName) == [[dungeonsseasonboss]] then\nmikuSbWrapInst(w)\nend\nend\nend\nlocal function mikuSbInstall()\nlocal K = [[UMG.DungeonsSeasonBoss.umg_dungeonsseasonboss_main]]\nlocal M = package.loaded[K]\nif type(M) == [[table]] then mikuSbWrapInst(M) end\nend\nlocal function mikuSbEntry()\nlocal T = _G.DLC_Logic and _G.DLC_Logic.Teamboss\nif type(T) ~= [[table]] or T.bMikuSbEntry22 == true then return end\nT.bMikuSbEntry22 = true\nlocal oldOF = T.OnOpenFunc\nif type(oldOF) == [[function]] then\nT.OnOpenFunc = function(s, ...)\nlocal r = oldOF(s, ...)\nmikuSbInstall()\nmikuSbEach()\nreturn r\nend\nend\nend\nmikuSbInstall()\nmikuSbEach()\nmikuSbEntry()\nlocal DL = _G.DLC_Logic\nif type(DL) == [[table]] and type(DL.InitFuncObj) == [[function]] and DL.bMikuSbZoneHook22 ~= true then\nDL.bMikuSbZoneHook22 = true\nlocal oldInit = DL.InitFuncObj\nDL.InitFuncObj = function(...)\nlocal r = oldInit(...)\nmikuSbInstall()\nmikuSbEach()\nmikuSbEntry()\nreturn r\nend\nend\nif type(_G.UI) == [[table]] and type(_G.UI.Open) == [[function]] and _G.bMikuSbOpenHook22 ~= true then\n_G.bMikuSbOpenHook22 = true\nlocal oldOpen = _G.UI.Open\n_G.UI.Open = function(a, ...)\nlocal r = oldOpen(a, ...)\nmikuSbInstall()\nmikuSbEach()\nreturn r\nend\nend\n";

	/// <summary>
	/// 2026-11-18（第 423 节）：第 33 期 DLC 窗口里的「适格启忆录」（拼图 / jigsaw_puzzle）一直锁着。
	///
	/// 链路（421 会话同款坑，逐层验证过）：
	///   uw_dlc33_main_btn 的点击 → DLC_Logic.OpenFunc(Puzzle) → 取当前期的 tbDlcFuncConfId.Puzzle = 390
	///   → DlcFuncBase.CheckIsUnlock → 卡面状态 = DLC_Logic.CheckIsUnlock(Puzzle) → Activity.IsOpen(390)。
	/// 而 390 的 Excel 窗口写的是历史期（2026-04-02 ~ 2026-05-14），本期 DLC 窗口时间被改写成
	/// 「现在 ~ 现在+30 天」，服务端登录体检把属性 50/3900 还原成历史值，客户端于是判成「未开启」，
	/// 卡片灰着、点进去提示「暂未开放」。
	///
	/// 服务端已按 ActivityWindowGuard.IsAlwaysOpenClass 把 jigsaw_puzzle 类活动常开（属性写本刻）。
	/// 客户端这层负责三件事，保证不依赖登录时序：
	///   1. Activity.IsOpen 对本期 Puzzle 活动 id 直接放行（外层包装，兜住属性晚到）；
	///   2. DlcFuncBase.CheckIsUnlock 对 Puzzle 卡片返回 Open（卡面与点击都走这层）；
	///   3. 已打开的 DLC 主窗口重跑 UpdateBtnDynamicInfo（窗口 1.5 秒就弹，比属性早）。
	/// upvalue 名字刻意用 pzPrev 系列，避开 DlcMainEntry 的剥层名单（oO/oC/oR/pR/oldOpen/old）。
	/// </summary>
	public const string DlcPuzzleOpen = "local function mikuPzId()\n"
		+ "local D = _G.DLC_Logic\n"
		+ "if type(D) ~= [[table]] or type(D.GetCurConf) ~= [[function]] then return 0 end\n"
		+ "local ok, conf = pcall(D.GetCurConf)\n"
		+ "if not ok or type(conf) ~= [[table]] then return 0 end\n"
		+ "local t = conf.tbDlcFuncConfId\n"
		+ "if type(t) ~= [[table]] then return 0 end\n"
		+ "return tonumber(t.Puzzle) or 0\n"
		+ "end\n"
		+ "local function mikuPzNow()\n"
		+ "local n = 0\n"
		+ "if type(GetTime) == [[function]] then local ok, t = pcall(GetTime) if ok then n = tonumber(t) or 0 end end\n"
		+ "return n\n"
		+ "end\n"
		+ "local function mikuPzSet()\n"
		+ "local id = mikuPzId()\n"
		+ "if id == 0 then return end\n"
		+ "local set = _G.tbMikuPuzzleForce\n"
		+ "if type(set) ~= [[table]] then set = {} _G.tbMikuPuzzleForce = set end\n"
		+ "set[id] = true\n"
		+ "local A = _G.Activity\n"
		+ "local row = A and A.tbConfig and A.tbConfig[id]\n"
		+ "if type(row) == [[table]] then\n"
		+ "local now = mikuPzNow()\n"
		+ "if now ~= 0 then\n"
		+ "row.nStartTime = now - 3600\n"
		+ "row.nEndTime = now - (-2592000)\n"
		+ "end\n"
		+ "if row.nCloseFlag ~= 0 then row.nCloseFlag = 0 end\n"
		+ "row.tbCondition = nil\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuPzAllow(nId)\n"
		+ "local gd = _G.MikuTryDead\n"
		+ "if type(gd) == [[function]] and gd(nId) then return false end\n"
		+ "local id = tonumber(nId) or 0\n"
		+ "if id == 0 then return false end\n"
		+ "local set = _G.tbMikuPuzzleForce\n"
		+ "if type(set) == [[table]] and set[id] then return true end\n"
		+ "if mikuPzId() == id then return true end\n"
		+ "local D = _G.DLC_Logic\n"
		+ "if type(D) == [[table]] and type(D.GetCurConf) == [[function]] then\n"
		+ "local ok, conf = pcall(D.GetCurConf)\n"
		+ "if ok and type(conf) == [[table]] and type(conf.tbDlcFuncConfId) == [[table]] then\n"
		+ "for _, v in pairs(conf.tbDlcFuncConfId) do\n"
		+ "if tonumber(v) == id then return true end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return false\n"
		+ "end\n"
		+ "local function mikuPzHook()\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] then return end\n"
		+ "if type(A.IsOpen) == [[function]] and A.IsOpen ~= _G.fnMikuPzIsOpen then\n"
		+ "A.bMikuPuzzleGate = true\n"
		+ "local pzPrev = A.IsOpen\n"
		+ "_G.fnMikuPzIsOpen = function(nId, ...)\n"
		+ "if mikuPzAllow(nId) then return true, nil end\n"
		+ "return pzPrev(nId, ...)\n"
		+ "end\n"
		+ "A.IsOpen = _G.fnMikuPzIsOpen\n"
		+ "end\n"
		+ "local FW = rawget(A, [[__FBW]])\n"
		+ "if type(FW) == [[function]] and FW ~= _G.fnMikuPzFbw then\n"
		+ "local pzPrevF = FW\n"
		+ "_G.fnMikuPzFbw = function(nId, ...)\n"
		+ "if mikuPzAllow(nId) then return true, nil end\n"
		+ "return pzPrevF(nId, ...)\n"
		+ "end\n"
		+ "rawset(A, [[__FBW]], _G.fnMikuPzFbw)\n"
		+ "if type(rawget(A, [[__FBWN]])) == [[function]] then rawset(A, [[__FBWN]], _G.fnMikuPzFbw) end\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuPzCard()\n"
		+ "local D = _G.DLC_Logic\n"
		+ "if type(D) == [[table]] then\n"
		+ "if type(_G.fnMikuPzCardSelf) ~= [[function]] then\n"
		+ "_G.fnMikuPzCardSelf = function(self, ...) return 0 end\n"
		+ "end\n"
		+ "for _, v in pairs(D) do\n"
		+ "if type(v) == [[table]] and rawget(v, [[sFuncName]]) == [[Puzzle]] then\n"
		+ "if rawget(v, [[CheckIsUnlock]]) ~= _G.fnMikuPzCardSelf then\n"
		+ "rawset(v, [[CheckIsUnlock]], _G.fnMikuPzCardSelf)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local B = _G.DlcFuncBase\n"
		+ "if type(B) == [[table]] and type(B.CheckIsUnlock) == [[function]] and B.CheckIsUnlock ~= _G.fnMikuPzCard then\n"
		+ "local pzPrevC = B.CheckIsUnlock\n"
		+ "_G.fnMikuPzCard = function(s, ...)\n"
		+ "if type(s) == [[table]] and rawget(s, [[sFuncName]]) == [[Puzzle]] then return 0 end\n"
		+ "return pzPrevC(s, ...)\n"
		+ "end\n"
		+ "B.CheckIsUnlock = _G.fnMikuPzCard\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuFwId()\n"
		+ "local D = _G.DLC_Logic\n"
		+ "if type(D) ~= [[table]] or type(D.GetCurConf) ~= [[function]] then return 0 end\n"
		+ "local ok, conf = pcall(D.GetCurConf)\n"
		+ "if not ok or type(conf) ~= [[table]] then return 0 end\n"
		+ "local t = conf.tbDlcFuncConfId\n"
		+ "if type(t) ~= [[table]] then return 0 end\n"
		+ "return tonumber(t.Farmwar) or 0\n"
		+ "end\n"
		+ "local function mikuFwFix()\n"
		+ "local C = _G.FarmWarActivityCfg\n"
		+ "if type(C) ~= [[table]] then return end\n"
		+ "local now = mikuPzNow()\n"
		+ "if now == 0 then return end\n"
		+ "local st = now - 3600\n"
		+ "local et = now - (-2592000)\n"
		+ "local id = mikuFwId()\n"
		+ "local acts = C.tbActivityCfg\n"
		+ "if type(acts) == [[table]] and id ~= 0 and type(acts[id]) == [[table]] then\n"
		+ "acts[id].nStartTime = st\n"
		+ "acts[id].nEndTime = et\n"
		+ "end\n"
		+ "local chs = C.tbChapter\n"
		+ "if type(chs) == [[table]] and id ~= 0 and type(chs[id]) == [[table]] then\n"
		+ "for _, c in pairs(chs[id]) do\n"
		+ "if type(c) == [[table]] then\n"
		+ "c.nStartTime = st\n"
		+ "c.nEndTime = et\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuSwGate()\n"
		+ "local R = _G.FunctionRouter\n"
		+ "if type(R) ~= [[table]] or type(R.IsOpenById) ~= [[function]] then return end\n"
		+ "if R.IsOpenById == _G.fnMikuSwGate then return end\n"
		+ "local prev = R.IsOpenById\n"
		+ "local FT = _G.FunctionType\n"
		+ "local nStar = 0\n"
		+ "if type(FT) == [[table]] and type(FT.StarWish) == [[number]] then nStar = FT.StarWish end\n"
		+ "_G.fnMikuSwGate = function(nType, ...)\n"
		+ "if nStar ~= 0 and tonumber(nType) == nStar then return true, {} end\n"
		+ "return prev(nType, ...)\n"
		+ "end\n"
		+ "R.IsOpenById = _G.fnMikuSwGate\n"
		+ "end\n"
		+ "local function mikuPzEnter()\n"
		+ "local J = _G.JigsawPuzzle\n"
		+ "if type(J) ~= [[table]] or type(J.EnterByActivityId) ~= [[function]] then return end\n"
		+ "if J.EnterByActivityId == _G.fnMikuPzEnter then return end\n"
		+ "local prev = J.EnterByActivityId\n"
		+ "_G.fnMikuPzEnter = function(actId)\n"
		+ "local A = _G.Activity\n"
		+ "local set = _G.tbMikuPuzzleForce\n"
		+ "local id = tonumber(actId) or 0\n"
		+ "local cfg = nil\n"
		+ "if type(A) == [[table]] and type(A.GetActivityConfig) == [[function]] then cfg = A.GetActivityConfig(actId) end\n"
		+ "if type(set) == [[table]] and set[id] and type(cfg) == [[table]] then\n"
		+ "local cd1 = cfg.tbCustomData1\n"
		+ "local sch = 0\n"
		+ "if type(cd1) == [[table]] then sch = tonumber(cd1[1]) or 0 end\n"
		+ "if sch ~= 0 then\n"
		+ "UI.Open([[JigsawPuzzlePlay]], actId, sch)\n"
		+ "return\n"
		+ "end\n"
		+ "end\n"
		+ "return prev(actId)\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuPzRedraw()\n"
		+ "if type(UI) ~= [[table]] or type(UI.tbWidget) ~= [[table]] then return end\n"
		+ "for _, w in pairs(UI.tbWidget) do\n"
		+ "if type(w) == [[table]] and type(w.sName) == [[string]] then\n"
		+ "local sn = string.lower(w.sName)\n"
		+ "if string.sub(sn, 1, 3) == [[dlc]] and string.find(sn, [[main]], 1, true) and type(w.UpdateBtnDynamicInfo) == [[function]] then\n"
		+ "pcall(function() w:UpdateBtnDynamicInfo() end)\n"
		+ "elseif sn == [[farmwarmain]] then\n"
		+ "if type(w.UpdateTime) == [[function]] then pcall(function() w:UpdateTime() end) end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuPzFix()\n"
		+ "local t = { mikuPzSet, mikuPzHook, mikuPzCard, mikuFwFix, mikuSwGate, mikuPzEnter, mikuPzRedraw }\n"
		+ "for i = 1, 7 do pcall(t[i]) end\n"
		+ "end\n"
		+ "pcall(mikuPzFix)\n"
		+ "local A2 = _G.Activity\n"
		+ "if type(A2) == [[table]] and type(A2.LoadActivitiesConfig) == [[function]] and A2.bMikuPzLoad ~= true then\n"
		+ "A2.bMikuPzLoad = true\n"
		+ "local pzPrevL = A2.LoadActivitiesConfig\n"
		+ "A2.LoadActivitiesConfig = function(...) local r = pzPrevL(...) pcall(mikuPzFix) return r end\n"
		+ "end\n"
		+ "if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuPzOpenHook ~= true then\n"
		+ "_G.bMikuPzOpenHook = true\n"
		+ "local pzPrevO = UI.Open\n"
		+ "UI.Open = function(...) pcall(mikuPzFix) local r = table.pack(pzPrevO(...)) pcall(mikuPzFix) return table.unpack(r, 1, r.n) end\n"
		+ "end\n";

	/// <summary>
	/// 2026-11-18（第 425 节）：登录期 s2c「No command named」噪声清理。
	/// 客户端 S2C.lua:29-36 的 Dispatch：
	///   local fn = s2c.tbCmds[name]
	///   if not fn then error([[s2c.Dispatch error. No command named: ]] .. tostring(name)) end
	///   fn(json.decode(data))
	/// 反汇编确认：Dispatch 里取的是 GETFIELD s2c.tbCmds + GETTABLE [name]，即每次调用都
	/// 实时从 s2c 表上取当前 tbCmds。所以「包 Dispatch」没用（C++ 缓存了原闭包），
	/// 但「给 tbCmds 挂 __index」有用 —— 任何未注册名字（含空串、不可见字符）都会拿到
	/// 一个空函数，不再抛错。名字记进 _G.tbMikuS2cNoise 供探针查。
	/// 配套把 s2c.Register 换成 rawset 直写：否则 __index 会让原 Register 的「重复注册」
	/// 检查永远命中，所有 Lua 命令注册全部报错。
	/// </summary>
	public const string S2CSilentNoise = "local function mikuS2cNoiseRec(k)\n"
		+ "if type(_G.tbMikuS2cNoise) ~= [[table]] then _G.tbMikuS2cNoise = {} end\n"
		+ "local s = tostring(k)\n"
		+ "_G.tbMikuS2cNoise[s] = (_G.tbMikuS2cNoise[s] or 0) - (-1)\n"
		+ "end\n"
		+ "local function mikuS2cMount(tb)\n"
		+ "if type(tb) ~= [[table]] then return end\n"
		+ "local mt = getmetatable(tb)\n"
		+ "if type(mt) ~= [[table]] then\n"
		+ "mt = {}\n"
		+ "setmetatable(tb, mt)\n"
		+ "end\n"
		+ "if _G.fnMikuS2cIndex ~= nil and mt.__index == _G.fnMikuS2cIndex then return end\n"
		+ "local fn = function(t, k)\n"
		+ "local noop = function() end\n"
		+ "rawset(t, k, noop)\n"
		+ "pcall(mikuS2cNoiseRec, k)\n"
		+ "return noop\n"
		+ "end\n"
		+ "mt.__index = fn\n"
		+ "_G.fnMikuS2cIndex = fn\n"
		+ "end\n"
		+ "local function mikuS2cSilent()\n"
		+ "if type(s2c) ~= [[table]] then return end\n"
		+ "mikuS2cMount(s2c.tbCmds)\n"
		+ "if type(s2c.Register) ~= [[function]] then return end\n"
		+ "if s2c.Register == _G.fnMikuS2cReg then return end\n"
		+ "local prevReg = s2c.Register\n"
		+ "local fnReg = function(cmd, fn)\n"
		+ "if type(cmd) == [[string]] and type(fn) == [[function]] then\n"
		+ "mikuS2cMount(s2c.tbCmds)\n"
		+ "rawset(s2c.tbCmds, cmd, fn)\n"
		+ "return\n"
		+ "end\n"
		+ "return prevReg(cmd, fn)\n"
		+ "end\n"
		+ "_G.fnMikuS2cReg = fnReg\n"
		+ "s2c.Register = fnReg\n"
		+ "end\n"
		+ "mikuS2cSilent()\n";

	/// <summary>
	/// 2026-11-18（第 426 节）：进游戏自动弹「八日签到」(shortsign) 与
	/// 「每日签到」(signday) 两个窗口，屏蔽掉。
	/// 这两个弹窗都由 Activity.Sign.CheckOpen 判定后弹出，它内部就是
	/// return CheckShortPop() or CheckSignPop()。把这两个判定包成恒 false 即可，
	/// 只关自动弹，手动点签到入口（UI.Open）不受影响。
	/// 注意：不能在里面调原函数——原 CheckShortPop/CheckSignPop 内部会
	/// EventSystem.Trigger(Event.ShortSign/SignDay)，事件处理器最终自己 UI.Open，
	/// 那才是真正的弹窗来源（实测保留副作用仍会弹）。
	/// </summary>
	public const string SignShortPopOff = "local function mikuSignWrap(name)\n"
		+ "local S = _G.Sign\n"
		+ "if type(S) ~= [[table]] then return end\n"
		+ "if type(S[name]) ~= [[function]] then return end\n"
		+ "local key = [[fnMikuNoPop_]] .. name\n"
		+ "if _G[key] ~= nil and S[name] == _G[key] then return end\n"
		+ "local fn = function(...)\n"
		+ "return false\n"
		+ "end\n"
		+ "_G[key] = fn\n"
		+ "S[name] = fn\n"
		+ "end\n"
		+ "mikuSignWrap([[CheckShortPop]])\n"
		+ "mikuSignWrap([[CheckSignPop]])\n";

	/// <summary>
	/// 2026-11-18（第 427 / 428 / 429 / 430 节）：把几张往期活动挂回各自的期数，别期一律不显示。
	///   · 活动 33 / 34（组 33，藏锋 / 芙提雅-缄默）      → 只在第 2 期；
	///   · 活动 100 / 101 / 102（组 100，琴诺-悖谬 / 羽蜕 / 苔丝）→ 只在第 7 期；
	///   · 活动 390 / 602（组 390，适格启忆录两条）       → 只在第 33、36 期。
	///   · 活动 283（组 283，妮塔之邀）                   → 只在第 27 期；
	///   · 活动 69（组 69，斑斓漫想 / 七日签到）           → 哪期都不开（空列表 = 全期退役）。
	///   · 活动 71（组 71，感知巡游 energy_exchange）       → 只在第 27 期（Excel 窗口停在 2024-01）。
	/// 第 430 节更正：第 429 节把 33/34 与 100 的期数写反了（当时按「组号=角色」猜的）。
	/// 实测名字取自 sTitleDes 的 Text()：33=藏锋、34=缄默、100=悖谬、101=羽蜕、102=苔丝，
	/// sGroupName 都是「队员试用」；第 2 期 dlc 配置的 TryIds 正好就是 33 / 34，
	/// 与「芙提雅-缄默属第 2 期」吻合（104~107 是「新品体验」，不在此表）。
	/// 为什么必须按 id 拦：gacha_try 的 IsOpen 不看时间窗，直接问
	/// GachaTry.IsGachaTryAvalible，后者只认「配置里有没有这条」，nEndTime 过期也 true；
	/// jigsaw_puzzle 那两条则是被 DlcPuzzleOpen（第 54 条）的 mikuPzAllow 强制开的。
	/// 拦法（四层，任一层失效另外三层还在）：
	///   1. Activity.IsOpen / __FBW / __FBWN：不在本期 -> false；
	///   2. GachaTry.IsGachaTryAvalible：不在本期 -> false；
	///   3. Activity.GetCaseByGroup / GetSortAllGroup：整组返空 / 从列表丢弃；
	///   4. 把 _G.MikuTryDead 暴露出去，让 DlcPuzzleOpen 的 mikuPzAllow 也先问一句，
	///      否则它包在 __FBW 外层会直接把 390/602 放行。
	/// 期数实时取 DLC_Logic.GetCurConf().nId（切期后不用重装也生效），取不到回退
	/// MIKU_ACT_ERA；默认模式（0）视为全部退役。
	/// 注意补丁文本禁用 + &lt; &gt; &amp; 单双引号，比较一律用 ~= / math.max 绕。
	/// </summary>
	public const string GachaTryOldHide = "local function mikuTryNow()\n"
		+ "local t = 0\n"
		+ "pcall(function() t = GetTime() end)\n"
		+ "if type(t) ~= [[number]] then t = 0 end\n"
		+ "return t\n"
		+ "end\n"
		+ "local tbMikuEraGate = {}\n"
		+ "tbMikuEraGate[33] = { 2 }\n"
		+ "tbMikuEraGate[34] = { 2 }\n"
		+ "tbMikuEraGate[100] = { 7 }\n"
		+ "tbMikuEraGate[101] = { 7 }\n"
		+ "tbMikuEraGate[102] = { 7 }\n"
		+ "tbMikuEraGate[390] = { 33, 36 }\n"
		+ "tbMikuEraGate[602] = { 33, 36 }\n"
		+ "tbMikuEraGate[283] = { 27 }\n"
		+ "tbMikuEraGate[69] = {}\n"
		+ "tbMikuEraGate[71] = { 27 }\n"
		+ "tbMikuEraGate[80] = {}\n"
		+ "tbMikuEraGate[88] = {}\n"
		+ "tbMikuEraGate[108] = {}\n"
		+ "tbMikuEraGate[112] = {}\n"
		+ "tbMikuEraGate[120] = {}\n"
		+ "tbMikuEraGate[148] = {}\n"
		+ "tbMikuEraGate[150] = {}\n"
		+ "tbMikuEraGate[164] = {}\n"
		+ "tbMikuEraGate[171] = {}\n"
		+ "tbMikuEraGate[172] = {}\n"
		+ "tbMikuEraGate[38] = {}\n"
		+ "local function mikuTryEra()\n"
		+ "local e = tonumber(_G.MikuDmEra)\n"
		+ "if e ~= nil then return e end\n"
		+ "return 0\n"
		+ "end\n"
		+ "local function mikuTryDead(nId)\n"
		+ "local id = tonumber(nId)\n"
		+ "if id == nil then return false end\n"
		+ "local list = tbMikuEraGate[id]\n"
		+ "if list == nil then return false end\n"
		+ "local era = mikuTryEra()\n"
		+ "for i = 1, #list do\n"
		+ "if list[i] == era then return false end\n"
		+ "end\n"
		+ "return true\n"
		+ "end\n"
		+ "_G.MikuTryDead = mikuTryDead\n"
		+ "local function mikuTryAllowed(nId)\n"
		+ "local id = tonumber(nId)\n"
		+ "if id == nil then return false end\n"
		+ "if tbMikuEraGate[id] == nil then return false end\n"
		+ "return not mikuTryDead(id)\n"
		+ "end\n"
		+ "local function mikuTryFixRow(row, now)\n"
		+ "if type(row) ~= [[table]] then return end\n"
		+ "row.nStartTime = now - 3600\n"
		+ "row.nEndTime = now - (-2592000)\n"
		+ "if (tonumber(row.nCloseFlag) or 0) ~= 0 then row.nCloseFlag = 0 end\n"
		+ "row.tbCondition = nil\n"
		+ "end\n"
		+ "local function mikuTryFixCfg()\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] then return end\n"
		+ "local now = mikuTryNow()\n"
		+ "if now == 0 then return end\n"
		+ "for id, _ in pairs(tbMikuEraGate) do\n"
		+ "if not mikuTryDead(id) then\n"
		+ "local tb = A.tbConfig\n"
		+ "if type(tb) == [[table]] and type(tb[id]) == [[table]] then mikuTryFixRow(tb[id], now) end\n"
		+ "local mg = A.tbModelGroup\n"
		+ "if type(mg) == [[table]] and type(mg[id]) == [[table]] then\n"
		+ "for _, row in pairs(mg[id]) do\n"
		+ "if type(row) == [[table]] and tonumber(row.nId) == id then mikuTryFixRow(row, now) end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuTryFilterCases(A, gid, rows)\n"
		+ "local out = {}\n"
		+ "local gateGroup = gid ~= nil and tbMikuEraGate[gid] ~= nil\n"
		+ "for i = 1, #rows do\n"
		+ "local row = rows[i]\n"
		+ "local rid = nil\n"
		+ "if type(row) == [[table]] then rid = tonumber(row.nId) end\n"
		+ "if rid == nil and type(row) == [[table]] then rid = tonumber(row.id) end\n"
		+ "local drop = false\n"
		+ "if rid ~= nil and mikuTryDead(rid) then drop = true end\n"
		+ "if gateGroup and (rid == nil or tbMikuEraGate[rid] == nil) then drop = true end\n"
		+ "if not drop then out[#out - (-1)] = row end\n"
		+ "end\n"
		+ "if #out ~= 0 then return out end\n"
		+ "if gid ~= nil and tbMikuEraGate[gid] ~= nil then\n"
		+ "local mg = A.tbModelGroup\n"
		+ "if type(mg) == [[table]] and type(mg[gid]) == [[table]] then\n"
		+ "for _, row in pairs(mg[gid]) do\n"
		+ "local rid = tonumber(type(row) == [[table]] and row.nId)\n"
		+ "if rid ~= nil and mikuTryAllowed(rid) then out[#out - (-1)] = row end\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return out\n"
		+ "end\n"
		+ "local function mikuTryHide()\n"
		+ "pcall(mikuTryFixCfg)\n"
		+ "local A = _G.Activity\n"
		+ "if type(A) ~= [[table]] then return end\n"
		+ "if type(A.IsOpen) == [[function]] and (_G.fnMikuTryIsOpen == nil or A.IsOpen ~= _G.fnMikuTryIsOpen) then\n"
		+ "local prevIsOpen = A.IsOpen\n"
		+ "local fnOpen = function(nId, ...)\n"
		+ "if mikuTryAllowed(nId) then return true end\n"
		+ "if mikuTryDead(nId) then return false end\n"
		+ "return prevIsOpen(nId, ...)\n"
		+ "end\n"
		+ "_G.fnMikuTryIsOpen = fnOpen\n"
		+ "A.IsOpen = fnOpen\n"
		+ "end\n"
		+ "local FW = rawget(A, [[__FBW]])\n"
		+ "if type(FW) == [[function]] and (_G.fnMikuTryFbw == nil or FW ~= _G.fnMikuTryFbw) then\n"
		+ "local prevFbw = FW\n"
		+ "local fnFbw = function(nId, ...)\n"
		+ "if mikuTryAllowed(nId) then return true end\n"
		+ "if mikuTryDead(nId) then return false end\n"
		+ "return prevFbw(nId, ...)\n"
		+ "end\n"
		+ "_G.fnMikuTryFbw = fnFbw\n"
		+ "rawset(A, [[__FBW]], fnFbw)\n"
		+ "if type(rawget(A, [[__FBWN]])) == [[function]] then rawset(A, [[__FBWN]], fnFbw) end\n"
		+ "end\n"
		+ "if type(A.GetCaseByGroup) == [[function]] and (_G.fnMikuTryCase == nil or A.GetCaseByGroup ~= _G.fnMikuTryCase) then\n"
		+ "local prevCase = A.GetCaseByGroup\n"
		+ "local fnCase = function(nId, ...)\n"
		+ "local gid = tonumber(nId)\n"
		+ "if gid ~= nil and tbMikuEraGate[gid] ~= nil and mikuTryDead(gid) then return {} end\n"
		+ "local r = prevCase(nId, ...)\n"
		+ "if type(r) ~= [[table]] then return r end\n"
		+ "return mikuTryFilterCases(A, gid, r)\n"
		+ "end\n"
		+ "_G.fnMikuTryCase = fnCase\n"
		+ "A.GetCaseByGroup = fnCase\n"
		+ "end\n"
		+ "if type(A.GetSortAllGroup) == [[function]] and (_G.fnMikuTrySort == nil or A.GetSortAllGroup ~= _G.fnMikuTrySort) then\n"
		+ "local prevSort = A.GetSortAllGroup\n"
		+ "local fnSort = function(...)\n"
		+ "pcall(mikuTryHide)\n"
		+ "local r = prevSort(...)\n"
		+ "if type(r) ~= [[table]] then return r end\n"
		+ "local now = mikuTryNow()\n"
		+ "local out = {}\n"
		+ "local have = {}\n"
		+ "for i = 1, #r do\n"
		+ "local e = r[i]\n"
		+ "local id = e\n"
		+ "if type(e) == [[table]] then id = e[1] end\n"
		+ "local gid = tonumber(id)\n"
		+ "local keep = true\n"
		+ "if gid ~= nil and tbMikuEraGate[gid] ~= nil then\n"
		+ "if mikuTryDead(gid) then keep = false end\n"
		+ "else\n"
		+ "local cfg = A.GetActivityConfig(id)\n"
		+ "if type(cfg) == [[table]] and cfg.sClass == [[gacha_try]] and now ~= 0 then\n"
		+ "if math.max(now - (cfg.nEndTime or 0), 0) ~= 0 then keep = false end\n"
		+ "end\n"
		+ "end\n"
		+ "if keep then\n"
		+ "out[#out - (-1)] = e\n"
		+ "if gid ~= nil then have[gid] = true end\n"
		+ "end\n"
		+ "end\n"
		+ "for id, _ in pairs(tbMikuEraGate) do\n"
		+ "if mikuTryAllowed(id) then\n"
		+ "local cfg = A.GetActivityConfig(id)\n"
		+ "local gid = id\n"
		+ "local idx = 0\n"
		+ "if type(cfg) == [[table]] then\n"
		+ "gid = tonumber(cfg.nGroupId) or id\n"
		+ "idx = tonumber(cfg.nIndex) or 0\n"
		+ "end\n"
		+ "if not have[gid] then\n"
		+ "have[gid] = true\n"
		+ "out[#out - (-1)] = { gid, idx }\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "return out\n"
		+ "end\n"
		+ "_G.fnMikuTrySort = fnSort\n"
		+ "A.GetSortAllGroup = fnSort\n"
		+ "end\n"
		+ "end\n"
		+ "local function mikuTryHideGT()\n"
		+ "local G = _G.GachaTry\n"
		+ "if type(G) ~= [[table]] or type(G.IsGachaTryAvalible) ~= [[function]] then return end\n"
		+ "if _G.fnMikuTryAval ~= nil and G.IsGachaTryAvalible == _G.fnMikuTryAval then return end\n"
		+ "local prevAval = G.IsGachaTryAvalible\n"
		+ "local fnAval = function(nId, ...)\n"
		+ "if mikuTryAllowed(nId) then return true end\n"
		+ "if mikuTryDead(nId) then return false end\n"
		+ "return prevAval(nId, ...)\n"
		+ "end\n"
		+ "_G.fnMikuTryAval = fnAval\n"
		+ "G.IsGachaTryAvalible = fnAval\n"
		+ "end\n"
		+ "mikuTryHide()\n"
		+ "mikuTryHideGT()\n"
		+ "local function mikuTryHookOpen()\n"
		+ "if type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] then return end\n"
		+ "if _G.fnMikuTryUiOpen ~= nil and UI.Open == _G.fnMikuTryUiOpen then return end\n"
		+ "local prevUiOpen = UI.Open\n"
		+ "local fnUi = function(...)\n"
		+ "pcall(mikuTryHide)\n"
		+ "pcall(mikuTryHideGT)\n"
		+ "local r = table.pack(prevUiOpen(...))\n"
		+ "pcall(mikuTryHide)\n"
		+ "return table.unpack(r, 1, r.n)\n"
		+ "end\n"
		+ "_G.fnMikuTryUiOpen = fnUi\n"
		+ "UI.Open = fnUi\n"
		+ "end\n"
		+ "mikuTryHookOpen()\n";

	/// <summary>
	/// 2026-11-19（第 434 节）：主界面「生日快乐」入口往后续命 100 年（2027 ~ 2126）。
	///
	/// 现象：2027 年生日当天按钮不出现。
	/// 真因：入口开关是客户端 BirthdayMail.CheckShow() = InBirthday() 命中当天年份，
	///   或 CheckNew()（在 period 窗口内且 attr 155/年份 未置 1）命中；但两个分支最后
	///   都要过一道 conf[年份] 是否存在（InBirthdayPeriod 更是开头就 conf[当前年] 判空）。
	///   配置来自 riki/wishmail.json，3.2.0 客户端只带 2025 / 2026 两年，
	///   conf[2027] 为 nil → 连「生日当天必显示」那条也直接返回 0（实测
	///   InBirthday=2027 命中但 Show=0）。
	///
	/// 修法：补丁运行期给 tbWishMailConf 补 2027 ~ 2126 共 100 行，字段照抄 2026 行
	///   （图标 110100013、PeriodTime 90、文案复用 ui.TxtBirthdayWishes2026*），
	///   只把 Id / nStartTime / nEndTime 按年份改。同时包一层 Init，若客户端重新
	///   载表（Init 里会把 tbWishMailConf 重置为 {} 再读 csv）就再补一次。
	///
	/// 注意：补丁文本禁用 + &lt; &gt; &amp; 单双引号，计数一律写成 n - (-1)；
	///   也不要出现 MIKU_ACT_ERA / MIKU_ACTIVATION_FLOOR 这类占位符字面量。
	/// 命中年份与最后一次补全数量写 D:/DSH/.tools/wishyears.log。
	/// </summary>
	public const string BirthdayWishYears = @"local LOG = [[D:/DSH/.tools/wishyears.log]]
local function wy(s)
local ok, f = pcall(io.open, LOG, [[a]])
if ok and f then
f:write(tostring(s))
f:write(string.char(10))
f:close()
end
end
local B = _G.BirthdayMail
if type(B) ~= [[table]] then
local ld = package.loaded
if type(ld) == [[table]] then
local t = ld[[Activity.BirthdayMail]]
if type(t) == [[table]] then B = t end
end
end
if type(B) ~= [[table]] then
wy([[no BirthdayMail module]])
return
end
local function wyFill()
local conf = rawget(B, [[tbWishMailConf]])
if type(conf) ~= [[table]] then return 0 end
local base = rawget(conf, 2026)
if type(base) ~= [[table]] then return 0 end
local n = 0
for y = 2027, 2126 do
if rawget(conf, y) == nil then
local row = {}
for k, v in pairs(base) do row[k] = v end
row.Id = y
row.nStartTime = os.time({ year = y, month = 1, day = 1, hour = 0, min = 0, sec = 0 })
row.nEndTime = os.time({ year = y - (-1), month = 1, day = 1, hour = 0, min = 0, sec = 0 })
row.nPeriodTime = 90
row.nIcon = 110100013
rawset(conf, y, row)
n = n - (-1)
end
end
return n
end
if rawget(B, [[bMikuWishYears]]) ~= true then
rawset(B, [[bMikuWishYears]], true)
local oldInit = rawget(B, [[Init]])
if type(oldInit) == [[function]] then
local fnInit = function(...)
local r1, r2, r3 = oldInit(...)
pcall(wyFill)
return r1, r2, r3
end
rawset(B, [[Init]], fnInit)
end
end
local okf, nf = pcall(wyFill)
wy([[fill ok=]] .. tostring(okf) .. [[ n=]] .. tostring(nf))
local c2 = rawget(B, [[tbWishMailConf]])
local r27 = rawget(c2, 2027)
local r126 = rawget(c2, 2126)
wy([[2027=]] .. tostring(r27 and r27.Id) .. [[ 2126=]] .. tostring(r126 and r126.Id))
";

	/// <summary>
	/// RmolPvpLoadingFix（第 454 节）：第 32 期绘梦沙盘「模拟对抗」首次进入卡在加载遮罩。
	///
	/// 现象：点模拟对抗进入房间，整屏蒙一层半透明遮罩、正中一个白色转圈，画面发虚；
	///       点右上返回、再进一次就完全正常。
	///
	/// 实测（第 453 节「资源缺失」的结论作废）：
	///   · 卡住时 UI.bOpenConnection 恒为 true、connection 控件可见，
	///     手动 UI.CloseConnection() 立刻恢复（遮罩与模糊一起消失），说明转圈就是连接遮罩，
	///     不是场景流送；房间场景本身早就在正常渲染。
	///   · 进房时 rmol 房间脚本会调两次 ShowConnection（OpenPVP 与 ExitRoom 各一次），
	///     之后标记没被清回去，遮罩就永久挂着。
	///   · 另外房间脚本还会叠开一层标准 formation 备战界面，它要等角色预览动画的加载完成通知，
	///     而该角色（girl003a_02）的预览待机动画在本客户端资源里缺失，那层也停在空白加载态，
	///     正好盖住下面的房间。
	///
	/// 修法：只在调用方属于 rmol 房间脚本时生效，其他界面完全不受影响（按调用栈里的文件名判断）：
	///   ShowConnection 直接跳过、不再起遮罩，房间本身照常渲染。
	///   （曾试过同时把标准 formation 备战层关掉，实测在登录恢复阶段会触发客户端崩溃，已放弃；
	///     真正卡住时其实只有 rmolformation 与 connection 两个控件，formation 不在链上。）
	/// 注意：本段注释里不要出现带点的小写脚本文件名，否则补丁会把自己认成调用方。
	/// </summary>
	public const string RmolPvpLoadingFix = @"-- RmolPvpLoadingFix（第 454 节）：模拟对抗首次进入的加载遮罩。
-- 转圈来自 UI 的 connection 遮罩，不是场景流送；进房时 rmol 房间脚本调了
-- ShowConnection 却没清掉标记，遮罩就一直挂着。
-- 房间脚本还会叠开一层标准 formation 备战界面，等的是角色预览动画的加载完成通知，
-- 而那个角色的预览待机动画在本客户端里缺失，那层永远空白。这里一并处理。
local LOG = [[D:/DSH/.tools/rmolpvp.log]]
local function mpvLog(s)
local ok, f = pcall(io.open, LOG, [[a]])
if ok and f then
f:write(os.date([[%H:%M:%S]]))
f:write([[ ]])
f:write(tostring(s))
f:write(string.char(10))
f:close()
end
end
local function mpvFrom(needle)
if type(debug) ~= [[table]] then return false end
for lv = 3, 12 do
local ok, info = pcall(debug.getinfo, lv, [[S]])
if not ok or not info then break end
local s = tostring(info.short_src or [[ ]])
if string.find(s, needle, 1, true) then return true end
end
return false
end
if type(UI) == [[table]] then
local U = UI
if type(U.ShowConnection) == [[function]] and _G.bMikuMpvConn ~= true then
_G.bMikuMpvConn = true
local mpvShow = U.ShowConnection
U.ShowConnection = function(...)
if mpvFrom([[rmol]]) then
mpvLog([[skip connection overlay]])
pcall(U.CloseConnection)
return
end
return mpvShow(...)
end
mpvLog([[connection hook installed]])
end
end";

	/// <summary>
	/// DonoDuelStarterFightBag（第 463 节）：首次进探索多诺卡，局内编队界面「暂无多诺卡」。
	///
	/// 客户端链路：DonoDuelRogue:Req_EnterLevel 先本地跑 InitFightBag()，
	/// 再发 CALL 给服务端。InitFightBag 只读 DonoDuel:GetFormation() →
	/// GetRogueFormation() → 属性 185/10~13。这三格为空时局内背包 tbFightBag 就是空的，
	/// 而 umg_donoduel_formation 在 ADonoRogueGameMode 里 bOpenBag 恒 true，
	/// 列表取 GetFightBagItem()（即 tbFightBag），于是左侧 5 个槽全空、中间「暂无多诺卡」，
	/// 引导 64107 第一步就要求点卡上阵，无卡可点。
	///
	/// 服务端第 463 节已补「自动填肉鸽编队 + 发任务 101 的 3 张初始卡」，
	/// 但那是登录/接任务时落库的，进关卡这一次如果属性还没同步过来仍会空。
	/// 这里在 InitFightBag 之后兜一层：局内背包为空就用局外背包的前 5 张填上。
	/// </summary>
	public const string DonoDuelStarterFightBag = @"-- DonoDuelStarterFightBag（第 463 节）：局内背包为空时用局外背包兜底
local function ddsf()
if type(DonoDuelRogue) ~= [[table]] then return end
if type(DonoDuelRogue.InitFightBag) ~= [[function]] then return end
if DonoDuelRogue.bMikuInitBagFix == true then return end
DonoDuelRogue.bMikuInitBagFix = true
local old = DonoDuelRogue.InitFightBag
DonoDuelRogue.InitFightBag = function(self, ...)
local r = old(self, ...)
local bag = DonoDuelRogue.tbFightBag
if type(bag) ~= [[table]] then return r end
local n = 0
for _ in pairs(bag) do n = n - (-1) end
if n ~= 0 then return r end
local cards = nil
pcall(function() cards = DonoDuel.GetBagAllCard(DonoDuel) end)
if type(cards) ~= [[table]] then return r end
local limit = 5
local added = 0
for _, card in ipairs(cards) do
if added == limit then break end
local uid = card and card.nUniqueId
if uid then
local ok = pcall(DonoDuelRogue.AddFightBagItem, DonoDuelRogue, uid)
if ok then added = added - (-1) end
end
end
local f = io.open([[D:/DSH/.tools/dd_starterbag.log]], [[a]])
if f then f:write([[auto fill ]] .. tostring(added) .. [[ card(s)]] .. string.char(10)) f:close() end
return r
end
end
ddsf()
if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDdBagHook ~= true then
_G.bMikuDdBagHook = true
local o = UI.Open
UI.Open = function(...) pcall(ddsf) return o(...) end
end";

	/// <summary>
	/// DonoDuelGuide64107（第 466/467 节）：第一次打「探索多诺卡」，卡牌星级提升教程弹得太早。
	///
	/// 现象：进编队/关卡时 64107（编队 + 培养升星 + 技能研习 共 12 步）就开播，
	/// 玩家还没拿到奖励；正确顺序是「第一波打完 → 获得奖励（3 张多诺卡 + 100 毛球币）
	/// 弹窗 → 关掉之后」再教星级提升。
	///
	/// 根因：donorogue_level.json 里 LevelID 5501（Stage 1 / RoomID 1 / CatchID 2）配了
	///   Guide_Point = 64107；而 guide 表里 64107 的 CheckEx 是空的，只有 PreGuide=64106，
	///   64106 是战斗界面（sWindow=fight）的单步引导，点掉就立刻满足 ——
	///   于是只要 64106 播过，64107 随时可播。
	///
	/// 第 466 节曾用「186/103 进度非 0」当条件，但 103 是关卡结算时按 Stage/RoomID 推进的，
	/// 失败的局也会推进（467 已修），而且它跟「首通奖励到账」不是同一时刻，
	/// 结果玩家重进关卡时教程抢在奖励前面弹。
	///
	/// 第 467 节改为直接盯首通奖励标记：
	///   服务端 DonoDuelRogueSettlement.SettleRoom 发完就写 185/(600+CatchID)；
	///   5501 的 CatchID 是 2，所以条件就是 185/602 非 0 —— 也就是「3 张卡 + 100 毛球币已进包」。
	///   这一位只有真正发过奖才为 1，重置存档时会一起清掉，天然跟着新号流程走。
	///
	/// 第 467 节追加（真正的抢跑源头）：反汇编 GuideLogic.OpenAppointGuide 发现它**不做任何
	/// 条件检查** —— 只要求 tbStep[1].sWindow 已经打开，就直接 BeginGuide。关卡按
	/// donorogue_level 的 Guide_Point 调的就是它，所以光改 tbCheckEx 挡不住「一进关卡就播」。
	/// 现在同时包一层 OpenAppointGuide：64107 在 185/602 为 0 时直接吞掉这次指定引导；
	/// 并在 DonoduelDrop（首通奖励弹窗）关掉时补一次 OpenAppointGuide(64107)，
	/// 保证「确定 → 关掉奖励框 → 教程」这个顺序，且不会漏掉教程。
	///
	/// 客户端 guide 配置来自 pak（LoadCsv 读 guide/guide.txt），改服务端
	/// Resources/guide/guide.json 不生效，所以这里在运行时直接改 tbConfig，
	/// 并包一层 LoadConf，防止后续重载把改动冲掉。
	/// </summary>
	public const string DonoDuelGuide64107 = @"-- DonoDuelGuide64107（第 466/467 节）：让星级提升教程等到首通奖励到账之后
local function ddgfirst()
local v = 0
pcall(function() v = me:GetAttribute(185, 602) end)
return v ~= 0
end
local function ddgcheck()
if type(GuideLogic) ~= [[table]] then return end
local fun = GuideLogic.tbCheckFun
if type(fun) ~= [[table]] then return end
fun.MikuDonoFirstReward = ddgfirst
end
local function ddgcfg()
if type(GuideLogic) ~= [[table]] then return end
local tb = GuideLogic.tbConfig
if type(tb) ~= [[table]] then return end
local c = tb[64107]
if type(c) ~= [[table]] then return end
if c.bMiku64107Fix == true then return end
c.bMiku64107Fix = true
c.tbCheckEx = { { [[MikuDonoFirstReward]], {} } }
end
local function ddgopen()
if type(GuideLogic) ~= [[table]] or type(GuideLogic.OpenAppointGuide) ~= [[function]] then return end
if GuideLogic.bMikuAppointGate == true then return end
GuideLogic.bMikuAppointGate = true
local old = GuideLogic.OpenAppointGuide
GuideLogic.OpenAppointGuide = function(id, ...)
if tonumber(id) == 64107 and not ddgfirst() then return end
return old(id, ...)
end
end
local function ddgdrop()
if type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] then return end
if UI.bMikuDropHook == true then return end
UI.bMikuDropHook = true
local oldOpen = UI.Open
UI.Open = function(a, ...)
local r = table.pack(oldOpen(a, ...))
pcall(function()
local n = tostring(type(a) == [[table]] and a.sName or a)
if n == [[DonoduelDrop]] then
local w = UI.GetUI and UI.GetUI([[DonoduelDrop]])
if type(w) == [[table]] and type(w.OnClose) == [[function]] and w.bMikuDropClose ~= true then
w.bMikuDropClose = true
local oc = w.OnClose
w.OnClose = function(self, ...)
local rr = oc(self, ...)
pcall(function()
if ddgfirst() and GuideLogic.CanOpenGuide(64107) then GuideLogic.OpenAppointGuide(64107) end
end)
return rr
end
end
end
end)
return table.unpack(r, 1, r.n)
end
end
local function ddgall()
ddgcheck()
ddgcfg()
ddgopen()
ddgdrop()
end
ddgall()
if type(GuideLogic) == [[table]] and type(GuideLogic.LoadConf) == [[function]] and _G.bMiku64107Hook ~= true then
_G.bMiku64107Hook = true
local oldLoad = GuideLogic.LoadConf
GuideLogic.LoadConf = function(...)
local r = oldLoad(...)
pcall(ddgall)
return r
end
end";

	/// <summary>
	/// DonoDuelAcceptStoryRetry（第 470 节）：任务接取时如果正开着培养/图鉴等子界面，
	/// 这条任务的「接取剧情」会被静默吞掉，之后再也不播。
	///
	/// 反汇编 DonoDuelTask_Accept 的回包处理：拿到 nId 后立刻
	/// <c>DonoDuelTask.PlayAcceptStory(mission)</c>；
	/// 而 PlayAcceptStory 第一件事就是 <c>DonoDuel.CheckMainUI(true)</c> ——
	/// 它要求 DonoDuelMainRoom 是当前最上层窗口，否则直接 return，不播也不排队。
	///
	/// 第 470 节服务端改成「潜能提升 / 队长技能成功时推进剧情任务」之后，
	/// 104 完成的时机正是玩家在培养界面里点潜能提升的那一刻：
	/// 104 领奖 → 客户端自动接取 105 → 要播 phase1_04，可培养界面还在最上层 → 剧情丢失，
	/// 玩家看到的就是「教程/培养做完了，剧情还是没接上」。
	///
	/// 修法：包一层 PlayAcceptStory，发现「有 sAcceptStory 但当前不在主房间最上层」时不丢，
	/// 先存起来；等 UI.Close / UI.Open 之后再试一次，条件满足就补播。
	/// 不改变正常路径：在主房间内接取时依旧原样立即播放。
	///
	/// 第 475 节修正：调用方是冒号语法 <c>DonoDuelTask:PlayAcceptStory(cfg)</c>，
	/// 原函数签名是 (self, tbMission) 两个参数（反汇编 params=2）。
	/// 第 470 节这层包装只写了一个参数，把 self 当成 tbMission 收下、
	/// 又把 self 原样传回原函数，真正的任务配置被丢掉 ——
	/// 于是每次接取都走 sAcceptStory=nil 分支，只弹「新增可获取的多诺卡」，剧情永远不播。
	/// 现在包装改为 (self, tbMission) 并原样转发。
	/// </summary>
	public const string DonoDuelAcceptStoryRetry = @"-- DonoDuelAcceptStoryRetry（第 470 节）：接取剧情被培养界面挡住时排队补播
local function ddsr()
if type(DonoDuelTask) ~= [[table]] or type(DonoDuel) ~= [[table]] then return end
if type(DonoDuel.CheckMainUI) ~= [[function]] then return end
if type(DonoDuelTask.PlayAcceptStory) ~= [[function]] then return end
if _G.bMikuDdStoryRetry == true then return end
_G.bMikuDdStoryRetry = true
_G.MikuDdPendingStory = nil
local oldPlay = DonoDuelTask.PlayAcceptStory
DonoDuelTask.PlayAcceptStory = function(self, tb)
if type(tb) == [[table]] and tb.sAcceptStory and DonoDuel.CheckMainUI(true) ~= true then
_G.MikuDdPendingStory = tb
return
end
_G.MikuDdPendingStory = nil
return oldPlay(self, tb)
end
local function flush()
local tb = _G.MikuDdPendingStory
if not tb then return end
if DonoDuel.CheckMainUI(true) ~= true then return end
_G.MikuDdPendingStory = nil
pcall(DonoDuelTask.PlayAcceptStory, DonoDuelTask, tb)
end
_G.MikuDdFlushStory = flush
if type(UI) == [[table]] and type(UI.Close) == [[function]] and _G.bMikuDdStoryCloseHook ~= true then
_G.bMikuDdStoryCloseHook = true
local oldClose = UI.Close
UI.Close = function(...)
local r = table.pack(oldClose(...))
pcall(flush)
return table.unpack(r, 1, r.n)
end
end
if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDdStoryOpenHook ~= true then
_G.bMikuDdStoryOpenHook = true
local oldOpen = UI.Open
UI.Open = function(...)
local r = table.pack(oldOpen(...))
pcall(flush)
return table.unpack(r, 1, r.n)
end
end
end
ddsr()
if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDdStoryBoot ~= true then
_G.bMikuDdStoryBoot = true
local o = UI.Open
UI.Open = function(...) pcall(ddsr) return o(...) end
end";

	/// <summary>
	/// DonoDuelInteractName（第 458 节）：多诺房间交互点提示条显示 (NULL)。
	///
	/// 现象：站在多诺主房间功能点前，屏幕下方那条提示显示 (NULL)，右侧才是 F 键。
	/// 真因（运行时 hook Text + traceback 实测）：
	///   DLC/DonoDuel/DonoDuelHouseInteract.lua 的 SendOverlapMsg 用
	///   Text(DonoDuelConfig:CheckFunctionOpen(npc.nFunctionId).sName) 组装提示文案；
	///   而 CheckFunctionOpen 对功能 101（探索多诺卡）走了一条特殊分支，只返回
	///   {nID, ID, nSeasonId, nFunctionID} 的占位表，没有 sName，Text(nil) 就把
	///   (NULL) 画了出来。102 及以上返回的是完整功能配置，所以别的点正常。
	/// 修法：包一层 CheckFunctionOpen，返回表 sName 为空时按 id 从 tbFunctionList
	/// 补 sName / sDescription，其余原样返回；再挂 UI.Open 兜底，防止配置重载。
	///
	/// 第 459 节补充：上面这层包装一开始写成了「local r = old(...) ... return r」，
	/// 只回吐第一个返回值，把 CheckFunctionOpen 的第二个返回值吞掉了。
	/// DonoDuelRogue:Req_EnterLevel 恰好是「local a, b = CheckFunctionOpen(101)」，
	/// 且 b 为 nil 就当场 return —— 于是开始作战时请求根本没发出去，
	/// UI.ShowConnection 的遮罩一直挂着，表现就是「点开始对战后卡加载」。
	/// 现在改成 local a, b, c = old(...) ... return a, b, c，
	/// 并在 b 为 nil 时用 tbFunctionList[id] 兜底补上（101 占位表只回一个值）。
	/// </summary>
	public const string DonoDuelInteractName = @"-- DonoDuelInteractName（第 458/459 节修订）：多诺功能点提示名 和 进入关卡返回值
-- SendOverlapMsg 取 CheckFunctionOpen(id).sName 当提示文案；功能 101 走特殊分支
-- 只回占位表，没有 sName，这里按 id 从 tbFunctionList 补回名字。
-- 同时必须保留第二个返回值：Req_EnterLevel 用 b（完整配置行）判断能否发进入关卡请求。
local function ddname()
if type(DonoDuelConfig) ~= [[table]] then return end
if type(DonoDuelConfig.CheckFunctionOpen) ~= [[function]] then return end
if DonoDuelConfig.bMikuFuncNameFix == true then return end
DonoDuelConfig.bMikuFuncNameFix = true
local old = DonoDuelConfig.CheckFunctionOpen
DonoDuelConfig.CheckFunctionOpen = function(self, id, ...)
local a, b, c = old(self, id, ...)
local list = DonoDuelConfig.tbFunctionList
local conf = list and list[id]
if type(a) == [[table]] and a.sName == nil and type(conf) == [[table]] then
if conf.sName then a.sName = conf.sName end
if a.sDescription == nil then a.sDescription = conf.sDescription end
end
if b == nil and type(conf) == [[table]] then b = conf end
return a, b, c
end
end
ddname()
if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDdNameHook ~= true then
_G.bMikuDdNameHook = true
local o = UI.Open
UI.Open = function(...) pcall(ddname) local r = table.pack(o(...)) pcall(ddname) return table.unpack(r, 1, r.n) end
end";


	/// <summary>
	/// DonoDuelGuideRetry（第 474 节）：第一次进「探索多诺卡」主房间时，引导 64100
	/// （安卡希雅教你点任务按钮）不弹。
	///
	/// 真因（探针实测）：
	///   · 进 DonoDuelMainRoom 瞬间只做一次 GuideLogic.CheckGuide，
	///     此时自动接取的 101 还没下发回来，CheckDonoDuelTaskCompleted(101) = false，64100 被挡；
	///   · 紧接着客户端自动接 101（AcceptAuto=1），并在 1 秒内 DoSomething + GetReward 把它做完；
	///   · 之后再没有任何 UI 事件触发 CheckGuide，64100 就这么一直躺着 ——
	///     手动按一次 Esc（界面变化）它立刻就播，说明条件本身是满足的。
	///
	/// 修法：主房间打开后用 UE4.Timer.NextFrame 做一段「每 20 帧重查一次、共 240 帧」的兜底，
	/// 等 101 的自动接取/完成回包落地后自然把 64100 带出来。条件不满足时 CheckGuide 本身无副作用。
	/// </summary>
	public const string DonoDuelGuideRetry = @"-- DonoDuelGuideRetry（第 474 节）：主房间常驻复查，补上 64100 的漏播
local function ddgr()
if type(UI) ~= [[table]] or type(GuideLogic) ~= [[table]] then return end
if type(GuideLogic.CheckGuide) ~= [[function]] then return end
if type(UI.GetUI) ~= [[function]] then return end
local w = nil
pcall(function() w = UI.GetUI([[DonoDuelMainRoom]]) end)
if type(w) ~= [[table]] then
pcall(function() w = UI.GetUI([[donoduelmainroom]]) end)
end
if type(w) ~= [[table]] then return end
local t = 0
pcall(function() t = GuideLogic.GetTask(64100) end)
if t ~= 0 then return end
local can = false
pcall(function() can = GuideLogic.CanOpenGuide(64100) end)
if can ~= true then return end
pcall(GuideLogic.CheckGuide, w)
end
local function ddgw()
if type(UE4) ~= [[table]] or type(UE4.Timer) ~= [[table]] then return end
if type(UE4.Timer.NextFrame) ~= [[function]] then return end
if _G.bMikuGuideLoop == true then return end
_G.bMikuGuideLoop = true
local acc = 1
local function tick()
acc = acc - 1
if acc ~= 0 then
UE4.Timer.NextFrame(tick)
return
end
acc = 30
pcall(ddgr)
UE4.Timer.NextFrame(tick)
end
UE4.Timer.NextFrame(tick)
end
ddgw()";

	/// <summary>
	/// DonoDuelCardPop（第 484 节）：支线任务领奖后不弹「获得多诺卡」。
	///
	/// 现象：探索多诺卡打完第 2 阶第 4 区 / 第 3 阶第 4 区，官服会弹「获得多诺卡」
	///       （3 张 / 2 张），本服什么都不弹。那两张正是剧情任务
	///       201「到达第2阶第3区」= 3 张、202「到达第3阶第3区」= 2 张。
	///
	/// 链路（客户端字节码确认）：
	///   DonoDuelTask_GetReward(s2c) → ShowMissionReward1Pop(道具，duel_reward_item)
	///     → 关闭后 ShowMissionReward2Pop → SafeOpen("DonoDuelCollect", tbReward2)
	///       → ShowMissionReward3Pop(tbRewards.tbCardId)  ← 这才是「获得多诺卡」
	///   而 201/202 这类支线的卡奖励全在 tbReward3，tbReward2 是空表，
	///   所以 2Pop 直接 return，卡面永远不弹（官服客户端已经按 tbReward3 兜底）。
	///
	/// 修法：包一层 1Pop / 2Pop ——
	///   有 tbReward3、但没有道具奖励 / 没有 tbReward2 时，
	///   按 tbReward3 的模板去背包里找服务端刚发的卡（uid 最大的一批），
	///   直接调 ShowMissionReward3Pop 弹出与官服一致的卡面。
	///
	/// 实测要点（2026-11-21 15:0x，字节码 dump + 逐形状试）：
	///   ShowMissionReward3Pop 是「冒号定义」ShowMissionReward3Pop(self, tbParam)，
	///   内部读 tbParam.tbRewards.tbCardId，再 DonoDuel:GetBagItemAndConfig(uid)
	///   拿卡面配置，最后 Item.Gain{ bShowDono=true, sTitle="donoduel.duel_reward_card" }。
	///   所以必须传 { tbRewards = { tbCardId = {uid…} } }；传 { tbCardId = … } 时
	///   那个表被当成 self、tbParam 为 nil，函数静默 return（不报错、不弹窗），
	///   这正是前一版补丁「pop=true 但什么都没出现」的原因。
	///   同理 GetBagItemAndConfig 也是冒号定义，必须 D:GetBagItemAndConfig(uid)。
	///
	///   1Pop / 2Pop 的原函数同样是冒号定义，包装时用 (a,b) 变参兼容
	///   「冒号调用」和「点号调用」两种形态，避免把 self 当 tbParam。
	///
	/// 注意：本段不能出现加号、尖括号、和号与引号，Send 会整条丢弃。
	/// </summary>
	public const string DonoDuelCardPop = @"-- DonoDuelCardPop（第 484 节）：支线任务卡奖励只在 tbReward3，领奖不弹卡
if _G.bMikuCardPopInstalled == true then return end
_G.bMikuCardPopInstalled = true

local T = DonoDuelTask
if T == nil then return end
if T.tbMissionConf == nil then return end

local function plog(s)
local f = io.open([[D:/DSH/.tools/donoduelcardpop.log]], [[a]])
if f ~= nil then
f:write(os.date([[%H:%M:%S ]]), s, [[\n]])
f:close()
end
end

local function pick(cfg)
local rows = cfg.tbReward3
if rows == nil then return nil end
local n = #rows
if n == 0 then return nil end
local all = DonoDuel.GetBagAllCard()
if all == nil then return nil end
local list = {}
for _, c in pairs(all) do
if type(c) == [[table]] and c.nUniqueId ~= nil then
table.insert(list, c)
end
end
if #list == 0 then return nil end
table.sort(list, function(a, b)
return math.max(b.nUniqueId, a.nUniqueId) ~= a.nUniqueId
end)
local used = {}
local picked = {}
for i = 1, #list do
if #picked == n then break end
local c = list[i]
local dup = false
for k = 1, #picked do
if picked[k] == c.nUniqueId then
dup = true
break
end
end
if not dup then
for j = 1, n do
if used[j] == nil and rows[j][1] == c.nTmpId then
used[j] = true
table.insert(picked, c.nUniqueId)
break
end
end
end
end
if #picked == 0 then return nil end
return picked
end

local function fire(uids, tag)
if type(uids) ~= [[table]] then return false end
if #uids == 0 then return false end
local ok = pcall(T.ShowMissionReward3Pop, T, { tbRewards = { tbCardId = uids } })
plog(tag .. [[ pop=]] .. tostring(ok) .. [[ n=]] .. tostring(#uids))
return ok
end

local function cardIdsOf(tp)
if type(tp) ~= [[table]] then return nil end
local wr = tp.tbRewards
if type(wr) ~= [[table]] then return nil end
local ids = wr.tbCardId
if type(ids) ~= [[table]] then return nil end
if #ids == 0 then return nil end
return ids
end

local function doPop(cfg, tp, tag)
local ids = cardIdsOf(tp)
if ids ~= nil then
if fire(ids, tag .. [[ sv]]) then return true end
end
local okp, uids = pcall(pick, cfg)
if not okp then
plog(tag .. [[ pick err]])
return false
end
if uids == nil then
plog(tag .. [[ no uid]])
return false
end
return fire(uids, tag)
end

local function cfgOf(tbParam)
if type(tbParam) ~= [[table]] then return nil end
local id = tbParam.nId
if id == nil then return nil end
return T.tbMissionConf[id]
end

local old1 = T.ShowMissionReward1Pop
if type(old1) == [[function]] then
T.ShowMissionReward1Pop = function(a, b)
local cs = false
if type(a) == [[table]] and a.tbMissionConf ~= nil then cs = true end
local tp = a
if cs then tp = b end
local cfg = cfgOf(tp)
if cfg ~= nil and cfg.tbReward3 ~= nil and #cfg.tbReward3 ~= 0 then
local items = nil
if type(tp) == [[table]] and type(tp.tbRewards) == [[table]] then items = tp.tbRewards.tbItems end
if items == nil or #items == 0 then
if doPop(cfg, tp, [[r1]]) then return end
end
end
if cs then return old1(a, b) end
return old1(a)
end
end

local old2 = T.ShowMissionReward2Pop
if type(old2) == [[function]] then
T.ShowMissionReward2Pop = function(a, b)
local cs = false
if type(a) == [[table]] and a.tbMissionConf ~= nil then cs = true end
local tp = a
if cs then tp = b end
local cfg = cfgOf(tp)
if cfg ~= nil and cfg.tbReward3 ~= nil and #cfg.tbReward3 ~= 0 then
local r2 = cfg.tbReward2
if r2 == nil or #r2 == 0 then
if doPop(cfg, tp, [[r2]]) then return end
end
end
if cs then return old2(a, b) end
return old2(a)
end
end
plog([[installed]])";

	/// <summary>
	/// DonoDuelAvatar（第 483 节）：「探索多诺」地图上 NPC 气泡只剩白色空圈。
	///
	/// 现象：地图上安卡希雅（NpcRole9）的位置只有一个发光的白圈加一个感叹号，
	///       圈里没有立绘头像。用户报的就是这个。
	///
	/// 真因（探针实测，2026-11-21）：
	///   NpcRole9.Object.ImgIconNew 的 Visibility = 1（Collapsed），
	///   而它的 Brush 已经挂上了正确的贴图（icon_girl001_130_png）。
	///   手动 img:SetVisibility(0) 之后头像立刻出现在圈里，截图核对通过。
	///   也就是说跟 painting / resource 表一点关系都没有，
	///   是控件被折叠了——与第 253 节基地宿舍头像问题是同一个根。
	///   （第 252 节「补 painting 1210008」的结论当时就被 253 节作废，别再走那条路。）
	///
	/// 修法：
	///   1. 遍历 UI.tbWidget 里 sName == donoduelmap 的实例，从它的控件树走一遍，
	///      把所有能取到 ImgIconNew 字段的节点（PanelParlour 下的 NpcRole1..9）记下来；
	///   2. 每 0.35 秒把这些节点的 ImgIconNew 强制成 4（SelfHitTestInvisible：
	///      画得出来但不吃点击，不会挡住 NPC 的 OnClickNpc）；
	///   3. 同时包一层 UI.Open / UI.SafeOpen，地图刚打开时立刻扫一次；
	///   4. 另外挂一条 UE4.Timer.NextFrame 心跳，避免 EventSystem 在多诺地图里不触发。
	///   列表按对象地址串（tostring）缓存，地址变了才重扫；每 30 次 tick 再强制
	///   重扫一次，兜住地图内动态增删的节点。用 obj 本身做相等比较不成立——
	///   UnLua 每次取 v.Object 都可能给出新的 Lua 包装，会导致每秒重扫整棵树。
	///
	/// 注意：本段不能出现加号、尖括号、和号与引号，Send 会整条丢弃；
	///       计数一律写成 减负数。
	/// </summary>
	public const string DonoDuelAvatar = @"-- DonoDuelAvatar（第 483 节）：多诺地图 NPC 气泡头像被折叠
-- 单例保护：补丁在登录后会按 2500/9000/20000ms 推三次，每次都是一整段
-- 独立 chunk。不加这道闸，就会装出三套 NextFrame 心跳和三份计数器
-- （日志里 30 秒 9 次 scan、total 忽大忽小就是撞出来的）。
if _G.bMikuDdInstalled == true then return end
_G.bMikuDdInstalled = true
local tbDdList = {}
local tDdScan = 0
local nDdHit = 0
local nDdAge = 0
local nDdLog = 0
local function ddLog(s)
if nDdLog == 120 then return end
nDdLog = nDdLog - (-1)
local h = io.open([[D:/DSH/.tools/donoduelavatar.log]], [[a]])
if h ~= nil then
h:write(tostring(s) .. string.char(10))
h:close()
end
end
local function ddFixOne(w)
if w == nil then return 0 end
local ok, img
ok, img = pcall(function() return w.ImgIconNew end)
if not ok or img == nil then return 0 end
local okv, v
okv, v = pcall(function() return img.Visibility end)
if okv and v ~= nil and v ~= 1 then return 0 end
if not pcall(function() img:SetVisibility(4) end) then return 0 end
pcall(function() img:SetRenderOpacity(1) end)
return 1
end
local function ddWalk(w, d, out)
if w == nil or d == nil then return end
local ok, img
ok, img = pcall(function() return w.ImgIconNew end)
if ok and img ~= nil then table.insert(out, w) end
local wt = nil
local okt = pcall(function() wt = w.WidgetTree end)
if okt and wt ~= nil then
local root = nil
pcall(function() root = wt.RootWidget end)
if root ~= nil and root ~= w then ddWalk(root, d - 1, out) end
end
if d ~= 0 then
local c = nil
pcall(function() c = w:GetChildrenCount() end)
if type(c) == [[number]] then
for i = 0, c - 1 do
local ch = nil
pcall(function() ch = w:GetChildAt(i) end)
if ch ~= nil then ddWalk(ch, d - 1, out) end
end
end
end
end
local function ddMapObj()
local ui = UI
if type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return nil end
for _, v in pairs(ui.tbWidget) do
if type(v) == [[table]] and type(v.sName) == [[string]] and string.lower(v.sName) == [[donoduelmap]] then
if v.Object ~= nil then return v.Object end
end
end
return nil
end
local function ddRescan(obj)
local raw = {}
ddWalk(obj, 12, raw)
local p1 = nil
pcall(function() p1 = obj.PanelRoom1 end)
if p1 ~= nil then ddWalk(p1, 12, raw) end
local out = {}
local seen = {}
for i = 1, #raw do
local id = tostring(raw[i])
if seen[id] == nil then
seen[id] = true
table.insert(out, raw[i])
end
end
tbDdList = out
local n = #out
ddLog([[scan npc=]] .. tostring(n))
return n
end
local function ddTick()
local t = os.clock()
local ds = t - tDdScan
if math.max(ds, 0.35) ~= ds then return 0 end
tDdScan = t
local obj = ddMapObj()
if obj == nil then
if #tbDdList ~= 0 then tbDdList = {} end
return 0
end
-- 重扫策略：UnLua 里既不能用 obj 相等比较，也不能用 tostring(obj) 做身份
-- （每次取 v.Object 都是新的 Lua 包装表/新地址），所以不猜实例换没换：
--   · 列表为空就扫；
--   · 每 30 次 tick（约 10 秒）强制重扫，兜住地图内动态增删的节点；
--   · UI.Open/SafeOpen 打开 donoduelmap 时把列表清空，下一次 tick 立刻重扫。
-- 这样固定开销是「开图瞬间一次、之后每 10 秒一次」，不会变成每帧扫整棵树。
nDdAge = nDdAge - (-1)
if #tbDdList == 0 or nDdAge == 30 then
nDdAge = 0
ddRescan(obj)
end
local n = 0
local hit = nil
for i = 1, #tbDdList do
local r = ddFixOne(tbDdList[i])
if r == 1 and hit == nil then hit = tbDdList[i] end
n = n - (-r)
end
if n ~= 0 then
nDdHit = nDdHit - (-n)
local nm = [[]]
if hit ~= nil then pcall(function() nm = hit:GetName() end) end
ddLog([[fix n=]] .. tostring(n) .. [[ total=]] .. tostring(nDdHit) .. [[ nm=]] .. tostring(nm))
end
return n
end
local function ddHookUI()
local ui = UI
if type(ui) ~= [[table]] then return end
local tbName = { [[Open]], [[SafeOpen]] }
for i = 1, #tbName do
local k = tbName[i]
local flag = [[bMikuDdHook]] .. k
if type(ui[k]) == [[function]] and _G[flag] ~= true then
_G[flag] = true
local old = ui[k]
ui[k] = function(...)
local a1 = ...
local r = table.pack(old(...))
if type(a1) == [[string]] and string.find(string.lower(a1), [[donoduelmap]], 1, true) ~= nil then
tbDdList = {}
nDdAge = 0
end
pcall(ddTick)
return table.unpack(r, 1, r.n)
end
end
end
end
local function ddLoop()
pcall(ddTick)
UE4.Timer.NextFrame(ddLoop)
end
pcall(ddHookUI)
pcall(ddTick)
pcall(function() UE4.Timer.NextFrame(ddLoop) end)
_G.__ddDbg = function() return #tbDdList, nDdHit, nDdLog end";

	private static readonly string[] Order = new string[66]
	{
		"EventSystemGuard", "GlobalTimeWindows", "ActivityFaceQuiet", "DlcWindowFix",
		"DlcMissionBoard", "DlcShopWindows", "DonoDuelArenaWindows", "DonoDuelBaseSpawn",
		"AlienDefenseLevelWindows", "FunTimedWindows", "GirlRushWindows", "GirlRushUnlock",
		"GirlRushAutoWin", "DlcFuncWindowFix", "DlcLegacyMainWindows", "PlotHideAllWindows",
		"GachaTryWindows", "ShopPresaleGuard", "BagRogueRelicUnlock", "BagRogueRelicOption",
		"BagRogueEndNode", "BagRogueShopAffix", "DefaultModeWindows", "BossPvpFix",
		"PrologueSkip", "LevelUpQuiet", "NoFormerShop", "Dlc24Unlock", "PaintFix", "DormAvatar",
		"LoginGuard", "ThrowChallengeContinue", "LogiRebuildUnlock",
		"LogiRebuildNoConfirm", "LogiRebuildKeepIndex", "Rogue3SeasonOpen",
		"Rogue3BlessFix", "Rogue3WarWindowFix", "VcTrialWindows", "Dlc11StoryLevel",
		"ShootScoreUnlock", "Act283Open", "ChessWin", "FarmWarSlotFix", "FarmWarGrowFix",
		"FarmWarItemFix", "FarmWarLevelFix", "FarmWarPlantFix", "AlienPalUnlockFix",
		"Repression4NodeFix", "SeasonBossZoneHide", "DlcPuzzleOpen", "S2CSilentNoise",
		"SignShortPopOff", "GachaTryOldHide", "BirthdayWishYears",
		"SeasonBossYmirScene", "RmolPvpLoadingFix", "DonoDuelInteractName", "DonoDuelStarterFightBag",
		"DonoDuelGuide64107", "DonoDuelAcceptStoryRetry", "DonoDuelGuideRetry", "DonoDuelAvatar",
		"DonoDuelCardPop", "PalBuildSeqFallback"
	};

	/// <summary>
	/// 2026-11-16（第 359 节）：第 11 期「碧水假日」主线节点全是 ST-00 占位 + 上锁。
	///
	/// 真因（与第 29 节同一个坑，但那次的 Send-StoryShim 注入早就没了）：
	///   umg_dlc11_story 的 OnOpen 第一句就是 rawget(self, [[Level]])，
	///   拿不到就把 self.Level 塞一个只有空 Init/Open 的哑对象（防 OnOpen 崩）。
	///   于是 OnOpen 主流程把哑对象当关卡控件用，lv.Init 什么都没建，
	///   画面只剩 UMG 设计稿里预置的 ST-00 卡片（点击无反应，也就无从解锁）。
	///
	/// 修法（第 29 节的 A 方案，改成常驻补丁）：
	///   包装 UI.Open，任何页面打开后检查 dlc11story 实例：
	///     · 从 Object.LevelScrollBox 取第 0 个子控件 = 真实 uw_dlc11_level；
	///     · rawset(self, [[Level]], 真实控件) 覆盖哑对象；
	///     · 用章节号重跑一次 OnOpen，让它把 23 个关卡灌进 tbLevelWidget；
	///     · LevelScrollBox / Level 都 SetVisibility(0)，避免被折叠成空白。
	///   每个实例只修一次（inst.bMikuD11 标记），日志写 D:/DSH/.tools/d11story.log。
	///
	/// 注意：upvalue 名故意用 d11Prev（不在 dmPeel 的 oO/oC/oR/pR/oldOpen/old 名单里），
	///       否则切期时 DlcMainEntry 的剥层会把这层包装剥掉。
	///       补丁文本里也不允许出现 '<'（客户端 Json.lua 会整包丢弃，第 356 节）。
	/// </summary>

	/// <summary>
	/// 2026-11-16（第 361 节）：第 23 期七日派对「碎箱挑战 2」（关卡 8407）进入后角色无法移动。
	/// 根因：Task.Extend.ShootScoreMgr:ReceiveBeginPlay 末尾用 LockPlayer(true) 锁住输入
	/// （AGamePlayerController.LockControlAndUpdateUI / ELockCtrlReason.Custom1），
	/// 而本应随后执行的 LockPlayer(false) 在本服环境下不会发生，于是永久锁死。
	/// 处理：包装该模块的 LockPlayer，观察到加锁后在下一次界面事件补一次解锁；
	/// 若包装安装时角色已被锁住（模块晚于加锁创建），安装后立即补一次解锁。
	/// 只在射击计分类关卡存在该模块，作用域天然隔离。
	/// </summary>
	public const string ShootScoreUnlock = "local MOD = [[Task.Extend.ShootScoreMgr]]\n"
		+ "local LOG = [[D:/DSH/.tools/_ss_unlock.log]]\n"
		+ "\n"
		+ "local function slog(s)\n"
		+ "local f = io.open(LOG, [[a]])\n"
		+ "if f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "\n"
		+ "local function getMod()\n"
		+ "local m = package.loaded[MOD]\n"
		+ "if type(m) == [[table]] then return m end\n"
		+ "return nil\n"
		+ "end\n"
		+ "\n"
		+ "local function install(m)\n"
		+ "if _G.__ssW == nil then _G.__ssW = {} end\n"
		+ "local W = _G.__ssW\n"
		+ "if W.mod ~= nil and W.mod ~= m then W.lock = nil end\n"
		+ "if W.lock == nil then\n"
		+ "local orig = rawget(m, [[LockPlayer]])\n"
		+ "if type(orig) ~= [[function]] then return end\n"
		+ "local w = function(self, b, ...)\n"
		+ "local r = orig(self, b, ...)\n"
		+ "if b == true then _G.__ssPend = 1 end\n"
		+ "return r\n"
		+ "end\n"
		+ "rawset(m, [[LockPlayer]], w)\n"
		+ "W.lock = w\n"
		+ "W.mod = m\n"
		+ "_G.__ssPend = 1\n"
		+ "slog([[wrap installed]])\n"
		+ "end\n"
		+ "end\n"
		+ "\n"
		+ "local function probe()\n"
		+ "local m = getMod()\n"
		+ "if m == nil then return end\n"
		+ "install(m)\n"
		+ "if _G.__ssPend == 1 and _G.__ssW ~= nil and _G.__ssW.lock ~= nil then\n"
		+ "_G.__ssPend = 0\n"
		+ "local ok = pcall(_G.__ssW.lock, nil, false)\n"
		+ "slog([[unlock ok=]] .. tostring(ok))\n"
		+ "end\n"
		+ "end\n"
		+ "\n"
		+ "local function safeProbe()\n"
		+ "pcall(probe)\n"
		+ "end\n"
		+ "\n"
		+ "safeProbe()\n"
		+ "\n"
		+ "if _G.__ssHooked == nil then _G.__ssHooked = {} end\n"
		+ "local names = { [[GetUI]], [[OpenInner]], [[CloseByName]], [[Close]], [[ReOpen]], [[SafeOpen]], [[OpenWithCallback]], [[ActiveTop]] }\n"
		+ "if type(UI) == [[table]] then\n"
		+ "for i = 1, #names do\n"
		+ "local nm = names[i]\n"
		+ "local cur = rawget(UI, nm)\n"
		+ "local prev = _G.__ssHooked[nm]\n"
		+ "if prev == nil or cur ~= prev then\n"
		+ "if type(cur) == [[function]] then\n"
		+ "local fn = cur\n"
		+ "local nw = function(...)\n"
		+ "local r = fn(...)\n"
		+ "safeProbe()\n"
		+ "return r\n"
		+ "end\n"
		+ "_G.__ssHooked[nm] = nw\n"
		+ "rawset(UI, nm, nw)\n"
		+ "end\n"
		+ "end\n"
		+ "end\n"
		+ "end";

	public const string Dlc11StoryLevel = "local function d11log(s)\n"
		+ "	local ok, f = pcall(io.open, [[D:/DSH/.tools/d11story.log]], [[a]])\n"
		+ "	if ok and f then f:write(tostring(s)) f:write(string.char(10)) f:close() end\n"
		+ "end\n"
		+ "\n"
		+ "local function d11mod(k) return package.loaded[k] end\n"
		+ "\n"
		+ "local function d11IsReal(lv)\n"
		+ "	if type(lv) ~= [[table]] then return false end\n"
		+ "	return rawget(lv, [[Object]]) ~= nil\n"
		+ "end\n"
		+ "\n"
		+ "local function d11Grab(self)\n"
		+ "	local obj = self and self.Object\n"
		+ "	if obj == nil then return nil end\n"
		+ "	local box\n"
		+ "	pcall(function() box = obj.LevelScrollBox end)\n"
		+ "	if box == nil then return nil end\n"
		+ "	local lv\n"
		+ "	pcall(function() lv = box:GetChildAt(0) end)\n"
		+ "	if type(lv) ~= [[table]] then return nil end\n"
		+ "	if rawget(lv, [[Object]]) == nil then return nil end\n"
		+ "	return lv, box\n"
		+ "end\n"
		+ "\n"
		+ "local function d11Prep(self)\n"
		+ "	if type(self) ~= [[table]] then return false end\n"
		+ "	if d11IsReal(rawget(self, [[Level]])) then return false end\n"
		+ "	local lv, box = d11Grab(self)\n"
		+ "	if lv == nil then return false end\n"
		+ "	rawset(self, [[Level]], lv)\n"
		+ "	self.bMikuD11 = true\n"
		+ "	pcall(function() box:SetVisibility(0) end)\n"
		+ "	pcall(function() lv:SetVisibility(0) end)\n"
		+ "	return true\n"
		+ "end\n"
		+ "\n"
		+ "local function d11Count(lv)\n"
		+ "	local n = 0\n"
		+ "	local tw = rawget(lv, [[tbLevelWidget]])\n"
		+ "	if type(tw) == [[table]] then for _ in pairs(tw) do n = n - (-1) end end\n"
		+ "	return n\n"
		+ "end\n"
		+ "\n"
		+ "local function d11Find()\n"
		+ "	for _, w in pairs(UI.tbWidget or {}) do\n"
		+ "		if type(w) == [[table]] and tostring(w.sName) == [[dlc11story]] then return w end\n"
		+ "	end\n"
		+ "	return nil\n"
		+ "end\n"
		+ "\n"
		+ "local function d11Fix(tag)\n"
		+ "	local inst = d11Find()\n"
		+ "	if type(inst) ~= [[table]] then return false end\n"
		+ "	local fixed = d11Prep(inst)\n"
		+ "	if fixed then\n"
		+ "		d11log((tag or [[fix]]) .. [[ ch=]] .. tostring(inst.nChapterID) .. [[ n=]] .. tostring(d11Count(rawget(inst, [[Level]]))) .. [[ vis=]] .. tostring(inst.Visibility))\n"
		+ "	end\n"
		+ "	return fixed\n"
		+ "end\n"
		+ "\n"
		+ "local cls = d11mod([[UMG.Dlc11.umg_dlc11_story]])\n"
		+ "if type(cls) ~= [[table]] then\n"
		+ "	d11log([[no-cls]])\n"
		+ "	return\n"
		+ "end\n"
		+ "\n"
		+ "if rawget(cls, [[__MikuD11HookVer]]) ~= 7 then\n"
		+ "	rawset(cls, [[__MikuD11HookVer]], 7)\n"
		+ "	local names = { [[GetUI]], [[CloseByName]], [[Close]], [[ReOpen]], [[SafeOpen]], [[OpenWithCallback]] }\n"
		+ "	local hooked = {}\n"
		+ "	for i = 1, #names do\n"
		+ "		local n = names[i]\n"
		+ "		local f = UI[n]\n"
		+ "		if type(f) == [[function]] then\n"
		+ "			hooked[#hooked - (-1)] = n\n"
		+ "			UI[n] = function(...)\n"
		+ "				local r = table.pack(f(...))\n"
		+ "				pcall(d11Fix, n)\n"
		+ "				return table.unpack(r, 1, r.n)\n"
		+ "			end\n"
		+ "		end\n"
		+ "	end\n"
		+ "	d11log([[ui-hooks ]] .. table.concat(hooked, [[,]]))\n"
		+ "end\n"
		+ "\n"
		+ "local function d11FixDeep(tag)\n"
		+ "	local inst = d11Find()\n"
		+ "	if type(inst) ~= [[table]] then return false end\n"
		+ "	local fixed = d11Prep(inst)\n"
		+ "	local lv = rawget(inst, [[Level]])\n"
		+ "	local n = d11Count(lv)\n"
		+ "	if fixed or n == 0 then\n"
		+ "		if _G.__d11Busy ~= true then\n"
		+ "			_G.__d11Busy = true\n"
		+ "			pcall(inst.OnOpen, inst, inst.nChapterID)\n"
		+ "			_G.__d11Busy = nil\n"
		+ "			d11log((tag or [[deep]]) .. [[ ch=]] .. tostring(inst.nChapterID) .. [[ n=]] .. tostring(d11Count(rawget(inst, [[Level]]))))\n"
		+ "		end\n"
		+ "	end\n"
		+ "	return fixed\n"
		+ "end\n"
		+ "\n"
		+ "if _G.__d11EvtHooked ~= 7 then\n"
		+ "	_G.__d11EvtHooked = 7\n"
		+ "	local evs = { [[LevelStarUpdate]], [[LevelSuccessShow]], [[LevelUp]], [[OnChallengeFinish]] }\n"
		+ "	local done = {}\n"
		+ "	for i = 1, #evs do\n"
		+ "		local e = evs[i]\n"
		+ "		local ok = pcall(EventSystem.On, e, function() pcall(d11FixDeep, [[evt-]] .. e) end)\n"
		+ "		if ok then done[#done - (-1)] = e end\n"
		+ "	end\n"
		+ "	d11log([[evt-hooks ]] .. table.concat(done, [[,]]))\n"
		+ "end\n"
		+ "\n"
		+ "d11Fix([[boot]])\n"
		+ "print([[D11P7 ready]])";

	private static readonly JsonSerializerOptions PatchJsonOpts = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public const string EventSystemGuard = "if not MikuEvtSafe then\nMikuEvtSafe = true\nEventSystem.Trigger = function(e, ...)\nlocal tbRemoved = {}\nlocal tbTODO = {}\nfor k, v in pairs(EventSystem.tbEvents[e] or {}) do\ntable.insert(tbTODO, v.f)\nif v.bOnce then table.insert(tbRemoved, k) end\nend\nif e == [[Logined]] then ZoneTime.DoLogin() end\nfor _, k in ipairs(tbRemoved) do\nlocal t = EventSystem.tbEvents[e]\nif t then t[k] = nil end\nEventSystem.tbHandles[k] = nil\nend\nfor _, f in ipairs(tbTODO) do\nlocal ok, err = pcall(f, ...)\nend\nend\nEventSystem.TriggerTarget = function(p, s, ...)\nlocal tbTargets = EventSystem.tbTargets[p]\nif not tbTargets then return false end\nlocal tbEvents = tbTargets[s]\nif not tbEvents then\nreturn false\nend\nif next(tbEvents) == nil then return end\nfor _, f in pairs(tbEvents) do\nlocal ok, err = pcall(f, p, ...)\nend\nreturn true\nend\nend";

	public const string GlobalTimeWindows = "local function gtwfix()\nif not IsInTime then return end\nlocal floor = MIKU_ACTIVATION_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuGlobalTimePatched and MikuGlobalEraFloor == floor and MikuGlobalEraId == era then return end\nMikuGlobalTimePatched = true\nMikuGlobalEraFloor = floor\nMikuGlobalEraId = era\nlocal old = MikuRawIsInTime or IsInTime\nMikuRawIsInTime = old\nIsInTime = function(nStart, nEnd, nNow)\nif old(nStart, nEnd, nNow) then return true end\nif not nEnd or nEnd == -1 or nEnd == 0 then return false end\nif floor ~= 0 and math.max(nEnd, floor) ~= nEnd then return false end\nlocal t = nNow or GetTime()\nif old(-1, nEnd, t) then return false end\nreturn true\nend\nif Activity and Activity.LoadActivitiesConfig and Activity.bMikuEraReset ~= era then\nActivity.bMikuEraReset = era\nActivity.LoadActivitiesConfig()\nend\nend\ngtwfix()";

	public const string ActivityFaceQuiet = "local function afqfix()\nif not Activityface or not Activityface.CheckDateTime then return end\nif Activityface.bQuietPatched then return end\nActivityface.bQuietPatched = true\nlocal oldCheck = Activityface.CheckDateTime\nActivityface.CheckDateTime = function(InId)\nlocal raw = MikuRawIsInTime\nif not raw then return false end\nlocal cfg = Activityface.GetConfig(InId)\nif not cfg then return false end\nif not raw(cfg.tStarttime, cfg.tEndtime, GetTime()) then return false end\nreturn oldCheck(InId)\nend\nActivityface.tbPopFaceId = {}\nActivityface.bLoginFirst = false\nend\nafqfix()";

	public const string DlcWindowFix = "local function dlcwinfix()\nif not DLC_Logic or not DLC_Logic.GetCurConf then return end\nif DLC_Logic.bCurWinPatched then return end\nDLC_Logic.bCurWinPatched = true\nlocal oldGet = DLC_Logic.GetCurConf\nDLC_Logic.GetCurConf = function(...)\nlocal conf = oldGet(...)\nif conf then\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nconf.nEnterStartTime = now - off1\nconf.nCloseEndTime = now - off2\nend\nreturn conf\nend\nend\ndlcwinfix()";

	public const string DlcMissionBoard = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nAchievementDLC.LoadTimeConf() AchievementDLC.nMaxLoadId = 0 AchievementDLC.LoadConf()\nfor _, t in pairs(AchievementDLC.tbTiemConf) do t.nStartTime = now - off1 t.nEndTime = now - off2 end\nfor _, cfg in pairs(AchievementDLC.tbConfig) do cfg.nStartTime = math.min(cfg.nStartTime or 0, now - off1) cfg.nEndTime = math.max(cfg.nEndTime or 0, now - off2) end";

	public const string DlcShopWindows = "local c = DLC_Logic.GetCurConf() or {}\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal t = ShopLogic.tbShopTabConf\nsetmetatable(t, { __index = function(tt, k) return rawget(tt, tonumber(k)) end })\nfor _, id in ipairs(c.tbShopId or {}) do local info = t[id] info.nBegin = now - off1 info.nEnd = now - off2 end";

	public const string DonoDuelArenaWindows = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function ddfix()\nif not DonoDuelConfig then return end\nfor _, c in pairs(DonoDuelConfig.tbSeasonList or {}) do c.nOpenTime = now - off1 c.nCloseTime = now - off2 c.tbCondition = {} end\nfor _, f in pairs(DonoDuelConfig.tbFunctionList or {}) do f.nOpenTime = now - off1 f.nCloseTime = now - off2 end\nif DonoDuelArena and DonoDuelArena.tbArenaBattle then\nfor _, cfg in pairs(DonoDuelArena.tbArenaBattle) do cfg.nOpenTime = now - off1 cfg.nCloseTime = now - off2 end\nend\nend\nddfix()\nif DonoDuelConfig and not DonoDuelConfig.bWinPatched then\nDonoDuelConfig.bWinPatched = true\nlocal oldF = DonoDuelConfig.LoadFunctionConfig\nDonoDuelConfig.LoadFunctionConfig = function(...) local r = oldF(...) ddfix() return r end\nlocal oldC = DonoDuelConfig.LoadConfig\nDonoDuelConfig.LoadConfig = function(...) local r = oldC(...) ddfix() return r end\nend\nif DLC_Logic and not DLC_Logic.bDonoFuncStamp then\nDLC_Logic.bDonoFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ddfix() return r end\nend";

	public const string DonoDuelBaseSpawn = "local function ddspawn()\nif not DonoDuel or not DonoDuel.LoadForStarPos then return end\nif DonoDuel.bSpawnPatched then return end\nDonoDuel.bSpawnPatched = true\nlocal LOG = [[D:/DSH/.tools/dd_spawn.log]]\nlocal function lg(s)\nlocal f = io.open(LOG, [[a]])\nif f then f:write(os.date([[%H:%M:%S ]])..s..string.char(10)) f:close() end\nend\nlocal function ddok(p)\nif type(p) ~= [[table]] then return false end\nif type(p.tbPos) ~= [[table]] then return false end\nif p.tbPos[1] == nil then return false end\nif p.tbPos[3] == nil then return false end\nreturn true\nend\nlocal function ddkey()\nlocal k = nil\npcall(function()\nk = string.format([[Dono_PlayerPos_%d_%d]], me:Id(), DonoDuel:GetActId())\nend)\nreturn k\nend\nlocal function ddread()\nlocal k = ddkey()\nif not k then return nil end\nlocal s = nil\npcall(function() s = UE4.UUserSetting.GetString(k) end)\nif type(s) ~= [[string]] then return nil end\nlocal t = nil\npcall(function() t = json.decode(s) end)\nif ddok(t) then return t end\nreturn nil\nend\nlocal function ddlvl()\nlocal v = 0\npcall(function() v = DonoDuel:GetLevelID() or 0 end)\nreturn v\nend\nlocal function dddef()\nDonoDuel.tbInitPlayerPos = {tbPos = {-1705, 0, 168}, tbRot = {0, 0, 0}}\nreturn DonoDuel.tbInitPlayerPos\nend\nlocal function ddwrap(t, n, fn)\nif type(t) ~= [[table]] then return end\nlocal old = t[n]\nif type(old) ~= [[function]] then return end\nt[n] = function(...) return fn(old, ...) end\nend\nddwrap(DonoDuel, [[SaveInitPlayerPos]], function(old, self, tb)\nif ddlvl() ~= 0 then return end\nif type(tb) == [[table]] then DonoDuel.tbMikuBasePos = tb end\nreturn old(self, tb)\nend)\nddwrap(DonoDuel, [[DoEnterDonoMap]], function(old, ...)\nif ddlvl() == 0 and ddok(DonoDuel.tbInitPlayerPos) then\nDonoDuel.tbMikuBasePos = DonoDuel.tbInitPlayerPos\nlg([[enter rogue base=]]..tostring(json.encode(DonoDuel.tbMikuBasePos)))\nend\nDonoDuel.bMikuExitRogue = false\nreturn old(...)\nend)\nddwrap(DonoDuelRogue, [[Req_EnterLevel]], function(old, ...)\nDonoDuel.bMikuExitRogue = false\nreturn old(...)\nend)\nddwrap(DonoDuelRogue, [[Req_LevelSettlement]], function(old, ...)\nDonoDuel.bMikuExitRogue = true\nlg([[req settlement lvl=]]..tostring(ddlvl()))\nreturn old(...)\nend)\nddwrap(DonoDuel, [[LoadForStarPos]], function(old, self, ...)\nlocal lvl = ddlvl()\nif lvl ~= 0 and not DonoDuel.bMikuExitRogue then\nlg([[star skip lvl=]]..tostring(lvl))\nreturn old(self, ...)\nend\nDonoDuel.bMikuExitRogue = false\nlocal p = DonoDuel.tbMikuBasePos\nif not ddok(p) then p = ddread() end\nif ddok(p) then DonoDuel.tbInitPlayerPos = p end\nif not ddok(DonoDuel.tbInitPlayerPos) then dddef() end\nlg([[star base lvl=]]..tostring(lvl)..[[ pos=]]..tostring(json.encode(DonoDuel.tbInitPlayerPos)))\nreturn old(self, ...)\nend)\nddwrap(DonoDuel, [[LoadInitPlayerPos]], function(old, self, ...)\npcall(old, self, ...)\nif not ddok(DonoDuel.tbInitPlayerPos) then\nlocal p = ddread()\nif ddok(p) then DonoDuel.tbInitPlayerPos = p end\nend\nif not ddok(DonoDuel.tbInitPlayerPos) then return dddef() end\nreturn DonoDuel.tbInitPlayerPos\nend)\nlg([[spawn patch ready]])\nend\nddspawn()";

	public const string AlienDefenseLevelWindows = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function mowstamp()\n-- 115c：难度卡锁实锤修复。uw_defense_difficulty_OnListItemObjectSet\n-- 反汇编：bLock = GetLevelPassNum(cfg.nUnlockCondition) 中 0（前置关\n-- 通关记录），nUnlockCondition 为 nil 则直接解锁。wrap\n-- GetDifficultConfig 置 nil（DefendLogic 未加载时跳过，mowstamp 由\n-- LoadConfig 或 InitFuncObj hook 反复触发补装）。\nif DefendLogic and type(DefendLogic.GetDifficultConfig) == [[function]] and DefendLogic.bMikuDiffUnlock ~= true then\nDefendLogic.bMikuDiffUnlock = true\nlocal oldGDC = DefendLogic.GetDifficultConfig\nDefendLogic.GetDifficultConfig = function(...)\nlocal cfg = oldGDC(...)\nif type(cfg) == [[table]] and cfg.nUnlockCondition ~= nil then cfg.nUnlockCondition = nil end\nreturn cfg\nend\nend\nif AlienDefenseConfig then\nfor _, cfg in pairs(AlienDefenseConfig.tbTimeList or {}) do cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end\nend\nif not AlienDefenseLevel then return end\nfor _, cfg in pairs(AlienDefenseLevel.tbLevel or {}) do\ncfg.nStartTime = now - off1\ncfg.nEndTime = now - off2\n-- 115：碎石惊沙全难度解锁——74 关链式 Condition（类型2 通关前置关）\n-- 全部清空（客户端字段名未实锤，tbCondition 与 Condition 双写空表，\n-- Condition.Check 空表恒过；服务端 json 同步清空做双保险）。\nif cfg.tbCondition ~= nil then cfg.tbCondition = {} end\nif cfg.Condition ~= nil then cfg.Condition = {} end\nend\n-- 115b：tbLevel 结构探针（首条字段名清单，确认 115 清空命中字段）。\nif AlienDefenseLevel.bMikuAdProbe ~= true then\nAlienDefenseLevel.bMikuAdProbe = true\nlocal fa0 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fa0 then\nlocal n1 = 0\nfor _, c1 in pairs(AlienDefenseLevel.tbLevel or {}) do\nn1 = n1 - (-1)\nif n1 == 1 then\nlocal ps1 = {}\nfor k1, v1 in pairs(c1) do\nif math.min(#ps1, 40) ~= 40 then\nps1[#ps1 - (-1)] = tostring(k1) .. [[=]] .. type(v1)\nend\nend\nfa0:write([[adcfg keys=]] .. table.concat(ps1, [[,]]))\nfa0:write(string.char(10))\nend\nend\nfa0:write([[adcfg total=]] .. tostring(n1))\nfa0:write(string.char(10))\nfa0:close()\nend\nend\nend\nmowstamp()\nif AlienDefenseConfig and not AlienDefenseConfig.bActWinPatched then\nAlienDefenseConfig.bActWinPatched = true\nlocal oldAct = AlienDefenseConfig.LoadConfig\nAlienDefenseConfig.LoadConfig = function(...) local r = oldAct(...) mowstamp() return r end\nend\nif AlienDefenseLevel and not AlienDefenseLevel.bWinPatched then\nAlienDefenseLevel.bWinPatched = true\nlocal old = AlienDefenseLevel.LoadConfig\nAlienDefenseLevel.LoadConfig = function(...) local r = old(...) mowstamp() return r end\nend\nif DLC_Logic and not DLC_Logic.bMowFuncStamp then\nDLC_Logic.bMowFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) mowstamp() return r end\nend";

	public const string FunTimedWindows = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function ftfmt(t)\nlocal s = os.date([[%Y%m%d%H%M]], t)\nif s and #s == 12 then return s end\nreturn t\nend\nlocal function ftstamp()\nif not FunTimed then return end\nif FunTimed.tbSession then\nfor _, s in pairs(FunTimed.tbSession) do s.tbOpenTime = { ftfmt(now - off1), ftfmt(now - off2) } end\nend\nif FunTimed.tbLevels then\nfor _, l in pairs(FunTimed.tbLevels) do l.tbTimeLimit = { ftfmt(now - off1), ftfmt(now - off2) } end\nend\nend\nftstamp()\nif FunTimed and not FunTimed.bWinPatched then\nFunTimed.bWinPatched = true\nlocal oldLoad = FunTimed.Load\nFunTimed.Load = function(...) local r = oldLoad(...) ftstamp() return r end\nend\nif DLC_Logic and not DLC_Logic.bFtFuncStamp then\nDLC_Logic.bFtFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ftstamp() return r end\nend";

	public const string GirlRushWindows = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function grstamp()\nif not GirlRushConfig then return end\nlocal function fix(tb)\nif type(tb) ~= type({}) then return end\nfor _, cfg in pairs(tb) do\nif type(cfg) == type({}) then cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end\nend\nend\nfix(GirlRushConfig.tbRushList)\nfix(GirlRushConfig.tbRushLevel)\nend\ngrstamp()\nif GirlRushConfig and not GirlRushConfig.bGrWindowPatched then\nGirlRushConfig.bGrWindowPatched = true\nlocal oldList = GirlRushConfig.LoadGirlRushListCfg\nif oldList then\nGirlRushConfig.LoadGirlRushListCfg = function(...) local r = oldList(...) grstamp() return r end\nend\nlocal oldLevel = GirlRushConfig.LoadGirlRushLevelCfg\nif oldLevel then\nGirlRushConfig.LoadGirlRushLevelCfg = function(...) local r = oldLevel(...) grstamp() return r end\nend\nend\nif DLC_Logic and not DLC_Logic.bGrFuncStamp then\nDLC_Logic.bGrFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) grstamp() return r end\nend";

	public const string DlcFuncWindowFix = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal nWinStart = now - off1 local nWinEnd = now - off2\nlocal function dfwTable(v) return type(v) == type({}) end\nlocal function dfwFunc(v) return type(v) == [[function]] end\nlocal function dfwRun(sName, fn, ...)\nlocal bOk, v1 = pcall(fn, ...)\nif not bOk then print([[DlcFuncWindowFix failed:]], sName, v1) end\nreturn v1\nend\nlocal tbForceAct = tbDfwForceAct\nif not dfwTable(tbForceAct) then tbForceAct = {} tbDfwForceAct = tbForceAct end\nlocal tbDlcIds = tbDfwIds\nif not dfwTable(tbDlcIds) then tbDlcIds = {} tbDfwIds = tbDlcIds end\nlocal tbDone = tbDfwDone\nif not dfwTable(tbDone) then tbDone = {} tbDfwDone = tbDone end\nlocal function dfwGroupOfId(nId)\nif not (Online and dfwTable(Online.tbOnlineList)) then return nil end\nfor _, row in pairs(Online.tbOnlineList) do\nif dfwTable(row) and row.nId == nId and type(row.nGroup) == [[number]] then return row.nGroup end\nend\nend\nif Online and dfwFunc(Online.GetGroupConfig) and not Online.bDfwGroupAlias then\nOnline.bDfwGroupAlias = true\nlocal fnOldGroup = Online.GetGroupConfig\nOnline.GetGroupConfig = function(nGroup, ...)\nlocal tbRows = fnOldGroup(nGroup, ...)\nif dfwTable(tbRows) then return tbRows end\nlocal nReal = dfwGroupOfId(nGroup)\nif nReal and nReal ~= nGroup then\nlocal tbAlt = fnOldGroup(nReal, ...)\nif dfwTable(tbAlt) then return tbAlt end\nend\nreturn tbRows\nend\nend\nlocal function dfwGroup(nId)\nif not (Online and dfwFunc(Online.GetGroupConfig)) then return end\nlocal tbRows = Online.GetGroupConfig(nId)\nif not dfwTable(tbRows) then return end\nfor _, row in pairs(tbRows) do\nif dfwTable(row) then\nrow.nStartTime = nWinStart row.nEndTime = nWinEnd row.nAwardTime = nWinEnd\nrow.tbCondition = nil row.tbOpenHour = {} row.nHide = 0\nend\nend\nend\nlocal function dfwActivity(nId)\ntbForceAct[nId] = true\nlocal cfg = Activity and Activity.tbConfig and Activity.tbConfig[nId]\nif not dfwTable(cfg) and Activity and dfwFunc(Activity.GetActivityConfig) then\ncfg = Activity.GetActivityConfig(nId)\nend\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nif (cfg.nCloseFlag or 0) ~= 0 then cfg.nCloseFlag = 0 end\nend\nlocal function dfwWorldboss(nId)\nif not (WorldBossLogic and dfwTable(WorldBossLogic.tbBossListCfg)) then return end\nlocal tbActs = WorldBossLogic.tbBossListCfg[nId]\nif not dfwTable(tbActs) then return end\nfor _, cfg in pairs(tbActs) do\nif dfwTable(cfg) then\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd cfg.nAwardTime = nWinEnd\nif dfwTable(WorldBossLogic.tbRankTimeCfg) and type(cfg.nReferRank1) == [[number]] then\nfor _, rt in pairs(WorldBossLogic.tbRankTimeCfg) do\nif dfwTable(rt) and rt.nID == cfg.nReferRank1 then\nrt.nStartTime = nWinStart rt.nEndTime = nWinEnd rt.nAwardTime = nWinEnd rt.nAwardEndTime = nWinEnd\nend\nend\nend\nend\nend\nend\nlocal function dfwFarmwar(nId)\nlocal cfg = FarmWarActivityCfg and FarmWarActivityCfg.tbActivityCfg and FarmWarActivityCfg.tbActivityCfg[nId]\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwCelebrate(nId)\nlocal cfg = StarWishConfig and StarWishConfig.tbTimeList and StarWishConfig.tbTimeList[nId]\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwClimb(nId)\nlocal tb = ClimbTower and ClimbTower.tbSeason and ClimbTower.tbSeason[nId]\nif not dfwTable(tb) then return end\ntb.nOpenTime = nWinStart tb.nCloseTime = nWinEnd\nend\nlocal function dfwRogue(nId)\nif not (Rogue3DLogic and dfwFunc(Rogue3DLogic.GetSeasonConfig)) then return end\nlocal cfg = Rogue3DLogic.GetSeasonConfig(nId)\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwMow(nId)\nif not (AlienDefenseConfig and dfwFunc(AlienDefenseConfig.GetTimeConfig)) then return end\nlocal cfg = AlienDefenseConfig.GetTimeConfig(nId)\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwRmol(nId)\nif not (DLC_RMOL and dfwFunc(DLC_RMOL.GetSession)) then return end\nlocal cfg = DLC_RMOL.GetSession(nId)\nif not dfwTable(cfg) then return end\ncfg.StartTime = nWinStart cfg.EndTime = nWinEnd\ncfg.IsOpen = function(self) return true end\nend\nlocal tbRoute = {\nRush = dfwGroup, Event = dfwGroup, Event2 = dfwGroup, Onlinesea = dfwGroup,\nOnlinepurge = dfwGroup, Towerrush = dfwGroup, Assaultops = dfwGroup, Dive = dfwGroup,\nPuzzle = dfwActivity, Former = dfwActivity, Nita = dfwActivity,\nWorldboss = dfwWorldboss, Farmwar = dfwFarmwar, Celebrate = dfwCelebrate,\nClimbTower = dfwClimb, Rogue = dfwRogue, Mow = dfwMow, Rmol = dfwRmol,\n}\nlocal function dfwStamp()\nif not (DLC_Logic and dfwFunc(DLC_Logic.GetCurConf)) then return end\nlocal conf = DLC_Logic.GetCurConf()\nlocal tbIds = conf and conf.tbDlcFuncConfId\nif not dfwTable(tbIds) then return end\nfor sKey, nId in pairs(tbIds) do\nif type(nId) == [[number]] then\ntbDlcIds[nId] = true\nlocal fnFix = tbRoute[sKey]\nif dfwFunc(fnFix) then fnFix(nId) end\nend\nend\nend\nlocal function dfwGuards()\nif not dfwTable(DLC_Logic) then return end\nlocal tbNames = { [[GetTimeInfo]], [[CheckIsUnlock]], [[GetSubTitle]], [[CheckRed]] }\nfor _, sFn in ipairs(tbNames) do\nlocal fnOld = DLC_Logic[sFn]\nif dfwFunc(fnOld) and not tbDone[sFn] then\ntbDone[sFn] = true\nDLC_Logic[sFn] = function(...)\nlocal bOk, v1, v2 = pcall(fnOld, ...)\nif bOk then return v1, v2 end\nreturn nil\nend\nend\nend\nend\nlocal function dfwOnlineOpen()\nif not (dfwTable(Online) and dfwFunc(Online.CheckOpen)) then return end\nif Online.bDfwOpen then return end\nOnline.bDfwOpen = true\nlocal fnOld = Online.CheckOpen\nOnline.CheckOpen = function(tbConf, bSkip, bAward)\nlocal bOk, sTip = fnOld(tbConf, bSkip, bAward)\nif bOk then return bOk, sTip end\nif dfwTable(tbConf) and (tbDlcIds[tbConf.nId] or tbDlcIds[tbConf.nGroup]) then return true end\nreturn bOk, sTip\nend\nend\nlocal function dfwRouter()\nif not (dfwTable(FunctionRouter) and dfwFunc(FunctionRouter.IsOpenById)) then return end\nif FunctionRouter.bDfwOpen then return end\nFunctionRouter.bDfwOpen = true\nlocal fnOld = FunctionRouter.IsOpenById\nFunctionRouter.IsOpenById = function(nType, ...)\nlocal bOk, tbMsg = fnOld(nType, ...)\nif bOk then return bOk, tbMsg end\nif next(tbDlcIds) ~= nil then return true, {} end\nreturn bOk, tbMsg\nend\nend\nlocal function dfwUnlock()\nlocal tbBase = DlcFuncBase\nif not dfwTable(tbBase) then return end\nif tbBase.bDfwUnlock then return end\ntbBase.bDfwUnlock = true\nlocal nOpen = 0\nif DLC_Logic and dfwTable(DLC_Logic.FuncState) then nOpen = DLC_Logic.FuncState.Open or 0 end\ntbBase.CheckIsUnlock = function(obj) return nOpen end\ntbBase.OpenFunc = function(obj, ...)\nif DLC_Logic and dfwFunc(DLC_Logic.SetFirstCheck) then pcall(DLC_Logic.SetFirstCheck, obj.sFuncKey) end\nlocal fnOpen = obj.OnOpenFunc\nif dfwFunc(fnOpen) then fnOpen(obj, ...) end\nend\nend\nlocal function dfwForceActivity()\nif not (dfwTable(Activity) and dfwFunc(Activity.IsOpen)) then return end\nif Activity.bDfwForceOpen then return end\nActivity.bDfwForceOpen = true\nlocal fnOldOpen = Activity.IsOpen\nActivity.IsOpen = function(nId, ...)\nif tbForceAct[nId] then return true, {} end\nreturn fnOldOpen(nId, ...)\nend\nend\nlocal function dfwHooks()\nif dfwTable(DLC_Logic) and not DLC_Logic.bDfwStamp then\nlocal fnInit = DLC_Logic.InitFuncObj\nif dfwFunc(fnInit) then\nDLC_Logic.bDfwStamp = true\nDLC_Logic.InitFuncObj = function(...)\ndfwRun([[unlock]], dfwUnlock)\nlocal r = fnInit(...)\ndfwRun([[unlock]], dfwUnlock)\ndfwRun([[stamp]], dfwStamp)\nreturn r\nend\nend\nend\nif dfwTable(Activity) and not Activity.bDfwStamp and dfwFunc(Activity.LoadActivitiesConfig) then\nActivity.bDfwStamp = true\nlocal fnActs = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) local r = fnActs(...) dfwRun([[stamp]], dfwStamp) return r end\nend\nif dfwTable(Online) and not Online.bDfwStamp and dfwFunc(Online.LoadConfig) then\nOnline.bDfwStamp = true\nlocal fnOnl = Online.LoadConfig\nOnline.LoadConfig = function(...) local r = fnOnl(...) dfwRun([[stamp]], dfwStamp) return r end\nend\nend\ndfwRun([[guards]], dfwGuards)\ndfwRun([[onlineopen]], dfwOnlineOpen)\ndfwRun([[router]], dfwRouter)\ndfwRun([[activity]], dfwForceActivity)\ndfwRun([[unlock]], dfwUnlock)\ndfwRun([[stamp]], dfwStamp)\ndfwRun([[hooks]], dfwHooks)";

	public const string DlcLegacyMainWindows = "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal wS = now - off1 local wE = now - off2\nlocal function lwOver(e)\nif type(e) ~= [[number]] or e == 0 or e == -1 then return false end\nreturn math.max(e, now) == now\nend\nlocal function lwRow(t)\nif type(t) ~= [[table]] then return t end\nif type(t.OpenTime) == [[number]] and lwOver(t.CloseTime) then t.OpenTime = wS t.CloseTime = wE end\nif type(t.nStartTime) == [[number]] and lwOver(t.nEndTime) then\nt.nStartTime = wS t.nEndTime = wE\nif type(t.nAwardTime) == [[number]] and t.nAwardTime ~= 0 then t.nAwardTime = wE end\nend\nreturn t\nend\nlocal function lwDead(s, e)\nif type(e) ~= [[number]] then return false end\nif e == -1 then return false end\nif e == 0 then return true end\nlocal started = math.max(s or 0, now) == now\nlocal live = math.max(e, now) == e and e ~= now\nreturn not (started and live)\nend\nlocal function lwList(tb)\nif type(tb) ~= [[table]] then return tb end\nfor _, t in pairs(tb) do lwRow(t) end\nreturn tb\nend\nlocal function lwRowGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...) return lwRow(old(...)) end\nend\nlocal function lwListGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...) return lwList(old(...)) end\nend\nlocal function lwPairGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...)\nlocal s, e = old(...)\nif lwDead(s, e) then return wS, wE end\nreturn s, e\nend\nend\nlocal function lwfix()\nlwRowGet(DLC_Chapter, [[GetChapterCfg]], [[bMikuLegacyCh]])\nlwRowGet(Online, [[GetConfig]], [[bMikuLegacyCfg]])\nlwListGet(Online, [[GetGroupConfig]], [[bMikuLegacyGrp]])\nlwRowGet(OnlineBoss, [[GetConfig]], [[bMikuLegacyBoss]])\nlwPairGet(DefendLogic, [[GetDlcActTime]], [[bMikuLegacyDef]])\nlwPairGet(GachaTry, [[GetTimeConf]], [[bMikuLegacyTry]])\nif VirCaptureConfig and type(VirCaptureConfig.GetTimeConfig) == [[function]] then\nif not VirCaptureConfig.bMikuLegacyVc then\nVirCaptureConfig.bMikuLegacyVc = true\nlocal oldVc = VirCaptureConfig.GetTimeConfig\nVirCaptureConfig.GetTimeConfig = function(self, ...) return lwRow(oldVc(self, ...)) end\nend\nend\nif BagRogueConfig and type(BagRogueConfig.GetTimeConfig) == [[function]] then\nif not BagRogueConfig.bMikuLegacyRg then\nBagRogueConfig.bMikuLegacyRg = true\nlocal oldRg = BagRogueConfig.GetTimeConfig\nBagRogueConfig.GetTimeConfig = function(self, ...) return lwRow(oldRg(self, ...)) end\nend\nend\nif Repression_Chapter and type(Repression_Chapter.tbActivity) == [[table]] then\nlwList(Repression_Chapter.tbActivity)\nend\nif UI and UI.tbWidget then\nfor _, w in pairs(UI.tbWidget) do\nif w and type(w.tbBtnInfo) == [[table]] and type(w.tbBtnInfo.Rogue) == [[table]] then\nlocal b = w.tbBtnInfo.Rogue\nif type(b.tbTime) == [[table]] then b.tbTime[1] = wS b.tbTime[2] = wE end\nb.bOver = false\nend\nend\nend\nend\nlwfix()\nif DLC_Logic and type(DLC_Logic.InitFuncObj) == [[function]] and not DLC_Logic.bMikuLegacyHook then\nDLC_Logic.bMikuLegacyHook = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) lwfix() return r end\nend";

	public const string PlotHideAllWindows = "local function dplot()\nif not PlayPlot then return end\nif MikuPlotWrapped == PlayPlot then return end\nlocal old = PlayPlot\nlocal function wrap(id, cb, ca, bgm, hide)\nif tonumber(id) == 8109201 then\nif ca then pcall(ca) end\nlocal L = _G.RichManLoveLogic\nlocal bFirst = true\nif type(L) == [[table]] and type(L.GetMapIsEntered) == [[function]] then\nlocal ok, v = pcall(L.GetMapIsEntered, 1)\nif ok and v == true then bFirst = false end\nend\nif bFirst and UI and type(UI.Open) == [[function]] then pcall(UI.Open, [[richmanlovedesc]]) end\nreturn\nend\nif hide == false or not id or id == 0 then return old(id, cb, ca, bgm, hide) end\nlocal token = {}\nMikuPlotToken = token\nlocal hid = {}\nif UI and UI.tbWidget then\nfor _, w in pairs(UI.tbWidget) do\nif w and w.IsOpen and w:IsOpen() then\nWidgetUtils.Hidden(w)\ntable.insert(hid, w)\nend\nend\nend\nlocal function back()\nif MikuPlotToken ~= token then return end\nfor i = 1, #hid do\nlocal w = hid[i]\nif w and IsValid(w) then WidgetUtils.Visible(w) end\nend\nMikuPlotToken = nil\nend\nlocal function done(...)\nback()\nif ca then return ca(...) end\nend\nreturn old(id, cb, done, bgm, false)\nend\nMikuPlotWrapped = wrap\nPlayPlot = wrap\nend\ndplot()";

	public const string GachaTryWindows = "local function gtry()\nif not Activity or not Activity.tbConfig then return end\nif not DLC_Logic or not DLC_Logic.GetCurConf then return end\nlocal floor = MIKU_ACT_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuTryWin2 and MikuTryWinFloor == floor and MikuTryWinEra == era then return end\nlocal conf = DLC_Logic.GetCurConf()\nif not conf or not conf.tbTryID then return end\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nfor _, group in pairs(conf.tbTryID) do\nlocal rows = Activity.GetTemplateCase(group)\nif rows then\nfor _, row in pairs(rows) do\nlocal nEnd = row.nEndTime\nif nEnd and nEnd ~= -1 and math.max(nEnd, floor) == nEnd then\nlocal srv = nil\nif Activity.GetOpenTime then srv = Activity.GetOpenTime(row.nId) end\nif srv and srv ~= 0 then row.nStartTime = srv else row.nStartTime = now - off1 end\nrow.nEndTime = now - off2\nrow.tbCondition = {}\nend\nend\nend\nend\nMikuTryWin2 = true\nMikuTryWinFloor = floor\nMikuTryWinEra = era\nend\nMikuTryApply = gtry\ngtry()\nif not MikuTryHooked2 then\nMikuTryHooked2 = true\nif DLC_Logic and DLC_Logic.InitFuncObj then\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) if MikuTryApply then MikuTryApply() end return r end\nend\nif Activity and Activity.LoadActivitiesConfig then\nlocal oldLoad = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) MikuTryWin2 = nil local r = oldLoad(...) if MikuTryApply then MikuTryApply() end return r end\nend\nend";

	public const string ShopPresaleGuard = "local M = IBLogic\nif type(M) == [[table]] and M.bMikuPresaleFix ~= true then\nM.bMikuPresaleFix = true\nlocal fnOld = M.CheckPreSellItem\nif type(fnOld) == [[function]] then\nM.CheckPreSellItem = function(...)\nlocal ok, a, b = pcall(fnOld, ...)\nif ok then return a, b end\nreturn false\nend\nend\nend";

	public const string BagRogueRelicOption = "-- 115i：GM 段修真根因。115h 埋点实锤：attempt to call a table\n-- value (field loaded)——package.loaded 后紧跟长括号字面量在\n-- Lua 里是调用语法（f 带字符串参数形式），不是取索引！115d 起\n-- 段内第一句就炸，pcall 静默吞掉，115g 错误处理器又被文件锁挡\n-- 住。修复：模块名走临时变量加单括号索引（与 probe 段同款写\n-- 法）；恢复元表 __index 双层 wrap（rawget 与 rawset）；埋点与\n-- 错误双写保留；dump 保留（60 函数 300KB 上限）。\nlocal le0 = io.open([[D:/DSH/.tools/c115_gm_err.txt]], [[a]])\nlocal lg0 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nlocal function glog(s0)\nif lg0 then\nlg0:write(s0)\nlg0:write(string.char(10))\nend\nend\nlocal function gelog(s0)\nif le0 then\nle0:write(s0)\nle0:write(string.char(10))\nend\nend\nlocal okg9, errg9 = pcall(function()\nglog([[=== chunk v115z enter ===]])\n-- 115ah：GM 彻底移除后的收尾清理。GM 三个 lua（GMCommandRegister /\n-- Misc.GM / UMG.GM.umg_adingm）所在的 Patch_GM-Windows_100_P.pak 已\n-- 整体移出游戏目录，客户端里已没有 GM 命令与面板逻辑；但\n-- umg_adingm 的 UMG 控件资源仍打在主 UI pak 里，进战斗时\n-- PreRelease.EnterFight 之后游戏自己会 UI.Open(AdinGM)，面板一开\n-- 悬浮图标就跟着出来。因此仅保留一道「禁止打开 AdinGM」的拦截\n-- （见下面 115ag 段，顺带把 ShowLuaError 弹窗一起吞掉）。\n-- 115t~115ad 那一整套（GM 模块入口置空、UI.tbWidget 定向扫实例、\n-- bMikuGmLast 重隐藏、WidgetUtils 高频钩子补隐藏、115x 的 UI 实现\n-- 扫描探针）已全部删除，不再需要在客户端里跟控件实例较劲。\n-- 保留：报错弹窗开关（登录扫一次，读 D:/DSH/.tools/miku_switch.txt）。\nlocal esw0 = io.open([[D:/DSH/.tools/miku_switch.txt]], [[r]])\nlocal eon0 = true\nif esw0 then\nlocal c0 = esw0:read([[*l]]) or [[]]\nesw0:close()\nif string.find(c0, [[false]], 1, true) ~= nil then\neon0 = false\nend\nend\nglog([[errpop config=]] .. tostring(eon0))\nif eon0 == false and rawget(_G, [[bMikuErrPopDone]]) ~= true then\nrawset(_G, [[bMikuErrPopDone]], true)\nlocal cz0 = utf8.char(0x62A5, 0x9519, 0x60C5, 0x51B5)\nlocal ecnt0 = 0\nlocal function escan0(efn, eowner, ekey, emodname)\nif type(efn) ~= [[function]] or type(ekey) ~= [[string]] or math.min(ecnt0, 39) == 39 then\nreturn\nend\nlocal oke, dpe = pcall(string.dump, efn)\nif oke and type(dpe) == [[string]] then\nif string.find(dpe, [[Lua error message]], 1, true) ~= nil or string.find(dpe, [[stack traceback]], 1, true) ~= nil or string.find(dpe, cz0, 1, true) ~= nil then\nrawset(eowner, ekey, function() end)\necnt0 = ecnt0 - (-1)\nglog([[errpop wrap: ]] .. emodname .. [[.]] .. ekey)\nend\nend\nend\nfor emk, emv in pairs(package.loaded) do\nif type(emk) == [[string]] and type(emv) == [[table]] and math.min(ecnt0, 39) ~= 39 then\npcall(function()\nfor ek0, ev0 in pairs(emv) do\nescan0(ev0, emv, ek0, emk)\nend\nlocal emt = getmetatable(emv)\nif type(emt) == [[table]] and type(emt.__index) == [[table]] then\nfor ek1, ev1 in pairs(emt.__index) do\nescan0(ev1, emt.__index, ek1, emk)\nend\nend\nend)\nend\nend\nglog([[errpop done got=]] .. tostring(ecnt0))\nend\n-- 115af：GirlRush 报错弹窗消除（第二版）。115ae 一条日志都没留下，\n-- 说明按 GirlRush.GirlRushLevelCfg 这个 key 取出来的不是函数表，\n-- 或该模块此刻还没进 package.loaded。改为模糊收集候选表：\n-- package.loaded 与 _G 里 key 含 GirlRush 的所有 table，逐个表的\n-- 函数用 debug.getinfo 的 linedefined 找 515~535 区间（报错栈是\n-- :523），命中就地 pcall 包装、出错静默返回 nil；全量字节码存\n-- c115af.bin，过程写 c115af.txt（含每个候选表的函数数与命中数）。\nif rawget(_G, [[bMikuGrWrap2]]) ~= true then\nrawset(_G, [[bMikuGrWrap2]], true)\nlocal afn = 0\nlocal afbuf = [[]]\nlocal afbufn = 0\nlocal function aflog(s)\nif math.min(afn, 60) == 60 then return end\nafn = afn - (-1)\npcall(function()\nlocal f = io.open([[D:/DSH/.tools/c115af.txt]], [[a]])\nif f then\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend)\nend\naflog([[af enter]])\nlocal cands = {}\nlocal seen = {}\nlocal function addcand(name, tb)\nif type(tb) ~= [[table]] then return end\nif seen[tb] == true then return end\nseen[tb] = true\ncands[#cands - (-1)] = { name, tb }\nend\nfor ck, cv in pairs(package.loaded) do\nif type(ck) == [[string]] and string.find(ck, [[GirlRush]], 1, true) ~= nil then\naddcand([[P:]] .. ck, cv)\nend\nend\npcall(function()\nfor ck2, cv2 in pairs(_G) do\nif type(ck2) == [[string]] and string.find(ck2, [[GirlRush]], 1, true) ~= nil then\naddcand([[G:]] .. ck2, cv2)\nend\nend\nend)\naflog([[cands=]] .. tostring(#cands))\nlocal found = 0\nlocal wrapn2 = 0\nfor ci = 1, #cands do\nlocal cnm = cands[ci][1]\nlocal ctb = cands[ci][2]\nlocal list = {}\nlocal inrange = 0\nfor fk, fv in pairs(ctb) do\nif type(fk) == [[string]] and type(fv) == [[function]] then\nlocal ln = -1\npcall(function()\nlocal info = debug.getinfo(fv, [[S]])\nif type(info) == [[table]] and type(info.linedefined) == [[number]] then\nln = info.linedefined\nend\nend)\nif math.min(afbufn, 80) ~= 80 then\nlocal okd, dd = pcall(string.dump, fv)\nif okd and type(dd) == [[string]] and math.min(#afbuf, 700000) ~= 700000 then\nafbufn = afbufn - (-1)\nafbuf = afbuf .. [[###]] .. cnm .. [[.]] .. fk .. [[#ln=]] .. tostring(ln) .. [[###]] .. string.char(10) .. dd .. string.char(10)\nend\nend\nlist[#list - (-1)] = { fk, fv, ln }\nif math.min(ln, 515) == 515 and math.min(ln, 535) == ln then\ninrange = inrange - (-1)\nfound = found - (-1)\nend\nend\nend\naflog([[cand ]] .. cnm .. [[ funcs=]] .. tostring(#list) .. [[ inrange=]] .. tostring(inrange))\nfor li = 1, #list do\nlocal fk2 = list[li][1]\nlocal fv2 = list[li][2]\nlocal ln2 = list[li][3]\nif math.min(ln2, 515) == 515 and math.min(ln2, 535) == ln2 then\nrawset(ctb, fk2, function(a1, a2, a3, ...)\nlocal okr, r1, r2, r3 = pcall(fv2, a1, a2, a3, ...)\nif okr == true then\nreturn r1, r2, r3\nend\naflog([[hit ]] .. cnm .. [[.]] .. fk2 .. [[ ln=]] .. tostring(ln2) .. [[ err=]] .. tostring(r1))\nreturn nil\nend)\nwrapn2 = wrapn2 - (-1)\nend\nend\nend\nlocal afp = io.open([[D:/DSH/.tools/c115af.bin]], [[wb]])\nif afp then\nafp:write(afbuf)\nafp:close()\nend\naflog([[done found=]] .. tostring(found) .. [[ wrap=]] .. tostring(wrapn2))\nend\n-- 115ag：UI 门面暴力定位。115af-b 实锤 package.loaded 里的\n-- UMG.UI 是个 boolean 而不是表，所以那层包装根本没装上（日志\n-- ui hooked=0）。这里改为枚举 package.loaded 与 _G 的全部 table，\n-- 找出真的带 OpenInner/Open 函数字段的那些，逐个包一层：面板名\n-- 不区分大小写含 adingm 或 luaerror 的直接吞掉（不打开），其余\n-- 原样透传。双重目标：①进战斗时 PreRelease.EnterFight 之后会自动\n-- UI.OpenInner(AdinGM)，这就是关卡里冒出来的 GM 图标来源，吞掉\n-- 它图标就不再出现；②ShowLuaError 弹窗同一条路，一并堵住。\n-- 候选表清单与命中情况写 c115ag.txt。\nif rawget(_G, [[bMikuUiGate2]]) ~= true then\nrawset(_G, [[bMikuUiGate2]], true)\nlocal agn = 0\nlocal function aglog(s)\nif math.min(agn, 60) == 60 then return end\nagn = agn - (-1)\npcall(function()\nlocal f = io.open([[D:/DSH/.tools/c115ag.txt]], [[a]])\nif f then\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend)\nend\nlocal seen = {}\nlocal cands = {}\nlocal function addt(name, tb)\nif type(tb) ~= [[table]] then return end\nif seen[tb] == true then return end\nseen[tb] = true\nif type(rawget(tb, [[OpenInner]])) == [[function]] or type(rawget(tb, [[Open]])) == [[function]] then\ncands[#cands - (-1)] = { name, tb }\nend\nend\nfor k, v in pairs(package.loaded) do\nif type(k) == [[string]] then\naddt([[P:]] .. k, v)\nend\nend\npcall(function()\nfor k2, v2 in pairs(_G) do\nif type(k2) == [[string]] then\naddt([[G:]] .. k2, v2)\nend\nend\nend)\naglog([[cands=]] .. tostring(#cands))\nlocal function agname(a1, a2)\nif type(a1) == [[string]] then return a1 end\nif type(a2) == [[string]] then return a2 end\nreturn nil\nend\nlocal function agtake(nm)\nif type(nm) ~= [[string]] then return false end\nlocal lo = string.lower(nm)\nif string.find(lo, [[adingm]], 1, true) ~= nil then return true end\nif string.find(lo, [[luaerror]], 1, true) ~= nil then return true end\nreturn false\nend\nlocal hit = 0\nlocal function agwrap(ofn, tag)\nreturn function(a1, a2, a3, ...)\nlocal nmz = agname(a1, a2)\nif agtake(nmz) == true then\naglog([[block ]] .. tag .. [[ ]] .. tostring(nmz))\nreturn\nend\nreturn ofn(a1, a2, a3, ...)\nend\nend\nfor ci = 1, #cands do\nlocal cnm = cands[ci][1]\nlocal ctb = cands[ci][2]\nlocal n0 = 0\nfor _, fnm in pairs({ [[OpenInner]], [[Open]] }) do\nlocal ofn = rawget(ctb, fnm)\nif type(ofn) == [[function]] then\nrawset(ctb, fnm, agwrap(ofn, cnm .. [[.]] .. fnm))\nn0 = n0 - (-1)\nhit = hit - (-1)\nend\nend\nlocal mt = getmetatable(ctb)\nif type(mt) == [[table]] then\nlocal ix = mt.__index\nif type(ix) == [[table]] then\nfor _, fnm2 in pairs({ [[OpenInner]], [[Open]] }) do\nlocal ofn2 = rawget(ix, fnm2)\nif type(ofn2) == [[function]] then\nrawset(ix, fnm2, agwrap(ofn2, cnm .. [[:ix.]] .. fnm2))\nn0 = n0 - (-1)\nhit = hit - (-1)\nend\nend\nelseif type(ix) == [[function]] then\nmt.__index = function(t2, k2)\nlocal val = ix(t2, k2)\nif (k2 == [[Open]] or k2 == [[OpenInner]]) and type(val) == [[function]] then\nreturn agwrap(val, cnm .. [[:fn.]] .. k2)\nend\nreturn val\nend\nn0 = n0 - (-1)\nhit = hit - (-1)\naglog([[cand ]] .. cnm .. [[ meta_index_fn wrapped]])\nend\nend\nif n0 ~= 0 then\naglog([[cand ]] .. cnm .. [[ hooked=]] .. tostring(n0))\nend\nend\naglog([[total hooked=]] .. tostring(hit))\nend\n-- 115x 的 UI 实现扫描探针在 115ah 清理时已删除（闪屏与 GM 都已另\n-- 有定论），这里只留一行说明，避免后人误以为漏了哪段。\n-- 115ai：GM 控件兜底隐藏（第二道）。pak 移出后 GM 命令已经不存在，\n-- 但 umg_adingm 这个 UMG 控件仍会被创建：18:50 那轮客户端是\n-- UI.OpenInner InName:AdinGM 直接开的，没走 UI.Open，门面只包了\n-- rawget 字段所以漏掉，图标又冒出来。这里恢复 115t 的实例隐藏法：\n-- 登录扫 UI.tbWidget，名字含 adingm/pnlgm/btngm/newgm 的实例直接\n-- WidgetUtils.Hidden 并记住路径；WidgetUtils 高频调用按计数节流\n-- 复查，实例失效就重扫。与门面拦截互补，任何一条生效图标都不出现。\nlocal function gmnname(s2)\nlocal okg3, g3 = pcall(function() return s2:GetName() end)\nif okg3 and type(g3) == [[string]] then\nlocal lo3 = string.lower(g3)\nif string.find(lo3, [[adingm]], 1, true) ~= nil or string.find(lo3, [[pnlgm]], 1, true) ~= nil or string.find(lo3, [[btngm]], 1, true) ~= nil or string.find(lo3, [[newgm]], 1, true) ~= nil then\nreturn true, g3\nend\nreturn false, g3\nend\nreturn false, [[?]]\nend\nlocal function gmhide(v3)\nlocal WU3 = rawget(_G, [[WidgetUtils]])\nif type(WU3) ~= [[table]] then\nlocal nmw3 = [[UMG.WidgetUtils]]\nWU3 = package.loaded[nmw3]\nend\nif type(WU3) == [[table]] and type(WU3.Hidden) == [[function]] then\npcall(WU3.Hidden, v3)\nreturn true\nend\nreturn false\nend\nlocal function gmcand(v2, tag2)\nif type(v2) ~= [[userdata]] then return false end\nlocal ig2, nm2 = gmnname(v2)\nif ig2 == true then\ngmhide(v2)\nrawset(_G, [[bMikuGmLast]], v2)\nglog([[gm hide: ]] .. tag2 .. [[ ]] .. tostring(nm2))\nreturn true\nend\nreturn false\nend\nlocal hitn0 = 0\nlocal UIh = rawget(_G, [[UI]])\nif type(UIh) == [[table]] then\nlocal tbw0 = rawget(UIh, [[tbWidget]])\nif type(tbw0) == [[table]] then\nfor wk0, wv0 in pairs(tbw0) do\nif type(wv0) == [[table]] then\nfor fk0, fv0 in pairs(wv0) do\nif gmcand(fv0, [[tbWidget.]] .. tostring(wk0) .. [[.]] .. tostring(fk0)) == true then\nhitn0 = hitn0 - (-1)\nend\nend\nelse\nif gmcand(wv0, [[tbWidget.]] .. tostring(wk0)) == true then\nhitn0 = hitn0 - (-1)\nend\nend\nend\nend\nend\nglog([[gm scan hit=]] .. tostring(hitn0))\nlocal gmlast0 = rawget(_G, [[bMikuGmLast]])\nif type(gmlast0) == [[userdata]] then\ngmhide(gmlast0)\nglog([[gm rehide done]])\nend\n-- 115ad：GM 图标复现修复。115t 靠登录后一次性 Hidden 管住了图标，\n-- 115u 为修闪屏精简后只剩这一次；实测图标在进入主界面后又被 UI\n-- 重新显示（或换了新实例，旧 bMikuGmLast 已失效）。改为事件驱动：\n-- 挂 WidgetUtils 的高频函数（节流 0.15 秒），先按上次命中的路径取\n-- 实例，失效再全扫 tbWidget，命中即 Hidden；同时把调用栈写进\n-- c115ad.txt（上限 30 条），用来定位到底是谁把它显示回来的。\nif rawget(_G, [[bMikuGmKeep]]) ~= true then\nrawset(_G, [[bMikuGmKeep]], true)\nlocal adn = 0\nlocal adt = 0\nlocal adc = 0\nlocal adf = 0\nlocal adh = [[]]\nlocal function adlog(s)\nif math.min(adn, 30) == 30 then return end\nadn = adn - (-1)\npcall(function()\nlocal f = io.open([[D:/DSH/.tools/c115ad.txt]], [[a]])\nif f then\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend)\nend\nlocal function adtb()\nlocal t = debug.traceback([[]], 4)\nt = string.gsub(t, string.char(10), [[ | ]])\nreturn string.sub(t, 1, 700)\nend\nlocal function adhide()\nlocal UIx = rawget(_G, [[UI]])\nif type(UIx) ~= [[table]] then return 0 end\nlocal tw = rawget(UIx, [[tbWidget]])\nif type(tw) ~= [[table]] then return 0 end\nlocal hit = 0\nlocal pth = rawget(_G, [[bMikuGmPath]])\nif type(pth) == [[table]] then\nlocal a = tw[pth[1]]\nlocal inst = nil\nif type(a) == [[table]] and pth[2] ~= nil then\ninst = rawget(a, pth[2])\nelse\ninst = a\nend\nif type(inst) == [[userdata]] then\nif gmcand(inst, [[path]]) == true then\nhit = hit - (-1)\nend\nend\nend\nif hit == 0 then\nfor wk, wv in pairs(tw) do\nif type(wv) == [[table]] then\nfor fk, fv in pairs(wv) do\nif gmcand(fv, [[ev.]] .. tostring(wk) .. [[.]] .. tostring(fk)) == true then\nhit = hit - (-1)\nrawset(_G, [[bMikuGmPath]], { wk, fk })\nend\nend\nelse\nif gmcand(wv, [[ev.]] .. tostring(wk)) == true then\nhit = hit - (-1)\nrawset(_G, [[bMikuGmPath]], { wk })\nend\nend\nend\nend\nreturn hit\nend\nlocal function adtick(tag)\nlocal okc, vc = pcall(os.clock)\nlocal usec = false\nif okc == true and type(vc) == [[number]] then\nusec = true\nend\nif usec == true then\nif adt ~= 0 then\nif math.min(vc - adt, 0.15) == 0 then return end\nend\nadt = vc\nelse\nadc = adc - (-1)\nif math.min(adc % 8, 8) ~= 0 then return end\nend\nif adf == 0 then\nadf = adf - (-1)\nadlog([[tick live tag=]] .. tostring(tag) .. [[ clock=]] .. tostring(usec))\nend\nlocal h = adhide()\nif h ~= 0 then\nadlog([[keep ]] .. tostring(tag) .. [[ hit=]] .. tostring(h) .. [[ t=]] .. tostring(os.time()) .. [[ ]] .. adtb())\nend\nend\nlocal adnm = [[UMG.WidgetUtils]]\nlocal WUa = rawget(_G, [[WidgetUtils]])\nif type(WUa) ~= [[table]] then\nWUa = package.loaded[adnm]\nend\nif type(WUa) == [[table]] then\nfor _, fn in pairs({ [[SetText]], [[Visible]], [[DisplayItems]], [[AddChildToPanel]], [[SetIcon]] }) do\nlocal f0 = rawget(WUa, fn)\nif type(f0) == [[function]] then\nrawset(WUa, fn, function(a1, a2, a3, ...)\nlocal r1, r2, r3 = f0(a1, a2, a3, ...)\npcall(adtick, fn)\nreturn r1, r2, r3\nend)\nadh = adh .. fn .. [[,]]\nend\nend\nadlog([[keep installed ]] .. adh)\nend\nend\n-- 115ab：闪屏终局修复（客户端赛季时间窗矫正）。115aa 探针实锤：\n-- 每秒一次的 BossPvpLogic.GetOpenID 全部来自\n-- umg_dungeonsboss_main.lua:841 的 Tick；而 GetTimeCfg 返回的是\n-- 本地表（challenge/bosspvp/boss_challenge.txt）里 ID 3 那一行的\n-- 2023 年窗口，nEndTime 为 1700452800，早已过期。Tick 的倒计时\n-- 归零判定于是永远成立，每秒重问服务端、每秒判定仍不满足、每秒\n-- ShowConnection 重开又关、每秒重播 dungeonsboss 相机，即闪屏。\n-- 服务端下发的窗口本来是合法的（当天 10:00 到 22:00），但客户端\n-- 按 id 取本地表，压根不看下发值。这里就地矫正 GetTimeCfg 的返回\n-- 值：本地 10:00 与 22:00 为 12 小时轮换分界（与服务端同一网格），\n-- 窗口一旦过期就改成当前这一格，倒计时恢复、轮询消失。\nif rawget(_G, [[bMikuBpHook]]) ~= true then\nrawset(_G, [[bMikuBpHook]], true)\nlocal abn = 0\nlocal function ablog(s7)\nif math.min(abn, 8) == 8 then return end\nabn = abn - (-1)\npcall(function()\nlocal f7 = io.open([[D:/DSH/.tools/c115ab.txt]], [[a]])\nif f7 then\nf7:write(tostring(s7))\nf7:write(string.char(10))\nf7:close()\nend\nend)\nend\nlocal function abwin(now)\nlocal d = os.date([[*t]], now)\nlocal h10 = os.time({ year = d.year, month = d.month, day = d.day, hour = 10, min = 0, sec = 0 })\nlocal h22 = os.time({ year = d.year, month = d.month, day = d.day, hour = 22, min = 0, sec = 0 })\nif math.min(now - h22, 0) == 0 then\nreturn h22, h22 - (-43200)\nend\nif math.min(now - h10, 0) == 0 then\nreturn h10, h22\nend\nreturn h10 - 43200, h10\nend\nlocal function abfix(cfg)\nif type(cfg) ~= [[table]] then return end\nlocal now = os.time()\nlocal en = cfg.nEndTime\nif type(en) ~= [[number]] then\nen = 0\nend\nlocal left = en - now\nif math.min(left, 0) == 0 and left ~= 0 then\nreturn\nend\nlocal st8, en8 = abwin(now)\ncfg.nStartTime = st8\ncfg.nEndTime = en8\nif type(cfg.nRewardTime) == [[number]] then\ncfg.nRewardTime = en8\nend\nablog([[fix id=]] .. tostring(cfg.nID) .. [[ old-en=]] .. tostring(en) .. [[ new-en=]] .. tostring(en8) .. [[ now=]] .. tostring(now))\nend\nlocal function abinstall()\nif rawget(_G, [[bMikuBpFix]]) == true then return end\nlocal BPA = rawget(_G, [[BossPvpLogic]])\nif type(BPA) ~= [[table]] then return end\nlocal fc = rawget(BPA, [[GetTimeCfg]])\nif type(fc) ~= [[function]] then return end\nrawset(_G, [[bMikuBpFix]], true)\nrawset(BPA, [[GetTimeCfg]], function(...)\nlocal cfgA = fc(...)\npcall(abfix, cfgA)\nreturn cfgA\nend)\nablog([[installed]])\nend\n-- 115ac：背包拼图 SetItemPos 护栏前置化。108b/112q 的护栏挂在\n-- UI.Open 上（等面板打开才装），且只靠 J.bMikuPosGuard 守标；\n-- 登录时若 bag_jigsaw 模块已在 package.loaded 里就直接装，不等\n-- 面板事件。装的时候顺手 dump 一次原生 SetItemPos 字节码\n-- （c115ac_bag.bin），下次真炸了就有原文可读，能照抄实现。\nlocal function abposguard()\nif rawget(_G, [[bMikuPosFix]]) == true then return end\nlocal okJ, J = pcall(function()\nlocal nmJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nreturn package.loaded[nmJ]\nend)\nif not okJ or type(J) ~= [[table]] then return end\nlocal op = rawget(J, [[SetItemPos]])\nif type(op) ~= [[function]] then return end\nrawset(_G, [[bMikuPosFix]], true)\npcall(function()\nlocal okD, dg = pcall(string.dump, op)\nif okD and type(dg) == [[string]] then\nlocal fg = io.open([[D:/DSH/.tools/c115ac_bag.bin]], [[wb]])\nif fg then\nfg:write(dg)\nfg:close()\nend\nend\nend)\nif rawget(J, [[bMikuPosGuard]]) == true then\nablog([[pos guard already]])\nreturn\nend\nrawset(J, [[bMikuPosGuard]], true)\nrawset(J, [[SetItemPos]], function(s2, ...)\nlocal okC, errC = pcall(op, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nlocal fixed = 0\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\nlocal okp = pcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nif okp then\nfixed = fixed - (-1)\nend\nend\nend\nend\nablog([[SetItemPos guard hit fixed=]] .. tostring(fixed) .. [[ err=]] .. tostring(errC))\nend)\nablog([[pos guard inst2]])\nend\nabposguard()\nabinstall()\npcall(function()\nlocal WU9 = rawget(_G, [[WidgetUtils]])\nif type(WU9) ~= [[table]] then return end\nfor _, k9 in pairs({ [[SetText]], [[Visible]] }) do\nlocal f9 = rawget(WU9, k9)\nif type(f9) == [[function]] then\nrawset(WU9, k9, function(...)\nif rawget(_G, [[bMikuBpFix]]) ~= true then pcall(abinstall) end\nif rawget(_G, [[bMikuPosFix]]) ~= true then pcall(abposguard) end\nreturn f9(...)\nend)\nend\nend\nend)\nend\nend)\nif not okg9 then\nglog([[GM ERR: ]] .. tostring(errg9))\ngelog([[GM ERR: ]] .. tostring(errg9))\nend\nif lg0 then\nlg0:close()\nend\nif le0 then\nle0:close()\nend\nlocal C = BagRogueConfig\nif type(C) == [[table]] and C.bMikuRelicOpt ~= true then\nC.bMikuRelicOpt = true\nlocal defs = {\n{ 3091, 301 }, { 3092, 301 },\n{ 3191, 311 }, { 3192, 311 },\n{ 3291, 321 }, { 3292, 321 },\n{ 3391, 331 }, { 3392, 331 },\n}\nfor i = 1, #defs do\nlocal d = defs[i]\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nif C.tbAdventure[d[1]] == nil then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = d[2], nOptionType = 9, nBuffId = 0,\nnOptionIcon = 110010331,\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = [[bagrogue.MikuOption9]],\nsOptionDesc = [[bagrogue.MikuDes9]],\ntbOption = { 1 }, tbUnlockEnding = {},\n}\nend\nend\nlocal L = Localization\nif type(L) == [[table]] and type(L.tbData) == [[table]] and type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nlocal k1 = [[bagrogue.MikuOption9]]\nlocal k2 = [[bagrogue.MikuDes9]]\nif L.tbData[k1] == nil then\nL.tbData[k1] = utf8.char(0x968F, 0x673A, 0x79D8, 0x5B9D) .. [[*{1}]]\nend\nif L.tbData[k2] == nil then\nL.tbData[k2] = utf8.char(0x4F60,0x4ECE,0x884C,0x56CA,0x6DF1,0x5904,0x7FFB,0x51FA,0x4E86,0x4E00,0x679A,0x672A,0x77E5,0x7684,0x79D8,0x5B9D,0x3002,0x82E5,0x8EAB,0x4E0A,0x5947,0x5668,0x672A,0x6EE1,0x4E09,0x4EF6,0x5219,0x7ACB,0x5373,0x88C5,0x5165,0xFF0C,0x82E5,0x5DF2,0x6EE1,0x4E09,0x4EF6,0x5219,0x6298,0x7B97,0x6210,0x6F20,0x5317,0x661F,0x7802,0x002A,0x0032,0x0030,0x0030,0x3002)\nend\nlocal k3 = [[bagrogue.MikuDes10]]\nif L.tbData[k3] == nil then\nL.tbData[k3] = utf8.char(0x628A, 0x5B83, 0x5E26, 0x5728, 0x8EAB, 0x4E0A, 0x5427, 0x2014, 0x2014, 0x5947, 0x5668, 0x7ACB, 0x5373, 0x751F, 0x6548, 0xFF0C, 0x901A, 0x5173, 0x540E, 0x56DE, 0x6536, 0x3002)\nend\nend\nend\nif type(C) == [[table]] and C.bMikuRelicOpt2 ~= true then\nC.bMikuRelicOpt2 = true\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nlocal rdefs = {\n{ 9501, 1001, 1001431, 1 }, { 9502, 1002, 1001432, 2 }, { 9503, 1003, 1001435, 3 }, { 9504, 1004, 1001436, 4 }, { 9505, 1005, 1001433, 5 }, { 9506, 1006, 1001434, 6 }, { 9507, 1007, 1001437, 7 }, { 9508, 1008, 1001438, 8 }, { 9509, 1009, 1001439, 9 }, { 9510, 1010, 1001440, 10 }, { 9511, 1011, 1001441, 11 }, { 9512, 1012, 1001442, 12 }, { 9513, 1013, 1001443, 13 }, { 9514, 1014, 1001444, 14 }, { 9515, 1015, 1001447, 15 }, { 9516, 1016, 1001448, 16 }, { 9517, 1017, 1001445, 17 }, { 9518, 1018, 1001446, 18 }, { 9519, 1019, 1001449, 19 }, { 9520, 1020, 1001450, 20 }, { 9521, 1021, 1001481, 21 }, { 9522, 1022, 1001482, 22 }, { 9523, 1023, 1001483, 23 }, { 9524, 1024, 1001484, 24 }, { 9525, 1025, 1001485, 25 }, { 9526, 1026, 1001486, 26 }, { 9527, 1027, 1001487, 27 }, { 9528, 1028, 1001488, 28 }, { 9529, 1029, 1001489, 29 }, { 9530, 1030, 1001490, 30 }\n}\nfor i = 1, #rdefs do\nlocal d = rdefs[i]\nif C.tbAdventure[d[1]] == nil then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = 0, nOptionType = 10, nBuffId = 0,\nnOptionIcon = d[3],\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = [[bagrogue.RelicName]] .. tostring(d[4]),\nsOptionDesc = [[bagrogue.MikuDes10]],\ntbOption = { d[2] }, tbUnlockEnding = {},\n}\nend\nend\nend\n-- 112e：秘宝卡客户端注入。gac 探针实锤客户端 adventure 表不是从\n-- adventure.json 实时读的——奇器卡 9501..9530 靠上面的运行时注入才有，\n-- 秘宝卡 12011..12275 同样必须注入（GetAdventureConfig 才查得到）。\n-- 名字/图标运行时从 GetJigsawConfig 取，描述用通用文案 MikuJigDes。\nif type(C) == [[table]] and C.bMikuJigOpt ~= true then\nC.bMikuJigOpt = true\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nlocal jdefs = {\n{ 12011, 1011 }, { 12012, 1012 }, { 12013, 1013 }, { 12014, 1014 }, { 12015, 1015 }, { 12021, 1021 }, { 12022, 1022 }, { 12023, 1023 },\n{ 12024, 1024 }, { 12025, 1025 }, { 12031, 1031 }, { 12032, 1032 }, { 12033, 1033 }, { 12034, 1034 }, { 12035, 1035 }, { 12041, 1041 },\n{ 12042, 1042 }, { 12043, 1043 }, { 12044, 1044 }, { 12045, 1045 }, { 12051, 1051 }, { 12052, 1052 }, { 12053, 1053 }, { 12054, 1054 },\n{ 12055, 1055 }, { 12061, 1061 }, { 12062, 1062 }, { 12063, 1063 }, { 12064, 1064 }, { 12065, 1065 }, { 12071, 1071 }, { 12072, 1072 },\n{ 12073, 1073 }, { 12074, 1074 }, { 12075, 1075 }, { 12081, 1081 }, { 12082, 1082 }, { 12083, 1083 }, { 12084, 1084 }, { 12085, 1085 },\n{ 12091, 1091 }, { 12092, 1092 }, { 12093, 1093 }, { 12094, 1094 }, { 12095, 1095 }, { 12101, 1101 }, { 12102, 1102 }, { 12103, 1103 },\n{ 12104, 1104 }, { 12105, 1105 }, { 12111, 1111 }, { 12112, 1112 }, { 12113, 1113 }, { 12114, 1114 }, { 12115, 1115 }, { 12121, 1121 },\n{ 12122, 1122 }, { 12123, 1123 }, { 12124, 1124 }, { 12125, 1125 }, { 12131, 1131 }, { 12132, 1132 }, { 12133, 1133 }, { 12134, 1134 },\n{ 12135, 1135 }, { 12141, 1141 }, { 12142, 1142 }, { 12143, 1143 }, { 12144, 1144 }, { 12145, 1145 }, { 12151, 1151 }, { 12152, 1152 },\n{ 12153, 1153 }, { 12154, 1154 }, { 12155, 1155 }, { 12161, 1161 }, { 12162, 1162 }, { 12163, 1163 }, { 12164, 1164 }, { 12165, 1165 },\n{ 12171, 1171 }, { 12172, 1172 }, { 12173, 1173 }, { 12174, 1174 }, { 12175, 1175 }, { 12181, 1181 }, { 12182, 1182 }, { 12183, 1183 },\n{ 12184, 1184 }, { 12185, 1185 }, { 12191, 1191 }, { 12192, 1192 }, { 12193, 1193 }, { 12194, 1194 }, { 12195, 1195 }, { 12201, 1201 },\n{ 12202, 1202 }, { 12203, 1203 }, { 12204, 1204 }, { 12205, 1205 }, { 12221, 1221 }, { 12222, 1222 }, { 12223, 1223 }, { 12224, 1224 },\n{ 12225, 1225 }, { 12231, 1231 }, { 12232, 1232 }, { 12233, 1233 }, { 12234, 1234 }, { 12235, 1235 }, { 12241, 1241 }, { 12242, 1242 },\n{ 12243, 1243 }, { 12244, 1244 }, { 12245, 1245 }, { 12251, 1251 }, { 12252, 1252 }, { 12253, 1253 }, { 12254, 1254 }, { 12255, 1255 },\n{ 12261, 1261 }, { 12262, 1262 }, { 12263, 1263 }, { 12264, 1264 }, { 12265, 1265 }, { 12271, 1271 }, { 12272, 1272 }, { 12273, 1273 },\n{ 12274, 1274 }, { 12275, 1275 },\n}\nfor i = 1, #jdefs do\nlocal d = jdefs[i]\nif C.tbAdventure[d[1]] == nil then\nlocal okj, jc = pcall(C.GetJigsawConfig, C, d[2])\nif okj and type(jc) == [[table]] then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = 0, nOptionType = 10, nBuffId = 0,\nnOptionIcon = jc.nIcon,\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = jc.sName,\nsOptionDesc = [[bagrogue.MikuJigDes]],\ntbOption = { d[2] }, tbUnlockEnding = {},\n}\nend\nend\nend\nlocal L = Localization\nif type(L) == [[table]] and type(L.tbData) == [[table]] and type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nlocal k4 = [[bagrogue.MikuJigDes]]\nif L.tbData[k4] == nil then\nL.tbData[k4] = utf8.char(0x79D8,0x5B9D,0x5DF2,0x6536,0x5165,0x884C,0x56CA,0xFF0C,0x53EF,0x5728,0x56FE,0x5F55,0x4E2D,0x67E5,0x770B,0x3002)\nend\nend\n-- 112h：本地化缺键记录器。图录秘宝「增益效果」全显示问号——在 tbData\n-- 上挂 __index 回退（只在查不到时触发，不改任何行为），命中 Talent/\n-- Affix/Jigsaw/Relic/bagrogue 相关键记 lk 日志，定位「？」是缺键还是缺参。\nif type(L) == [[table]] and type(L.tbData) == [[table]] and L.bMikuLkHook ~= true then\nL.bMikuLkHook = true\nif getmetatable(L.tbData) == nil then\nsetmetatable(L.tbData, {\n__index = function(t, k)\nlocal s9 = tostring(k)\nlocal f9 = string.find(s9, [[Talent]], 1, true)\nif f9 == nil then f9 = string.find(s9, [[Affix]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[Jigsaw]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[Relic]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[bagrogue]], 1, true) end\nif f9 ~= nil then\nlocal f = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif f then\nf:write([[lk:]] .. s9 .. string.char(10))\nf:close()\nend\nend\nreturn nil\nend\n})\nend\nend\n-- 112p：行前整备默认解锁。IsBagGridUnlock 字节码反汇编实锤：\n-- 解锁条件 = GetBagUnlockConfig(id).nUnlockOrder 中 BagRogue:GetBagMaxIndex()。\n-- 运行时遍历 BagRogueConfig 全部子表，凡子元素（或子表自身）含\n-- nUnlockOrder 数字的条目一律置 0——背包功能解锁表全开，\n-- 行前整备（TxtBackPackBuffReady）随之默认解锁。\npcall(function()\nfor kU, vU in pairs(C) do\nif type(vU) == [[table]] then\nif type(vU.nUnlockOrder) == [[number]] then\nvU.nUnlockOrder = 0\nend\nfor kV, vV in pairs(vU) do\nif type(vV) == [[table]] and type(vV.nUnlockOrder) == [[number]] then\nvV.nUnlockOrder = 0\nend\nend\nend\nend\nend)\nend\n-- 112l/112q：SetItemPos 护栏前置安装。第 108 节护栏挂在选卡链\n-- （tryPatchAfterOpen），但 bag_jigsaw 模块在打开背包页时才\n-- 加载——先选卡后开背包时 package.loaded 里还没有它，护栏\n-- 装不上，原生 SetItemPos 炸（invalid parameter A 上报）。\n-- 112q 事故复盘：旧版整段挂在 bMikuJigOpt 守卫块内且只 wrap\n-- 了 UI.OpenInner——本局实锤 OpenInner 从未触发（oi: 零产物），\n-- 背包面板走 UI.Open 打开；且 2.5s 推送时 UI 未就绪则 9s/20s\n-- 重推被外层守卫挡住永不重试。行前整备解锁后局外开背包即裸奔。\n-- 改为独立守卫块（UI 未就绪不置标，重推自动重试），Open 与\n-- OpenInner 双 wrap，装好写安装标记日志。\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and UI.bMikuPosGuardHook ~= true then\nUI.bMikuPosGuardHook = true\nlocal UIg = UI\nlocal function installPosGuard()\n-- 115i：package.loaded[[字面量]] 是调用语法不是索引（GM 段实锤\n-- attempt to call a table value），改临时变量单括号索引。\nlocal nmJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nlocal okJ, J = pcall(function() return package.loaded[nmJ] end)\nif okJ and type(J) == [[table]] and type(J.SetItemPos) == [[function]] and J.bMikuPosGuard ~= true then\nJ.bMikuPosGuard = true\nlocal lq1 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lq1 then\nlq1:write([[pos guard module installed]])\nlq1:write(string.char(10))\nlq1:close()\nend\nlocal origPos = J.SetItemPos\nJ.SetItemPos = function(s2, ...)\nlocal okC, errC = pcall(origPos, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\npcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nend\nend\nend\nlocal lf = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf then\nlf:write([[SetItemPos guard: ]] .. tostring(errC))\nlf:write(string.char(10))\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nlocal okCell = false\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos then okCell = true end\nend\nif not okCell then\nlf:write([[bad u=]] .. tostring(w and w.uId) .. [[ pos=]] .. tostring(w and w.nPos) ..\n[[ dir=]] .. tostring(w and w.nDir) ..\n[[ tp=]] .. tostring(w and w.tpCfg and w.tpCfg.ID))\nlf:write(string.char(10))\nend\nend\nlf:close()\nend\nend\nend\nend\n-- 112r：图录详情抓取前置。112n/112o 的 dump 全挂在 UI.OpenInner\n-- 与 BackpackRiki 名字守卫下，本局 oi: 零产物实锤图录面板同样走\n-- UI.Open 打开——抓取从未触发。挂在 112q 的 UI.Open wrap 里：\n-- 开任何含 ackpack 或 Riki 名面板后，枚举 package.loaded 全部\n-- backpack 或 Riki 模块，dump 模块函数与 metatable 方法（pk_/cx_\n-- 前缀），ItemInfo 详情类必在其中，一次性守标。\n-- 112w：图录详情类补 dump 提为独立函数。uw_backpack_riki_talent\n-- (_list) 要图录 tab 点击后才 require（bf missing 实锤），而 112v\n-- 把补 dump 放在 pk dump 守标之后——首触发即被 return 拦截，\n-- 后续面板事件永不执行。本函数每次 ackpack/Riki 面板事件都尝试\n-- （rt_ 前缀，bMikuBfCnt 上限 10，bf tail done 日志记抓取数）。\nlocal function dumpBfTail()\nlocal bf90 = {\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent_list]],\n}\nlocal bfc91 = tonumber(UIg.bMikuBfCnt) or 0\nif math.min(bfc91, 10) == 10 then return end\nlocal got99 = 0\nfor i92 = 1, #bf90 do\nlocal md93 = package.loaded[bf90[i92]]\nif type(md93) == [[table]] then\ngot99 = got99 - (-1)\nfor k94, v94 in pairs(md93) do\nif type(k94) == [[string]] and type(v94) == [[function]] then\nlocal ok95, d95 = pcall(string.dump, v94)\nif ok95 and type(d95) == [[string]] then\nlocal fn96 = io.open([[D:/DSH/.tools/c112_rt_]] .. tostring(i92) .. [[_]] .. k94 .. [[.bin]], [[wb]])\nif fn96 then\nfn96:write(d95)\nfn96:close()\nend\nend\nend\nend\nend\nend\nUIg.bMikuBfCnt = bfc91 - (-1)\nlocal lf97 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf97 then\nlf97:write([[bf tail done got=]] .. tostring(got99))\nlf97:write(string.char(10))\nlf97:close()\nend\nend\nlocal function dumpAfterOpen(nm)\nif type(nm) ~= [[string]] then return end\n-- 115b：碎石惊沙难度选择面板（名字含 lien 或 efense）独立 dump。\nif string.find(nm, [[lien]], 1, true) ~= nil or string.find(nm, [[efense]], 1, true) ~= nil then\n-- 115c：面板打开时补装 GetDifficultConfig wrap（与 mowstamp 同守标幂等）。\nif DefendLogic and type(DefendLogic.GetDifficultConfig) == [[function]] and DefendLogic.bMikuDiffUnlock ~= true then\nDefendLogic.bMikuDiffUnlock = true\nlocal oldGD2 = DefendLogic.GetDifficultConfig\nDefendLogic.GetDifficultConfig = function(...)\nlocal cfg2 = oldGD2(...)\nif type(cfg2) == [[table]] and cfg2.nUnlockCondition ~= nil then cfg2.nUnlockCondition = nil end\nreturn cfg2\nend\nend\nif UIg.bMikuAdDumped ~= true then\nUIg.bMikuAdDumped = true\nlocal cnta = 0\nfor mka, mva in pairs(package.loaded) do\nif type(mka) == [[string]] and type(mva) == [[table]]\nand (string.find(mka, [[lien]], 1, true) ~= nil or string.find(mka, [[efense]], 1, true) ~= nil) then\nfor mkc, mvc in pairs(mva) do\nif type(mkc) == [[string]] and type(mvc) == [[function]] and math.min(cnta, 200) ~= 200 then\ncnta = cnta - (-1)\nlocal okca, dca = pcall(string.dump, mvc)\nif okca and type(dca) == [[string]] then\nlocal fca = io.open([[D:/DSH/.tools/c115_ad_]] .. string.gsub(mka, [[%.]], [[_]]) .. [[_]] .. mkc .. [[.bin]], [[wb]])\nif fca then\nfca:write(dca)\nfca:close()\nend\nend\nend\nend\nlocal mta = getmetatable(mva)\nif type(mta) == [[table]] and type(mta.__index) == [[table]] then\nfor mkd, mvd in pairs(mta.__index) do\nif type(mkd) == [[string]] and type(mvd) == [[function]] and math.min(cnta, 200) ~= 200 then\ncnta = cnta - (-1)\nlocal okcd, dcd = pcall(string.dump, mvd)\nif okcd and type(dcd) == [[string]] then\nlocal fcd = io.open([[D:/DSH/.tools/c115_ad_]] .. string.gsub(mka, [[%.]], [[_]]) .. [[_]] .. mkd .. [[.bin]], [[wb]])\nif fcd then\nfcd:write(dcd)\nfcd:close()\nend\nend\nend\nend\nend\nend\nend\nlocal lfa = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lfa then\nlfa:write([[ad dump done nm=]] .. nm .. [[ cnt=]] .. tostring(cnta))\nlfa:write(string.char(10))\nlfa:close()\nend\nend\nend\nlocal isB = string.find(nm, [[ackpack]], 1, true) ~= nil\nlocal isR = string.find(nm, [[Riki]], 1, true) ~= nil\nlocal isR2 = string.find(nm, [[riki]], 1, true) ~= nil\nif not isB and not isR and not isR2 then return end\nif UIg.bMikuPkDumped == true then\npcall(dumpBfTail)\nreturn\nend\nUIg.bMikuPkDumped = true\nlocal cnt7 = 0\nfor mk7, mv7 in pairs(package.loaded) do\nif type(mk7) == [[string]] and type(mv7) == [[table]]\nand (string.find(mk7, [[ackpack]], 1, true) ~= nil\nor string.find(mk7, [[iki]], 1, true) ~= nil) then\nfor mk8, mv8 in pairs(mv7) do\nif type(mk8) == [[string]] and type(mv8) == [[function]] and math.min(cnt7, 170) ~= 170 then\ncnt7 = cnt7 - (-1)\nlocal okD7, d7 = pcall(string.dump, mv8)\nif okD7 and type(d7) == [[string]] then\nlocal fn7 = [[D:/DSH/.tools/c112_pk_]] .. string.gsub(mk7, [[%.]], [[_]]) .. [[_]] .. mk8 .. [[.bin]]\nlocal lf7 = io.open(fn7, [[wb]])\nif lf7 then\nlf7:write(d7)\nlf7:close()\nend\nend\nend\nend\nlocal mt7 = getmetatable(mv7)\nif type(mt7) == [[table]] and type(mt7.__index) == [[table]] then\nfor mk9, mv9 in pairs(mt7.__index) do\nif type(mk9) == [[string]] and type(mv9) == [[function]] and math.min(cnt7, 170) ~= 170 then\ncnt7 = cnt7 - (-1)\nlocal okD9, d9 = pcall(string.dump, mv9)\nif okD9 and type(d9) == [[string]] then\nlocal fn9 = [[D:/DSH/.tools/c112_cx_]] .. string.gsub(mk7, [[%.]], [[_]]) .. [[_]] .. mk9 .. [[.bin]]\nlocal lf9 = io.open(fn9, [[wb]])\nif lf9 then\nlf9:write(d9)\nlf9:close()\nend\nend\nend\nend\nend\nend\nend\n-- 112t：loaded 值非 table 的模块名记录（lv:）。uw_backpack_riki_talent\n-- 在 loaded（umin 实锤）却未被 dump，疑值类型非 table 被跳过。\nlocal lv50 = {}\nfor lk51, lv52 in pairs(package.loaded) do\nif type(lk51) == [[string]] and type(lv52) ~= [[table]]\nand string.find(lk51, [[ackpack]], 1, true) ~= nil then\nif math.min(#lv50, 20) ~= 20 then\ntable.insert(lv50, lk51 .. [[=]] .. type(lv52))\nend\nend\nend\nlocal lf69 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf69 then\nlf69:write([[lv:]] .. table.concat(lv50, [[,]]))\nlf69:write(string.char(10))\nlf69:close()\nend\n-- 112t：页面实例抓取。UI.GetUI(nm) 拿面板实例：函数字段直接 dump\n-- （ri2_），table 字段进一层（cf2_，ItemInfo 等子 widget 引用），\n-- 实例元表方法（cr2_）。详情类不在 loaded 模块表时由实例兜底抓到。\npcall(function()\nlocal gi53 = UIg.GetUI\nif type(gi53) ~= [[function]] then return end\nlocal ok54, inst55 = pcall(gi53, nm)\nif not ok54 or type(inst55) ~= [[table]] then return end\nlocal cnt57 = 0\nfor k58, v58 in pairs(inst55) do\nif type(v58) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok59, d59 = pcall(string.dump, v58)\nif ok59 and type(d59) == [[string]] then\nlocal fn60 = io.open([[D:/DSH/.tools/c112_ri2_]] .. tostring(k58) .. [[.bin]], [[wb]])\nif fn60 then\nfn60:write(d59)\nfn60:close()\nend\nend\nelseif type(v58) == [[table]] then\nfor k61, v61 in pairs(v58) do\nif type(v61) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok62, d62 = pcall(string.dump, v61)\nif ok62 and type(d62) == [[string]] then\nlocal fn63 = io.open([[D:/DSH/.tools/c112_cf2_]] .. tostring(k58) .. [[_]] .. tostring(k61) .. [[.bin]], [[wb]])\nif fn63 then\nfn63:write(d62)\nfn63:close()\nend\nend\nend\nend\nend\nend\nlocal rmt64 = getmetatable(inst55)\nif type(rmt64) == [[table]] and type(rmt64.__index) == [[table]] then\nfor k65, v65 in pairs(rmt64.__index) do\nif type(k65) == [[string]] and type(v65) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok66, d66 = pcall(string.dump, v65)\nif ok66 and type(d66) == [[string]] then\nlocal fn67 = io.open([[D:/DSH/.tools/c112_cr2_]] .. tostring(k65) .. [[.bin]], [[wb]])\nif fn67 then\nfn67:write(d66)\nfn67:close()\nend\nend\nend\nend\nend\nlocal lf68 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf68 then\nlf68:write([[inst dump done cnt=]] .. tostring(cnt57) .. [[ nm=]] .. nm)\nlf68:write(string.char(10))\nlf68:close()\nend\nend)\n-- 112v 旧补 dump 段已提为 dumpBfTail（112w），见 dumpAfterOpen 开头。\nlocal lf8 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf8 then\nlf8:write([[pk dump done cnt=]] .. tostring(cnt7) .. [[ nm=]] .. nm)\nlf8:write(string.char(10))\nlf8:close()\nend\nend\nif type(UIg.OpenInner) == [[function]] and UIg.bMikuPGOI ~= true then\nUIg.bMikuPGOI = true\nlocal oldOI2 = UIg.OpenInner\nUIg.OpenInner = function(nm, ...)\nlocal r2 = table.pack(oldOI2(nm, ...))\npcall(installPosGuard)\npcall(dumpAfterOpen, nm)\nreturn table.unpack(r2, 1, r2.n)\nend\nend\nif type(UIg.Open) == [[function]] and UIg.bMikuPGO ~= true then\nUIg.bMikuPGO = true\nlocal oldOP2 = UIg.Open\nUIg.Open = function(nm, ...)\nlocal r2 = table.pack(oldOP2(nm, ...))\npcall(installPosGuard)\npcall(dumpAfterOpen, nm)\nreturn table.unpack(r2, 1, r2.n)\nend\nend\npcall(installPosGuard)\nlocal lq2 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lq2 then\nlq2:write([[pos guard hook installed]])\nlq2:write(string.char(10))\nlq2:close()\nend\nend\n-- 112r：行前整备解锁结构探针。bagunlock 置 0 与 BagRogueConfig\n-- 遍历都没解开入口锁——解锁判定读的真实表与字段未知。登录时\n-- 打：GetBagMaxIndex 返回值（gmi）、GetBagUnlockConfig 前几条\n-- 结构（guc）、BagRogueConfig 顶层键名（bgk）、全局含 Unlock\n-- 或 Function 关键词的表（gu）。一次拿到真实结构再精准改。\npcall(function()\nlocal fo1 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fo1 == nil then return end\n-- 112s：浅层序列化器（深度 2、每层 40 键，防循环靠深度上限）。\nlocal function serMk(tm, dm)\nif type(tm) ~= [[table]] then return tostring(tm) end\nif math.min(dm, 3) == 3 then return [[T]] end\nlocal ps42 = {}\nfor k43, v43 in pairs(tm) do\nif math.min(#ps42, 40) ~= 40 then\ntable.insert(ps42, tostring(k43) .. [[=]] .. serMk(v43, dm - (-1)))\nend\nend\nreturn [[{]] .. table.concat(ps42, [[,]]) .. [[}]]\nend\nlocal BR = _G.BagRogue\nif type(BR) == [[table]] then\nlocal v1 = nil\npcall(function() v1 = BR.GetBagMaxIndex(BR) end)\nfo1:write([[gmi:]] .. tostring(v1))\nfo1:write(string.char(10))\nfor i2 = 1, 6 do\nlocal c2 = nil\npcall(function() c2 = BR.GetBagUnlockConfig(BR, i2) end)\nlocal ps2 = {}\nif type(c2) == [[table]] then\nfor k3, v3 in pairs(c2) do\nif type(v3) == [[table]] then\ntable.insert(ps2, tostring(k3) .. [[=tbl]])\nelse\ntable.insert(ps2, tostring(k3) .. [[=]] .. tostring(v3))\nend\nend\nend\nfo1:write([[guc:]] .. tostring(i2) .. [[ ]] .. table.concat(ps2, [[ ]]))\nfo1:write(string.char(10))\nend\n-- 112s：解锁表深探。GetBagUnlockConfig(1..6) 返回 nil（guc 空），\n-- 参数不是 1..6；解锁配置实际在 tbBagUnlock / tbBagUnlockOrder /\n-- nMaxBagUnlock。全量序列化拿真实字段名。\nlocal bu16 = BR.tbBagUnlock\nif type(bu16) == [[table]] then\nfo1:write([[tbu:]] .. serMk(bu16, 0))\nfo1:write(string.char(10))\nend\nlocal bo17 = BR.tbBagUnlockOrder\nif type(bo17) == [[table]] then\nfo1:write([[tbo:]] .. serMk(bo17, 0))\nfo1:write(string.char(10))\nend\nfo1:write([[nmx:]] .. tostring(BR.nMaxBagUnlock))\nfo1:write(string.char(10))\nlocal oc18 = nil\npcall(function() oc18 = BR.GetOpenConf(BR) end)\nif oc18 ~= nil then\nfo1:write([[oc:]] .. serMk(oc18, 0))\nfo1:write(string.char(10))\nend\nend\nlocal ps4 = {}\nfor k5, v5 in pairs(C) do\nif type(k5) == [[string]] and math.min(#ps4, 90) ~= 90 then\ntable.insert(ps4, k5)\nend\nend\nfo1:write([[bgk:]] .. table.concat(ps4, [[ ]]))\nfo1:write(string.char(10))\nlocal cnt6 = 0\nfor k6, v6 in pairs(_G) do\nif type(k6) == [[string]] and math.min(cnt6, 40) ~= 40 then\nlocal s6 = string.find(k6, [[nlock]], 1, true) ~= nil\nlocal s7 = string.find(k6, [[unction]], 1, true) ~= nil\nif s6 or s7 then\ncnt6 = cnt6 - (-1)\nfo1:write([[gu:]] .. k6 .. [[=]] .. type(v6))\nfo1:write(string.char(10))\nend\nend\nend\n-- 112s：功能屏蔽系统探查。行前整备入口锁疑走 FunctionShield\n-- 通用屏蔽（gu 实锤全局存在）。dump 其方法字节码（fs_ 前缀）\n-- 并序列化配置表内容（fsc:）。\nlocal fsc20 = rawget(_G, [[FunctionShieldConfig]])\nif type(fsc20) == [[table]] then\nfo1:write([[fsc:]] .. serMk(fsc20, 0))\nfo1:write(string.char(10))\nend\nlocal FS21 = rawget(_G, [[FunctionShield]])\nif type(FS21) == [[table]] then\nfor k22, v22 in pairs(FS21) do\nif type(v22) == [[function]] then\nlocal ok23, d23 = pcall(string.dump, v22)\nif ok23 and type(d23) == [[string]] then\nlocal fn24 = io.open([[D:/DSH/.tools/c112_fs_]] .. tostring(k22) .. [[.bin]], [[wb]])\nif fn24 then\nfn24:write(d23)\nfn24:close()\nend\nend\nend\nend\nend\n-- 112s：图录详情类定位。详情渲染在 Switcher.GetActiveWidget():Show，\n-- 83 个 dump 中无此类。搜索 ItemInfo/Detail 模块并 dump 方法（ii_ 前缀）。\nfor k26, v26 in pairs(package.loaded) do\nif type(k26) == [[string]] then\nlocal kl27 = string.lower(k26)\nlocal f28 = string.find(kl27, [[teminfo]], 1, true) ~= nil\nlocal f29 = string.find(kl27, [[etail]], 1, true) ~= nil\nif f28 or f29 then\nfo1:write([[ii found:]] .. k26)\nfo1:write(string.char(10))\nif type(v26) == [[table]] then\nfor k31, v31 in pairs(v26) do\nif type(v31) == [[function]] then\nlocal ok32, d32 = pcall(string.dump, v31)\nif ok32 and type(d32) == [[string]] then\nlocal fn33 = io.open([[D:/DSH/.tools/c112_ii_]] .. k26 .. [[_]] .. tostring(k31) .. [[.bin]], [[wb]])\nif fn33 then\nfn33:write(d32)\nfn33:close()\nend\nend\nend\nend\nend\nend\nend\nend\n-- 112s：全部 UMG/uw 模块名清单（umin:，700 字符分块，上限 20 块）。\nlocal nms34 = {}\nfor k35, v35 in pairs(package.loaded) do\nif type(k35) == [[string]] then\nlocal kl36 = string.lower(k35)\nlocal f37 = string.find(kl36, [[umg]], 1, true) ~= nil\nlocal f38 = string.find(kl36, [[uw_]], 1, true) ~= nil\nif f37 or f38 then\ntable.insert(nms34, k35)\nend\nend\nend\nlocal all39 = [[umin:]] .. table.concat(nms34, [[,]])\nfor seg40 = 0, 19 do\nlocal s41 = string.sub(all39, seg40 * 700 - (-1), (seg40 - (-1)) * 700)\nif s41 ~= [[]] then\nfo1:write(s41)\nfo1:write(string.char(10))\nend\nend\nfo1:close()\nend)\n-- 112u：解锁探针对象修正。112s 用 _G.BagRogue（运行时单例）调\n-- GetOpenConf/GetBagUnlockConfig 全落空——配置函数在 BagRogueConfig\n-- （bgk 键名实锤）。用正确对象重跑并 dump 判断函数本体；顺带 dump\n-- 行前整备（buffformation 系）与图录详情（uw_backpack_riki_talent）\n-- 类方法（bf_ 前缀），反汇编找入口置灰判断。\npcall(function()\nlocal BC = BagRogueConfig\nif type(BC) ~= [[table]] then return end\nlocal fo70 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fo70 == nil then return end\nlocal function serMk2(tm, dm)\nif type(tm) ~= [[table]] then return tostring(tm) end\nif math.min(dm, 3) == 3 then return [[T]] end\nlocal ps87 = {}\nfor k88, v88 in pairs(tm) do\nif math.min(#ps87, 40) ~= 40 then\ntable.insert(ps87, tostring(k88) .. [[=]] .. serMk2(v88, dm - (-1)))\nend\nend\nreturn [[{]] .. table.concat(ps87, [[,]]) .. [[}]]\nend\nlocal bu71 = BC.tbBagUnlock\nif type(bu71) == [[table]] then\nfo70:write([[tbu2:]] .. serMk2(bu71, 0))\nfo70:write(string.char(10))\nend\nlocal bo72 = BC.tbBagUnlockOrder\nif type(bo72) == [[table]] then\nfo70:write([[tbo2:]] .. serMk2(bo72, 0))\nfo70:write(string.char(10))\nend\nfo70:write([[nmx2:]] .. tostring(BC.nMaxBagUnlock))\nfo70:write(string.char(10))\nlocal oc73 = nil\npcall(function() oc73 = BC.GetOpenConf(BC) end)\nif oc73 ~= nil then\nfo70:write([[oc2:]] .. serMk2(oc73, 0))\nfo70:write(string.char(10))\nend\nfor i74 = 0, 3 do\nlocal c75 = nil\npcall(function() c75 = BC.GetBagUnlockConfig(BC, i74) end)\nfo70:write([[guc2:]] .. tostring(i74) .. [[ ]] .. serMk2(c75, 0))\nfo70:write(string.char(10))\nend\n-- 判断函数本体 dump（反汇编看参数与分支）\nfor i76 = 1, 4 do\nlocal nm77 = nil\nif i76 == 1 then nm77 = [[GetBagUnlockConfig]] end\nif i76 == 2 then nm77 = [[CheckOpenConfig]] end\nif i76 == 3 then nm77 = [[GetOpenConf]] end\nif i76 == 4 then nm77 = [[LoadBagUnlockConfig]] end\nlocal fv78 = BC[nm77]\nif type(fv78) == [[function]] then\nlocal ok79, d79 = pcall(string.dump, fv78)\nif ok79 and type(d79) == [[string]] then\nlocal fn80 = io.open([[D:/DSH/.tools/c112_bf_]] .. nm77 .. [[.bin]], [[wb]])\nif fn80 then\nfn80:write(d79)\nfn80:close()\nend\nend\nend\nend\n-- buffformation 系与图录详情类方法 dump（登录时已加载，umin 实锤）\nlocal cl81 = {\n[[UMG.Rogue25.umg_rogue25_buffformation]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buffformation_common]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buffformation_role]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buff_formation_check]],\n[[UMG.Rogue25.Widgets.uw_rogue25_formation_buff_item]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent_list]],\n}\nfor i82 = 1, #cl81 do\nlocal md83 = package.loaded[cl81[i82]]\nif type(md83) == [[table]] then\nfor k84, v84 in pairs(md83) do\nif type(k84) == [[string]] and type(v84) == [[function]] then\nlocal ok85, d85 = pcall(string.dump, v84)\nif ok85 and type(d85) == [[string]] then\nlocal fn86 = io.open([[D:/DSH/.tools/c112_bf_]] .. tostring(i82) .. [[_]] .. k84 .. [[.bin]], [[wb]])\nif fn86 then\nfn86:write(d85)\nfn86:close()\nend\nend\nend\nend\nelse\nfo70:write([[bf missing:]] .. cl81[i82])\nfo70:write(string.char(10))\nend\nend\nfo70:close()\nend)\n-- 115f：旧 GM 段（原 115d/115e 位置，在探针段之后）已上移到\n-- chunk 顶部并加固（单句柄复用、dump 拼接单文件、xpcall 错误\n-- 回写），见 payload 开头。原位置句柄耗尽导致 io.open 全 nil。\n-- 112t：功能屏蔽总开关关闭。CheckItem/CheckOpen/CheckTest 反汇编实锤\n-- bEnable=false 时全部直接放行（CheckOpen return true、CheckItem 不查\n-- GDPL 表）——行前整备等被屏蔽入口一并解锁。幂等守标与旧值日志。\npcall(function()\nlocal FS66 = rawget(_G, [[FunctionShield]])\nif type(FS66) == [[table]] and FS66.bMikuShieldOff ~= true then\nFS66.bMikuShieldOff = true\nlocal was67 = FS66.bEnable\nFS66.bEnable = false\nlocal lf68 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf68 then\nlf68:write([[shield off, bEnable was:]] .. tostring(was67))\nlf68:write(string.char(10))\nlf68:close()\nend\nend\nend)";

	public const string BagRogueRelicUnlock = "local M = BagRogueConfig\nif type(M) == [[table]] and M.bMikuRelicFix ~= true then\nM.bMikuRelicFix = true\nlocal fnOld = M.GetCurTimeConfig\nif type(fnOld) == [[function]] then\nlocal tbPool = {\n{ 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010 },\n{ 1011, 1012, 1013, 1014, 1015, 1016, 1017, 1018, 1019, 1020 },\n{ 1021, 1022, 1023, 1024, 1025, 1026, 1027, 1028, 1029, 1030 },\n}\nM.GetCurTimeConfig = function(...)\nlocal cfg = fnOld(...)\nif type(cfg) == [[table]] then\nlocal ru = cfg.tbRelicUnlock\nif type(ru) ~= [[table]] or #ru == 0 then\ncfg.tbRelicUnlock = tbPool\nend\n-- 112v：行前整备入口解锁。CheckOpenConfig 第三道关实锤\n-- Condition.Check(tbCondition)（{1={1=1,2=30}}，类型1参数30，\n-- error.condition_limit 的来源）。清空条件判定恒过。\ncfg.tbCondition = {}\nend\nreturn cfg\nend\nend\nend";

	public const string BagRogueEndNode = "local function mikuApply()\nlocal M = BagRogue\nif type(M) ~= [[table]] then return end\nif M.bMikuEndNodeFix ~= true then\nM.bMikuEndNodeFix = true\nlocal fnOld = M.IsEndNode\nif type(fnOld) == [[function]] then\nM.IsEndNode = function(self)\nlocal okOld, vOld = pcall(fnOld, self)\nif okOld and vOld then return true end\nlocal nMapId = M.GetCurLevelNode(self)\nlocal nNodeId = M.GetCurWalkNode(self)\nlocal tbMap = BagRogueConfig.tbMapNode[nMapId]\nif type(tbMap) ~= [[table]] then return false end\nlocal cfg = tbMap[nNodeId]\nif type(cfg) ~= [[table]] then return false end\nif cfg.nEventType ~= 5 then return false end\nfor _, other in pairs(tbMap) do\nlocal pres = other.tbPreNode\nif type(pres) == [[table]] then\nfor _, p in pairs(pres) do\nif tonumber(p) == nNodeId then return false end\nend\nend\nend\nreturn true\nend\nend\nend\nif M.bMikuSettleFix ~= true and type(s2c) == [[table]] and type(s2c.tbCmds) == [[table]] then\nlocal fnSettle = s2c.tbCmds.BagRogue_LevelSettlement\nif type(fnSettle) == [[function]] then\nM.bMikuSettleFix = true\ns2c.tbCmds.BagRogue_LevelSettlement = function(tbParam)\npcall(fnSettle, tbParam)\nlocal bEnd = false\nlocal nMap = M.GetCurLevelNode(M)\nif nMap == 0 or nMap == nil then bEnd = true end\nif not bEnd then\nlocal okEnd, vEnd = pcall(M.IsEndNode, M)\nif okEnd and vEnd then bEnd = true end\nend\nif bEnd then\nM.bMikuEnded = true\nM.bClickNode = false\nM.bMikuPick = nil\nelse\nlocal kPick = [[tbRelicChoose]]\nif type(tbParam) == [[table]] and type(tbParam[kPick]) == [[table]] and tbParam[kPick][1] ~= nil then\nM.bMikuPick = true\nend\nend\nend\nend\nend\nif M.bMikuPickHook ~= true and type(UI) == [[table]] then\nM.bMikuPickHook = true\nlocal fnCheck = function()\nif BagRogue.bMikuPick ~= true then return end\nlocal bSettled = true\nif type(UI.IsOpen) == [[function]] then\nlocal okIs, vIs = pcall(UI.IsOpen, [[backpacksettlement]])\nif okIs and vIs then bSettled = false end\nend\nif bSettled then\nBagRogue.bMikuPick = nil\npcall(UI.Open, [[backpackchoose]])\nend\nend\nfor _, fnName in ipairs({[[Close]], [[CloseByName]], [[CloseTop]], [[CloseAll]], [[PopUI]]}) do\nlocal fnRaw = UI[fnName]\nif type(fnRaw) == [[function]] then\nUI[fnName] = function(...)\nlocal rs = table.pack(pcall(fnRaw, ...))\npcall(fnCheck)\nif rs[1] then return table.unpack(rs, 2, rs.n) end\nreturn nil\nend\nend\nend\nend\nend\nmikuApply()\nif _G.bMikuOpenHooked ~= true and type(UI) == [[table]] and type(UI.Open) == [[function]] then\n_G.bMikuOpenHooked = true\nlocal fnOpen = UI.Open\nUI.Open = function(sName, ...)\nmikuApply()\nif type(sName) == [[string]] and type(BagRogue) == [[table]] and BagRogue.bMikuEnded == true and string.lower(sName) == [[backpackmap]] then\nBagRogue.bMikuEnded = nil\nreturn fnOpen([[BackpackMain]])\nend\nreturn fnOpen(sName, ...)\nend\nend";

	public const string BagRogueShopAffix = "local sUI = [[BackpackShop]]\nlocal sLog = [[D:/DSH/.tools/shop_patch_err.txt]]\nlocal function logErr(s)\nlocal f = io.open(sLog, [[a]])\nif f then\nf:write(s)\nf:write(string.char(10))\nf:close()\nend\nend\nlocal function patchShopInst(p)\nif type(p) ~= [[table]] or p.bMikuInstPatch == true then return false end\nlocal orig = p.RefreshShop\nif type(orig) ~= [[function]] then return false end\np.bMikuInstPatch = true\np.RefreshShop = function(self, ...)\nlocal ok0, err0 = xpcall(function()\nlocal walk = BagRogue.GetCurWalkNode and BagRogue:GetCurWalkNode()\nlocal info = walk and BagRogue.GetNodeInfo and BagRogue:GetNodeInfo(walk)\nif type(info) ~= [[table]] or type(info.D) ~= [[table]] then\nerror([[no info]])\nend\nlocal sid = self.nStoreId or info.D[1] or 1\nlocal sc = BagRogueConfig.GetStoreConfig and BagRogueConfig:GetStoreConfig(sid)\npcall(function()\nif sc and sc.nCashId and Cash and Cash.GetMoneyCfgInfo and WidgetUtils and WidgetUtils.SetTexture and self.ImgMoneyIcon then\nlocal mc = Cash:GetMoneyCfgInfo(sc.nCashId)\nif mc and mc.nIcon then\nWidgetUtils.SetTexture(self.ImgMoneyIcon, mc.nIcon)\nend\nend\nend)\npcall(function()\nif sc and self.TxtMoney and WidgetUtils and WidgetUtils.SetText and sc.refreshCost and Cash and Cash.GetStoreCashNum then\nlocal rc = sc.refreshCost\nlocal have = Cash:GetStoreCashNum(sc.nCashId)\nWidgetUtils.SetText(self.TxtMoney, tostring(rc))\nif self.TxtMoney.SetColorFromHex then\nif math.max(have or 0, rc) == rc and have ~= rc then\nself.TxtMoney:SetColorFromHex([[#FF1010FF]])\nelse\nself.TxtMoney:SetColorFromHex([[#FFFFFFFF]])\nend\nend\nend\nend)\npcall(function()\nif self.DoClearListItems then\nself:DoClearListItems(self.ListBuff)\nend\nend)\nif type(self.ListBuff) ~= [[table]] and type(self.ListBuff) ~= [[userdata]] then return end\nfor i = 2, #info.D do\nlocal d = info.D[i]\nlocal cfg = BagRogueConfig.GetJigsawConfig and BagRogueConfig:GetJigsawConfig(d)\nif cfg then\npcall(function()\nlocal item = {}\nitem.tpCfg = cfg\nitem.nType = 3\nitem.idx = i\nitem.bSelled = false\npcall(function()\nlocal sv = info.S\nif type(sv) ~= [[number]] then sv = 0 end\nif GetBits and GetBits(sv, i, i) == 1 then\nitem.bSelled = true\nend\nend)\nlocal st = BagRogue.ShowItemType and BagRogue.ShowItemType.Shop\nif st then\nitem.ShowItemType = st\nend\nlocal key = BagRogue.GetAffixStrId and BagRogue:GetAffixStrId(d)\nlocal ja = nil\nif key and type(info.JA) == [[table]] then\nja = info.JA[key]\nend\nitem.tbAffix = type(ja) == [[table]] and ja or {}\nlocal w = self.Factory:Create(item)\nif w and self.ListBuff.AddItem then\nself.ListBuff:AddItem(w)\nend\nend)\nend\nend\nend, function(e) return debug.traceback(e) end)\nif not ok0 then\nlogErr([[shop rewrite fail ]] .. tostring(err0))\nreturn orig(self, ...)\nend\nreturn nil\nend\nreturn true\nend\nlocal function patchChooseText()\n-- 第 110 节：探索收获选卡卡片上的占位文本（截图实锤 Text Block）。\n-- 根因：原版 uw_backpack_choose_item2.Show 里 TxtNum:SetText(Text BackPackSoldOut)\n-- 把控件字段名当本地化键，键不存在 → 引擎显示 Text Block 占位，\n-- TxtNum 布局紧跟名字后面（视觉上挤掉名字尾巴）。\n-- 实测（probe5）：卡片实例字段是 advCfg；控件须经 GetSubWidget(name) 取；\n-- TxtPropsName=商品名/TxtStoryName=事件名/TxtStory=描述都正常，\n-- BackPackSoldOut 实际是底部按钮文字（选择）——不能动。\n-- 修复：wrap BackpackChoose 实例 SaveWidget（Show 之后必调），fixCard 清空 TxtNum；\n-- 装补丁时已存在的卡片立即修一遍（guard=bMikuChooseFix）。\nif type(UI) ~= [[table]] or type(UI.GetUI) ~= [[function]] then return end\nlocal okP, p = pcall(UI.GetUI, [[BackpackChoose]])\nif not okP or type(p) ~= [[table]] then return end\nif p.bMikuChooseFix == true then return end\np.bMikuChooseFix = true\nlocal function setSub(w, nm, val)\n-- 111c：取控件统一走 GetSubWidget 优先（卡片与面板控件都挂这上面），\n-- 失败再退回字段索引 w[nm]（兼容旧 setPanelText 的取法）。\nlocal sw = nil\nif type(w.GetSubWidget) == [[function]] then\nlocal okg, s2 = pcall(w.GetSubWidget, w, nm)\nif okg then sw = s2 end\nend\nif sw == nil then\nlocal okg2, s3 = pcall(function() return w[nm] end)\nif okg2 then sw = s3 end\nend\nif sw == nil then return end\nlocal oks, sfn = pcall(function() return sw.SetText end)\nif oks and sfn ~= nil then\npcall(sfn, sw, val)\nend\nend\nlocal function dumpKeys(t)\n-- 111c：诊断用，列一个配置表的全部字符串键（写日志找道具描述源）。\nlocal ks = {}\nif type(t) ~= [[table]] then return [[]] end\nfor k, _ in pairs(t) do\nif type(k) == [[string]] then table.insert(ks, k) end\nend\ntable.sort(ks)\nreturn table.concat(ks, [[,]])\nend\nlocal function dumpCfg(t)\n-- 111d：诊断用，列配置表的键值（string 值截 48 字符，其余只写类型）。\n-- 111f：数字键也列（tbOption 是数组，GETI tbOption[1] 实锤）。\nlocal parts = {}\nif type(t) ~= [[table]] then return tostring(t) end\nfor k, v in pairs(t) do\nif type(k) == [[string]] or type(k) == [[number]] then\nif type(v) == [[string]] then\ntable.insert(parts, tostring(k) .. [[=]] .. string.sub(v, 1, 48))\nelseif type(v) == [[number]] or type(v) == [[boolean]] then\n-- 111h：数字/布尔值直接打值（tbOption[1] 的 Id 之前只见类型不见值）。\ntable.insert(parts, tostring(k) .. [[=]] .. tostring(v))\nelse\ntable.insert(parts, tostring(k) .. [[:(]] .. type(v) .. [[)]])\nend\nend\nend\ntable.sort(parts)\nreturn table.concat(parts, [[ | ]])\nend\nlocal function setTitles(w)\n-- 111c：面板标题栏（顶部横幅）设为探索事件名。111 版用 setPanelText 走\n-- p[nm] 字段索引一直没生效（顶部空白实锤），111c 统一走 setSub（GetSubWidget 优先）。\n-- 111s：标题按卡种类区分（用户实锤）：奇器三选一=迷雾的酬谢，\n-- 秘宝三选一=探索收获，其余保持事件名。种类由 fixCard 写 w.bMikuKind。\nlocal ti = nil\nif w.bMikuKind == [[relic]] then\nti = [[迷雾的酬谢]]\nelseif w.bMikuKind == [[jig]] then\nti = [[探索收获]]\nelse\nlocal se = w.advCfg and w.advCfg.sEventName\nif type(se) ~= [[string]] then return end\nlocal oko, tv = pcall(Text, se)\nif oko and tv ~= nil then ti = tostring(tv) end\nend\nif ti == nil then return end\nsetSub(p, [[TxtTitle]], ti)\nsetSub(p, [[TxtStoryTitle]], ti)\nend\nlocal function fixCard(w)\nif type(w) ~= [[table]] or type(w.advCfg) ~= [[table]] then return end\nif type(w.GetSubWidget) ~= [[function]] then return end\nsetSub(w, [[TxtNum]], [[]])\n-- 第 111 节：探索收获卡对齐奇器卡版式（大字名字横幅、真实词条描述、标题栏事件名）。\n-- 卡片实例 w.idx = 拼图 Id（OnOpen Adventure 分支里 item.idx 与 GetAffixStrId 同源），\n-- 由此反推 jigsaw 配置（sName）与节点表 JA 词条（GetAffixConfig 的 sDes 与 tbDesVal）。\nlocal cfg = nil\nif type(w.idx) == [[number]] and type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetJigsawConfig) == [[function]] then\nlocal okc, c = pcall(BagRogueConfig.GetJigsawConfig, BagRogueConfig, w.idx)\nif okc and type(c) == [[table]] and type(c.sName) == [[string]] and c.nSubId ~= nil then\ncfg = c\nend\nend\nif cfg == nil then\n-- 第 111b 节：道具卡（冠冕/火石/炙热炉火，idx 非拼图 Id）走不到上面的分支，\n-- 原版渲染下 TxtPropsName 小字名字压在图标上。修法不依赖配置表：\n-- 读 TxtPropsName 现成文本挪到 TxtStoryName 大字横幅，再清掉小字；\n-- GetText 拿不到（非 string）就原样不动，无损退回现状。\nif w.bMikuItemFix == true then return end\nw.bMikuItemFix = true\nlocal okg, sw = pcall(w.GetSubWidget, w, [[TxtPropsName]])\nif okg and sw ~= nil then\nlocal okh, hfn = pcall(function() return sw.GetText end)\nif okh and hfn ~= nil then\nlocal okv, cur = pcall(hfn, sw)\nlocal lf = io.open(sLog, [[a]])\nif lf then\nlf:write([[choose item card idx=]] .. tostring(w.idx) ..\n[[ name=]] .. tostring(cur) .. [[ t=]] .. type(cur) ..\n[[ tpCfg=]] .. type(w.tpCfg) ..\n[[ tpKeys=]] .. dumpKeys(w.tpCfg) ..\n[[ tbAffix=]] .. type(w.tbAffix) ..\n[[ brc=]] .. dumpKeys(BagRogueConfig))\nlf:write(string.char(10))\nlf:close()\nend\nif okv and type(cur) == [[string]] and cur ~= [[]] then\nsetSub(w, [[TxtStoryName]], cur)\nsetSub(w, [[TxtPropsName]], [[]])\nend\nend\nend\n-- 111f：widget 实锤不持有 item 数据（wcard 只有 advCfg/idx/Object 等），\n-- 描述从 advCfg（探索事件配置）挖：dump 全字段；tbOption[idx] 子表\n-- （三选一每选项独立配置）候选描述字段命中即写 TxtStory。\n-- Cash 是模块闭包 upvalue 不在 _G（money 段 111e 没打出来的根因），\n-- globals 探查加 Cash 名单。\nlocal tried = {}\nlocal hitDesc = false\nlocal ac = w.advCfg\nif type(ac) == [[table]] then\ntable.insert(tried, [[acfg:]] .. dumpCfg(ac))\n-- 111g：名字键实锤是 RelicNameX（含羞草=RelicName1 等），三选项是秘宝体系；\n-- sOptionDesc 三卡同为 MikuDes10（事件级通用文案，翻译结果恰为模板默认值）。\n-- 专属描述走 tbOption[idx]（秘宝 Id 或选项子表）到 GetRelicConfig 取。\nlocal opt = ac.tbOption\n-- 111t：秘宝三选一卡（第 112 节，战斗格 BattleJigsawReward）。\n-- 112c：秘宝卡条目已改挂 OptionType 10（原版选卡 UI 只认 1..10，type 11\n-- 一张卡都不建，弹空白面板）。识别口径=type 10 且卡 Id 前缀 11\n-- （秘宝卡 11011..12275，原版奇器卡 9501..9530 前缀 9 不撞）。\n-- 拼图 Id 段 1011..1275 与奇器 Id 1001..1030 重叠，必须先于奇器路径按\n-- 拼图配置渲染：名字=GetJigsawConfig.sName、图标=SetTexture(nIcon)、\n-- 描述=基础属性、类别=GetJigsawConfig.nTalentType。\n-- 112i：识别口径修正。秘宝卡选项 Id=12011..12275（12000 与拼图 Id 相加），\n-- 千位前缀是 12——112c 写的 sub(nId,1,2)==[[11]] 永远不命中\n-- （探针实锤：卡面名字是 111b 兜底挪的，描述落到通用文案，111t 从未生效）。\n-- 改用 math.floor(nId/1000)==12（禁字符无法用比较运算，除法取整等价）。\n-- 奇器卡 9501..9530 → floor=9 不撞；原版其他 type 10 卡不在 12 段。\nif ac.nOptionType == 11\nor (ac.nOptionType == 10\nand math.floor((tonumber(ac.nId) or 0) / 1000) == 12) then\nw.bMikuKind = [[jig]]\nlocal oidJ = nil\nif type(opt) == [[table]] and type(opt[1]) == [[number]] then\noidJ = opt[1]\nend\nif oidJ ~= nil\nand type(BagRogueConfig.GetJigsawConfig) == [[function]] then\nlocal okJ2, jc = pcall(BagRogueConfig.GetJigsawConfig, BagRogueConfig, oidJ)\ntable.insert(tried, [[jigpick:]] .. tostring(oidJ) .. [[ ]] .. dumpCfg(jc))\nif okJ2 and type(jc) == [[table]] then\nlocal bannerJ = nil\nlocal okN2, nm2 = pcall(Text, jc.sName)\nif okN2 and nm2 ~= nil then bannerJ = tostring(nm2) end\n-- 112c：类别标签（nTalentType → GetTalentTypeConfig[1].sName），\n-- 拼进名字横幅，与奇器卡「含羞草 · 生机」版式对齐。\nif type(jc.nTalentType) == [[number]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nlocal okT3, tc3 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, jc.nTalentType)\nlocal t13 = nil\nif okT3 and type(tc3) == [[table]] then t13 = tc3[1] end\nif type(t13) == [[table]] and type(t13.sName) == [[string]] then\nlocal okX3, tx3 = pcall(Text, t13.sName)\nif okX3 and tx3 ~= nil and tostring(tx3) ~= [[]] then\nif bannerJ == nil then bannerJ = tostring(tx3)\nelse bannerJ = bannerJ .. [[ · ]] .. tostring(tx3) end\nend\nend\nend\nif type(bannerJ) == [[string]] and bannerJ ~= [[]] then\nsetSub(w, [[TxtStoryName]], bannerJ)\nsetSub(w, [[TxtPropsName]], [[]])\nend\nif type(jc.nIcon) == [[number]] then\nlocal okI2, iw2 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okI2 and iw2 ~= nil then\npcall(function() iw2:SetTexture(jc.nIcon) return true end)\nend\nend\n-- 112j：分段组装，模拟商店卡文本版式：\n-- 属性行 / 察刻经验 / 天赋类别行 / 增益效果 / [n] 词条。\n-- 选卡模板只有 TxtStory 一个文本块，用 string.char(10) 换行分节。\nlocal nl2 = string.char(10)\n-- 112m：属性名与数值的连接符（字符四十三），图录版式为「攻击43号字符203」。\nlocal plus2 = string.char(43)\n-- 112k：tbAttribute/tbTalentNum 客户端实为数组子表结构（tal 探针\n-- 实锤 tbTalentNum={1:(table),2:(table)}），兼容两种形态：number\n-- 映射（键=类别Id）或子表（候选键名取类别/数值），同时深一层\n-- dump 子表键值（tale:/atte: 探针），键名猜错时下轮日志可定位。\nlocal partsA = {}\nlocal partsJ = {}\nlocal at2 = jc.tbAttribute or jc.Attribute\nif type(at2) == [[table]] then\nlocal lab2 = {}\nlab2.AttackNum = [[攻击]]\nlab2.DefenceNum = [[防御]]\nlab2.HealthNum = [[最大生命]]\nlab2.nAttackNum = [[攻击]]\nlab2.nDefenceNum = [[防御]]\nlab2.nHealthNum = [[最大生命]]\nlab2.nAttack = [[攻击]]\nlab2.nDefence = [[防御]]\nlab2.nHealth = [[生命]]\nfor k2, v2 in pairs(at2) do\nif type(v2) == [[number]] and lab2[k2] then\ntable.insert(partsA, lab2[k2] .. [[ ]] .. tostring(v2))\nelseif type(v2) == [[table]] then\nlocal dv2 = {}\nfor k5, v5 in pairs(v2) do\ntable.insert(dv2, tostring(k5) .. [[=]] .. tostring(v5))\nend\ntable.insert(tried, [[atte:]] .. tostring(k2) .. [[ ]] .. table.concat(dv2, [[,]]))\n-- 112m：atte 探针实锤子表为数组形态 v2[1]=属性枚举名 v2[2]=数值\n-- （例 1=AttackNum,2=203）。枚举名经 lab2 映射中文，仅三种封闭集合。\nlocal vv2 = v2.nNum or v2.Num or v2.nValue or v2.nCount or v2.nAttributeVal\nlocal nm2v = nil\nif type(v2[1]) == [[string]] and lab2[v2[1]] ~= nil then\nnm2v = lab2[v2[1]]\nif type(v2[2]) == [[number]] then vv2 = v2[2] end\nelse\nlocal tp2 = v2.nType or v2.Type or v2.nAttributeType or v2.nAttributeNum\nif type(v2[1]) == [[number]] then tp2 = v2[1] end\nif type(v2[2]) == [[number]] then vv2 = v2[2] end\ntp2 = tp2 or tonumber(k2)\nif tp2 == 1 then nm2v = [[攻击]] end\nif tp2 == 2 then nm2v = [[防御]] end\nif tp2 == 3 then nm2v = [[最大生命]] end\nend\nif nm2v ~= nil and type(vv2) == [[number]] then\ntable.insert(partsA, nm2v .. plus2 .. tostring(vv2))\nend\nend\nend\nend\n-- 112j：察刻经验区——类别名走 GetTalentTypeConfig(类别Id)[1].sName\n-- （111j 奇器类别同源）。\nlocal tal2 = {}\nlocal ttn2 = jc.tbTalentNum\nif type(ttn2) == [[table]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nfor k4, v4 in pairs(ttn2) do\nlocal tid4 = nil\nlocal val4 = nil\nif type(v4) == [[number]] then\ntid4 = tonumber(k4)\nval4 = v4\nelseif type(v4) == [[table]] then\nlocal dv4 = {}\nfor k5, v5 in pairs(v4) do\ntable.insert(dv4, tostring(k5) .. [[=]] .. tostring(v5))\nend\ntable.insert(tried, [[tale:]] .. tostring(k4) .. [[ ]] .. table.concat(dv4, [[,]]))\n-- 112m：tale 探针实锤子表为数组形态 v4[1]=篆痕类别Id v4[2]=累计层数\n-- （例 1=11,2=3）。类别名走 GetTalentTypeConfig（111c 横幅同源已实锤）。\ntid4 = v4.nTalentType or v4.TalentType or v4.nId or v4.Id\nval4 = v4.nNum or v4.Num or v4.nTalentNum or v4.TalentNum or v4.nValue or v4.nCount\nif type(v4[1]) == [[number]] then tid4 = v4[1] end\nif type(v4[2]) == [[number]] then val4 = v4[2] end\nend\nif tid4 ~= nil and val4 ~= nil then\nlocal nm4 = tostring(tid4)\nlocal ok4, tc4 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, tid4)\nif ok4 and type(tc4) == [[table]] and type(tc4[1]) == [[table]]\nand type(tc4[1].sName) == [[string]] then\nlocal okn4, tn4 = pcall(Text, tc4[1].sName)\nif okn4 and tn4 ~= nil and tostring(tn4) ~= [[]] then\nnm4 = tostring(tn4)\nend\nend\ntable.insert(tal2, nm4 .. [[ ]] .. tostring(val4))\nend\nend\nend\n-- 112i：节点 JA 词条渲染（与商店货架同源同键）。卡面 w.idx 是选项序号\n-- （探针实锤 idx=3），拼图 Id 在 tbOption[1]；JA 键=GetAffixStrId(拼图Id)。\n-- 词条行 [n] xxx 追加在属性后，与商店/图录版式对齐；选定后服务端发放\n-- 同一批 JA 词条进背包，所见即所得。\nlocal jaJ = nil\nif type(BagRogue) == [[table]] and type(BagRogue.GetCurWalkNode) == [[function]]\nand type(BagRogue.GetNodeInfo) == [[function]]\nand type(BagRogue.GetAffixStrId) == [[function]]\nand type(oidJ) == [[number]] then\nlocal okw2, walk2 = pcall(BagRogue.GetCurWalkNode, BagRogue)\nlocal info2 = nil\nif okw2 and walk2 ~= nil then\nlocal oki2, inf2 = pcall(BagRogue.GetNodeInfo, BagRogue, walk2)\nif oki2 then info2 = inf2 end\nend\nif type(info2) == [[table]] and type(info2.JA) == [[table]] then\nlocal okk2, key2b = pcall(BagRogue.GetAffixStrId, BagRogue, oidJ)\nif okk2 and type(key2b) == [[string]] then jaJ = info2.JA[key2b] end\nend\nend\ntable.insert(tried, [[jaJ:]] .. type(jaJ) .. [[/]] .. (jaJ and #jaJ or 0))\nif type(jaJ) == [[table]] then\nfor n2 = 1, #jaJ do\nif type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal oka2, ac3 = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, jaJ[n2])\nif oka2 and type(ac3) == [[table]] and type(ac3.sDes) == [[string]] then\nlocal okt2, tx2 = pcall(Text, ac3.sDes, table.unpack(ac3.tbDesVal or {}))\nif okt2 and tx2 ~= nil then\ntable.insert(partsJ, string.format([[[%d] ]], n2) .. tostring(tx2))\nend\nend\nend\nend\nend\n-- 112j：商店卡同款文本版式——属性 / 篆痕行 / 增益效果 / 词条。\n-- 112m：tbTalentNum 实锤为篆痕类别与层数（talent.json TalentType），\n-- 标题由「察刻经验」改为「篆痕」（图录 tab 名 Other_ManualTab1 同词）。\nlocal ekao2 = nil\nlocal zeng2 = nil\nif type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nekao2 = utf8.char(0x7BC6, 0x75D5)\nzeng2 = utf8.char(0x589E, 0x76CA, 0x6548, 0x679C)\nend\nlocal out2 = {}\nfor _, s7 in ipairs(partsA) do\ntable.insert(out2, s7)\nend\nif #tal2 ~= 0 and ekao2 ~= nil then\ntable.insert(out2, ekao2)\nfor _, s7 in ipairs(tal2) do\ntable.insert(out2, s7)\nend\nend\nif #partsJ ~= 0 and zeng2 ~= nil then\ntable.insert(out2, zeng2)\nfor _, s7 in ipairs(partsJ) do\ntable.insert(out2, s7)\nend\nend\nif #out2 ~= 0 then\nsetSub(w, [[TxtStory]], table.concat(out2, nl2))\nhitDesc = true\nend\n-- 112m：dump 商店货架 item widget（全局 rogue25 系列）的原版显示函数\n-- 字节码，供属性行与篆痕行版式精确比对。函数名字拼接不含禁字符。\npcall(function()\nfor gk, gv in pairs(_G) do\nif type(gk) == [[string]] and string.find(gk, [[rogue25]], 1, true) ~= nil\nand type(gv) == [[table]] then\nfor mk, mv in pairs(gv) do\nif type(mv) == [[function]] then\nlocal okd2, d2 = pcall(string.dump, mv)\nif okd2 and type(d2) == [[string]] and math.min(#d2, 201) == 201 then\nlocal f9 = io.open([[D:/DSH/.tools/rg25dump_]] .. gk .. [[_]] .. tostring(mk) .. [[.bin]], [[wb]])\nif f9 ~= nil then\nf9:write(d2)\nf9:close()\nend\nend\nend\nend\nend\nend\nend)\nend\nend\nelseif type(opt) == [[table]] and type(w.idx) == [[number]] then\ntable.insert(tried, [[topt:]] .. dumpCfg(opt))\n-- 111h：tbOption 实锤只有 1 个元素（topt:1:(number)），原版名字分支\n-- GETI C=1 取 tbOption[1]——Id 在 [1]，不在 [w.idx]。\nlocal oid = nil\nif type(opt[1]) == [[number]] then\noid = opt[1]\nelseif type(opt[w.idx]) == [[number]] then\noid = opt[w.idx]\nend\nif oid ~= nil\nand type(BagRogueConfig.GetRelicConfig) == [[function]] then\nlocal okR, rc = pcall(BagRogueConfig.GetRelicConfig, BagRogueConfig, oid)\ntable.insert(tried, [[relic]] .. tostring(oid) .. [[:]] .. dumpCfg(rc))\nif type(rc) == [[table]] then\n-- 111s：奇器卡种类标记（标题=迷雾的酬谢）与显式 SetTexture(rc.nIcon)\n-- 覆盖原版图标源（首卡炙热炉火图标显示错误，配置 Id 1001444 与\n-- advCfg.nOptionIcon 一致但画面是别的东西——原版 type 10 分支可能\n-- 从别的表取了图标；111p 实证 ImgIcon:SetTexture 可用）。\nw.bMikuKind = [[relic]]\nif type(rc.nIcon) == [[number]] then\nlocal okI9, iw9 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okI9 and iw9 ~= nil then\nlocal okS9 = pcall(function() iw9:SetTexture(rc.nIcon) return true end)\nif okS9 ~= true then\nokS9 = pcall(function() return _G.SetTexture(iw9, rc.nIcon) end)\nend\ntable.insert(tried, [[seticon:]] .. tostring(rc.nIcon) ..\n[[/]] .. tostring(okS9))\nend\nend\nlocal names = {[[sDes]], [[sDesc]], [[sIntroduce]], [[sText]],\n[[sStory]], [[sTip]], [[sOptionDesc]]}\nfor _, nn in ipairs(names) do\nif type(rc[nn]) == [[string]] and rc[nn] ~= [[]] then\nlocal okT, tv = pcall(Text, rc[nn], table.unpack(rc.tbDesVal or {}))\nif okT and tv ~= nil and tostring(tv) ~= [[]] then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:relic.]] .. nn ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nbreak\nend\nend\nend\n-- 111i：relic 配置实锤没有描述字段（只有 sName/nAffix/nIcon 等），\n-- 专属效果描述在词条表：rc.nAffix（实锤 620）→ GetAffixConfig 的\n-- sDes 与 tbDesVal（111 秘宝卡同源）。\nif hitDesc ~= true and type(rc.nAffix) == [[number]]\nand type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal okA, ac2 = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, rc.nAffix)\ntable.insert(tried, [[affix:]] .. dumpCfg(ac2))\nif okA and type(ac2) == [[table]] and type(ac2.sDes) == [[string]]\nand ac2.sDes ~= [[]] then\nlocal okT, tv = pcall(Text, ac2.sDes, table.unpack(ac2.tbDesVal or {}))\nif okT and tv ~= nil and tostring(tv) ~= [[]] then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:affix.]] .. tostring(rc.nAffix) ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nend\nend\nend\nend\nelseif type(oid) == [[table]] then\ntable.insert(tried, [[opt:]] .. dumpCfg(oid))\nlocal names = {[[sDes]], [[sDesc]], [[sOptionDesc]], [[sText]],\n[[sStory]], [[sIntroduce]], [[sTip]]}\nfor _, nn in ipairs(names) do\nif type(oid[nn]) == [[string]] and oid[nn] ~= [[]] then\nlocal okT, tv = pcall(Text, oid[nn], table.unpack(oid.tbDesVal or {}))\nif okT and tv ~= nil then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:opt.]] .. nn ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nbreak\nend\nend\nend\nend\nend\nif hitDesc ~= true and type(ac.sOptionDesc) == [[string]]\nand ac.sOptionDesc ~= [[]] then\nlocal okT, tv = pcall(Text, ac.sOptionDesc, table.unpack(ac.tbOption or {}))\nif okT and tv ~= nil then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:acfg.sOptionDesc]])\nhitDesc = true\nend\nend\nend\nif type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetCurTimeConfig) == [[function]]\nand type(Cash) == [[table]] and type(Cash.GetMoneyCfgInfo) == [[function]] then\nlocal okC, tc = pcall(BagRogueConfig.GetCurTimeConfig, BagRogueConfig)\nif okC and type(tc) == [[table]] and tc.nKeyItem ~= nil then\nlocal okM, mc = pcall(Cash.GetMoneyCfgInfo, Cash, tc.nKeyItem)\ntable.insert(tried, [[money:]] .. dumpCfg(mc))\nend\nelse\ntable.insert(tried, [[cashnil:]] .. type(Cash))\nend\ntable.insert(tried, [[wcard:]] .. dumpCfg(w))\nlocal globals = {}\nfor k, v in pairs(_G) do\nif type(k) == [[string]] and type(v) == [[table]] then\nif string.find(k, [[Item]]) ~= nil or string.find(k, [[Prop]]) ~= nil\nor string.find(k, [[Cash]]) ~= nil then\ntable.insert(globals, k)\nend\nend\nend\ntable.sort(globals)\n-- 111k：白底板探查。选卡 nOptionType=10 不匹配 Show 的任何图标分支，\n-- 金色图标另有机制；白色底板是模板静态控件。dump ImgIcon 自身字段，\n-- 按候选名探测底板控件（下一轮按探测结果隐藏/换图）。\nlocal okG5, si = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5 and si ~= nil then\ntable.insert(tried, [[icon:]] .. dumpCfg(si))\nend\ndo\nlocal bgnames = {[[ImgIconBg]], [[ImgBg]], [[ImgDi]], [[ImgKuang]],\n[[ImgQuality]], [[PanelIcon]], [[ImgBase]], [[ImgItemBg]],\n[[ImgIconBg1]], [[ImgIconBg2]], [[ImgBox]], [[ImgWhite]], [[ImgIconDi]]}\nfor _, bn in ipairs(bgnames) do\nlocal okg6, s6 = pcall(w.GetSubWidget, w, bn)\nif okg6 and s6 ~= nil then\ntable.insert(tried, [[bgfound:]] .. bn .. [[:]] .. dumpCfg(s6))\n-- 111m：反汇编实锤原版隐藏控件用 WidgetUtils.Collapsed(w)。\nif bn == [[ImgBg]] then\nlocal okw7, wu7 = pcall(function() return WidgetUtils end)\nlocal okc7 = false\nif okw7 and type(wu7) == [[table]]\nand type(wu7.Collapsed) == [[function]] then\nokc7 = pcall(wu7.Collapsed, s6)\nend\ntable.insert(tried, [[bghide:]] .. tostring(okc7))\nend\nend\nend\nend\n-- 111n：白块不是 ImgBg（collapsed:true 但画面无变化）。父链探查与\n-- 候选名 v2，找到即 Collapsed（bg 名单隐藏无损），日志记父名。\ndo\nlocal function parentName(sw)\nlocal okp, pf = pcall(function() return sw.GetParent end)\nif okp and pf ~= nil then\nlocal okp2, pw = pcall(pf, sw)\nif okp2 and pw ~= nil then\nlocal okn2, nf = pcall(function() return pw.GetName end)\nif okn2 and nf ~= nil then\nlocal okn3, nm2 = pcall(nf, pw)\nreturn tostring(nm2)\nend\nend\nend\nreturn [[-]]\nend\nlocal okG5b, si5 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5b and si5 ~= nil then\ntable.insert(tried, [[iparent:]] .. parentName(si5))\nend\nlocal bgn2 = {[[ImageBg]], [[ImgBG]], [[Bg]], [[BgIcon]], [[IconBg]],\n[[ImgIconBack]], [[ImgBack]], [[ImgDiBan]], [[ImgSlot]], [[ImgGrid]],\n[[ImgCell]], [[ImgSquare]], [[ImgRect]], [[ImgPaper]], [[ImgCard]],\n[[WhiteBg]], [[ImgWhiteBg]], [[BgWhite]], [[ImgBaseBg]],\n[[ImgZheZhao]], [[ImgLight]], [[ImgGlow]], [[ImgUnder]]}\nfor _, bn2 in ipairs(bgn2) do\nlocal okg8, s8 = pcall(w.GetSubWidget, w, bn2)\nif okg8 and s8 ~= nil then\nlocal hid = false\nlocal okw8, wu8 = pcall(function() return WidgetUtils end)\nif okw8 and type(wu8) == [[table]]\nand type(wu8.Collapsed) == [[function]] then\nhid = pcall(wu8.Collapsed, s8)\nend\ntable.insert(tried, [[bg2:]] .. bn2 .. [[/]] .. tostring(hid) ..\n[[/p=]] .. parentName(s8))\nend\nend\nend\n-- 111o：iparent 实锤 ImgIcon 的父容器是 PanelStory，白块是其兄弟\n-- 控件。枚举 PanelStory 子控件（GetChildrenCount/GetChildAt），\n-- 名字含 Img/Image 且非 ImgIcon 的 Collapsed，全部落日志。\ndo\nlocal okG5c, si6 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5c and si6 ~= nil then\nlocal okp9, pf9 = pcall(function() return si6.GetParent end)\nif okp9 and pf9 ~= nil then\nlocal okp10, pw9 = pcall(pf9, si6)\nif okp10 and pw9 ~= nil then\nlocal okcc, cf9 = pcall(function() return pw9.GetChildrenCount end)\nlocal okca, caf9 = pcall(function() return pw9.GetChildAt end)\nif okcc and cf9 ~= nil and okca and caf9 ~= nil then\nlocal okn4, cnt = pcall(cf9, pw9)\ncnt = tonumber(cnt) or 0\ntable.insert(tried, [[pstorycnt:]] .. tostring(cnt))\nfor i = 0, cnt - 1 do\nlocal okch, chw = pcall(caf9, pw9, i)\nif okch and chw ~= nil then\nlocal nmx = [[-]]\nlocal oknf2, nf2 = pcall(function() return chw.GetName end)\nif oknf2 and nf2 ~= nil then\nlocal okn5, nm3 = pcall(nf2, chw)\nnmx = tostring(nm3)\nend\ntable.insert(tried, [[child]] .. tostring(i) .. [[:]] .. nmx)\nif nmx ~= [[ImgIcon]]\nand (string.find(nmx, [[Img]]) ~= nil\nor string.find(nmx, [[Image]]) ~= nil) then\nlocal okh9, wu9 = pcall(function() return WidgetUtils end)\nlocal hid9 = false\nif okh9 and type(wu9) == [[table]]\nand type(wu9.Collapsed) == [[function]] then\nhid9 = pcall(wu9.Collapsed, chw)\nend\ntable.insert(tried, [[hid]] .. tostring(i) .. [[:]] .. tostring(hid9))\nend\nend\nend\nend\nend\nend\nend\nend\n-- 111q：白块根因修正。type 10 不走 Show 的任何面板折叠分支，而奇器卡\n-- （type 2）会 Collapsed PanelPath 与 PanelRandom——白块在这两个面板里\n-- （bg2 探到的 Bg 父容器正是 PanelPath）。补齐同款折叠，PanelStory\n-- （描述区）保持可见。\ndo\nlocal okwq, wuq = pcall(function() return WidgetUtils end)\nif okwq and type(wuq) == [[table]]\nand type(wuq.Collapsed) == [[function]] then\nlocal pns = {[[PanelPath]], [[PanelRandom]]}\nfor _, pn in ipairs(pns) do\nlocal okpq, pwq = pcall(w.GetSubWidget, w, pn)\nif okpq and pwq ~= nil then\nlocal okcq = pcall(wuq.Collapsed, pwq)\ntable.insert(tried, [[panel:]] .. pn .. [[/]] .. tostring(okcq))\nend\nend\nend\nend\nlocal lfd = io.open(sLog, [[a]])\nif lfd then\nlfd:write([[choose desc idx=]] .. tostring(w.idx) .. [[ ]] ..\ntable.concat(tried, [[ ]]) .. [[ globals=]] .. table.concat(globals, [[,]]))\nlfd:write(string.char(10))\nlfd:close()\nend\n-- 111j：类别标签。图录奇器实锤有类别（飓风→风雪），数据链\n-- tbOption[1] → GetRelicConfig.nTalentType → GetTalentTypeConfig。\n-- 卡模板没有独立标签控件（Show 反汇编全控件清单无），拼进名字横幅：\n-- 「含羞草 · 生机」。string.find 纯文本比较防重复拼。\n-- 112c：拼图卡（bMikuKind=jig）类别已在 111t 分支按 GetJigsawConfig 处理，\n-- 且拼图 Id 1011..1030 与奇器 Id 撞表（GetRelicConfig 会命中错的奇器），这里跳过。\nif w.bMikuKind ~= [[jig]]\nand type(ac) == [[table]] and type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetRelicConfig) == [[function]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nlocal o2 = ac.tbOption\nif type(o2) == [[table]] and type(o2[1]) == [[number]] then\nlocal okR2, rc2 = pcall(BagRogueConfig.GetRelicConfig, BagRogueConfig, o2[1])\nif okR2 and type(rc2) == [[table]] and type(rc2.nTalentType) == [[number]] then\nlocal okT2, tc2 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, rc2.nTalentType)\nif okT2 and type(tc2) == [[table]] then\n-- 111l：类别配置全字段落日志，确认 sName/sTypeName 取名是否命中。\nlocal lft = io.open(sLog, [[a]])\nif lft then\n-- 111m：GetTalentTypeConfig 返回 {1..5:子表} 索引表，dump 子表找类别名键。\nlocal sub1 = tc2[1]\nlocal sub2 = tc2[2]\nlft:write([[choose talent idx=]] .. tostring(w.idx) .. [[ ]] ..\ndumpCfg(tc2) .. [[ SUB1:]] .. dumpCfg(sub1) ..\n[[ SUB2:]] .. dumpCfg(sub2))\nlft:write(string.char(10))\nlft:close()\nend\nlocal tn = nil\n-- 111n：GetTalentTypeConfig 实锤返回 {1..5:等级子表}，类别名在子表\n-- sName（日志 SUB1 实锤 sName=bagrogue.TalentName10）。\nlocal t1c = tc2[1]\nif type(t1c) == [[table]] then\ntn = t1c.sName\nif type(tn) ~= [[string]] then tn = t1c.sTypeName end\nend\nif type(tn) ~= [[string]] then tn = tc2.sName end\nif type(tn) ~= [[string]] then tn = tc2.sTypeName end\nif type(tn) == [[string]] and tn ~= [[]] then\nlocal okN, tvn = pcall(Text, tn)\nif okN and tvn ~= nil and tostring(tvn) ~= [[]] then\nlocal okG3, sw3 = pcall(w.GetSubWidget, w, [[TxtStoryName]])\nif okG3 and sw3 ~= nil then\nlocal okH3, h3 = pcall(function() return sw3.GetText end)\nif okH3 and h3 ~= nil then\nlocal okV3, cur3 = pcall(h3, sw3)\nif okV3 and type(cur3) == [[string]] and cur3 ~= [[]]\nand string.find(cur3, tostring(tvn), 1, true) == nil then\nsetSub(w, [[TxtStoryName]], cur3 .. [[ · ]] .. tostring(tvn))\nend\nend\nend\nend\nend\nend\nend\nend\nend\nsetTitles(w)\nreturn\nend\nlocal okn, nm = pcall(Text, cfg.sName)\n-- 111s：秘宝卡种类标记（标题=探索）。\nw.bMikuKind = [[jig]]\nif okn and nm ~= nil then\nsetSub(w, [[TxtStoryName]], tostring(nm))\nend\nsetSub(w, [[TxtPropsName]], [[]])\nlocal lines = {}\nlocal ja = nil\nif type(BagRogue) == [[table]] and type(BagRogue.GetCurWalkNode) == [[function]]\nand type(BagRogue.GetNodeInfo) == [[function]]\nand type(BagRogue.GetAffixStrId) == [[function]] then\nlocal okw, walk = pcall(BagRogue.GetCurWalkNode, BagRogue)\nlocal info = nil\nif okw and walk ~= nil then\nlocal oki, inf = pcall(BagRogue.GetNodeInfo, BagRogue, walk)\nif oki then info = inf end\nend\nif type(info) == [[table]] and type(info.JA) == [[table]] then\nlocal okk, key = pcall(BagRogue.GetAffixStrId, BagRogue, w.idx)\nif okk and type(key) == [[string]] then ja = info.JA[key] end\nend\nend\nif type(ja) == [[table]] then\nfor n = 1, #ja do\nif type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal oka, ac = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, ja[n])\nif oka and type(ac) == [[table]] and type(ac.sDes) == [[string]] then\nlocal okt, tx = pcall(Text, ac.sDes, table.unpack(ac.tbDesVal or {}))\nif okt and tx ~= nil then\ntable.insert(lines, string.format([[[%d] ]], n) .. tostring(tx))\nend\nend\nend\nend\nend\nif #lines ~= 0 then\nsetSub(w, [[TxtStory]], table.concat(lines, string.char(10)))\nend\nsetTitles(w)\nend\nif type(p.SaveWidget) == [[function]] then\nlocal origSave = p.SaveWidget\np.SaveWidget = function(s2, key, w, ...)\nlocal r = origSave(s2, key, w, ...)\nfixCard(w)\nreturn r\nend\nend\nlocal function scanCards(t)\n-- 三层固定展开（面板 tbWidget[3][i] 结构），不用加法（禁字符）。\nif type(t) ~= [[table]] then return end\nfor _, v in pairs(t) do\nif type(v) == [[table]] then\nif type(v.advCfg) == [[table]] then\nfixCard(v)\nelse\nfor _, v2 in pairs(v) do\nif type(v2) == [[table]] then\nif type(v2.advCfg) == [[table]] then\nfixCard(v2)\nelse\nfor _, v3 in pairs(v2) do\nif type(v3) == [[table]] and type(v3.advCfg) == [[table]] then\nfixCard(v3)\nend\nend\nend\nend\nend\nend\nend\nend\nend\nscanCards(p)\n-- 112c：选卡链路函数字节码 dump 与 ShowAdvChoose 参数探针。\n-- GM /lua 通道失效（NtfScript 客户端不执行），改由补丁在客户端落盘：\n-- BackpackChoose 实例/元表、BackpackMap 实例/元表、uw_backpack_choose_item2\n-- 模块的全部函数 dump 到 D:/DSH/.tools/c112_*.bin，供本地 luac54 反汇编；\n-- 并 wrap 地图面板 ShowAdvChoose 记录 D 全量（点节点时触发）。\n-- 每客户端会话只做一次（guard=BagRogue.bMikuChooseDump）。\ndo\nlocal brD = BagRogue\nif type(brD) == [[table]] and brD.bMikuChooseDump ~= true then\nbrD.bMikuChooseDump = true\nlocal function dumpFn(fn, nm)\nif type(fn) ~= [[function]] then return end\nlocal okd, chunk = pcall(string.dump, fn)\nif okd and type(chunk) == [[string]] then\nlocal f = io.open([[D:/DSH/.tools/c112_]] .. nm .. [[.bin]], [[wb]])\nif f then f:write(chunk) f:close() end\nend\nend\ndumpFn(p.Show, [[p_Show]])\ndumpFn(p.OnOpen, [[p_OnOpen]])\nlocal mt = getmetatable(p)\nif type(mt) == [[table]] then\nlocal ix = mt.__index\nif type(ix) == [[table]] then\nfor k5, v5 in pairs(ix) do\nif type(k5) == [[string]] and type(v5) == [[function]] then\ndumpFn(v5, [[cp_]] .. k5)\nend\nend\nelseif type(ix) == [[function]] then\ndumpFn(ix, [[mt_index_fn]])\nend\nend\nlocal okM, mp = pcall(UI.GetUI, [[BackpackMap]])\nif okM and type(mp) == [[table]] then\ndumpFn(mp.ShowAdvChoose, [[map_ShowAdvChoose]])\nlocal mmt = getmetatable(mp)\nif type(mmt) == [[table]] and type(mmt.__index) == [[table]] then\nfor k6, v6 in pairs(mmt.__index) do\nif type(k6) == [[string]] and type(v6) == [[function]] then\ndumpFn(v6, [[cm_]] .. k6)\nend\nend\nend\nif type(mp.ShowAdvChoose) == [[function]] then\nlocal oldSAC = mp.ShowAdvChoose\nmp.ShowAdvChoose = function(s3, d3, ja3, ...)\nlocal lfp = io.open(sLog, [[a]])\nif lfp then\nlfp:write([[sac dType=]] .. type(d3))\nif type(d3) == [[table]] then\nlfp:write([[ dLen=]] .. tostring(#d3))\nfor i5 = 1, 3 do\nlocal e5 = d3[i5]\nif type(e5) == [[table]] then\nlfp:write([[ e]] .. i5 .. [[ v1=]] .. tostring(e5[1])\n.. [[ v2=]] .. tostring(e5[2]))\nend\nend\nend\nlfp:write([[ ja=]] .. type(ja3))\nlfp:write(string.char(10))\nlfp:close()\nend\nreturn oldSAC(s3, d3, ja3, ...)\nend\nend\nend\n-- 112d：Adventure 分支守卫探针。反汇编实锤 OnOpen 建卡前有两道守卫\n-- （查不到配置就静默跳过，0 卡 0 报错），直接探配置查找函数对\n-- 奇器卡 9515 与秘宝卡 12033 的返回，并 wrap OnOpen/GetWidget/\n-- SaveWidget 记录实参与建卡调用链。\nlocal cfgT = BagRogueConfig\nif type(cfgT) == [[table]] then\nlocal ids = {1033, 1034, 1035, 1011, 1275, 12033}\nfor i9 = 1, 6 do\nlocal pid = ids[i9]\nlocal okA, aA = pcall(cfgT.GetAdventureConfig, cfgT, pid)\nif okA and type(aA) == [[table]] then\nlogErr([[gac:]] .. tostring(pid) .. [[ nId=]] .. tostring(aA.nId)\n.. [[ name=]] .. tostring(aA.sOptionName) .. [[ icon=]] .. tostring(aA.nOptionIcon))\nelse\nlogErr([[gac:]] .. tostring(pid) .. [[ nil]])\nend\nlocal okJ, aJ = pcall(cfgT.GetJigsawConfig, cfgT, pid)\nif okJ and type(aJ) == [[table]] then\nlogErr([[gjc:]] .. tostring(pid) .. [[ nId=]] .. tostring(aJ.nId)\n.. [[ sub=]] .. tostring(aJ.nSubId) .. [[ name=]] .. tostring(aJ.sName)\n.. [[ icon=]] .. tostring(aJ.nIcon))\nelse\nlogErr([[gjc:]] .. tostring(pid) .. [[ nil]])\nend\nend\nlocal tj = cfgT.tbJigsaw\nif type(tj) == [[table]] then\nlocal allk = {}\nfor k8, v8 in pairs(tj) do\nlocal s8 = tostring(k8) .. [[=]] .. tostring(type(v8))\nif type(v8) == [[table]] then\ns8 = s8 .. [[/nId=]] .. tostring(v8.nId) .. [[/sub=]] .. tostring(v8.nSubId)\nend\ntable.insert(allk, s8)\nend\nlocal head = {}\nfor i9 = 1, 10 do\nhead[i9] = allk[i9] or [[]]\nend\nlogErr([[tbJigsaw cnt=]] .. tostring(#allk) .. [[ ]] .. table.concat(head, [[ ]]))\nelse\nlogErr([[tbJigsaw nil]])\nend\nend\nif type(p) == [[table]] and type(p.OnOpen) == [[function]] then\nlocal ooO = p.OnOpen\np.OnOpen = function(pp, a1, a2, a3, a4)\nlogErr([[oo:1=]] .. dumpCfg(a1) .. [[ |2=]] .. dumpCfg(a2)\n.. [[ |3=]] .. dumpCfg(a3) .. [[ |4=]] .. dumpCfg(a4))\nreturn ooO(pp, a1, a2, a3, a4)\nend\nif type(p.GetWidget) == [[function]] then\nlocal gwO = p.GetWidget\np.GetWidget = function(pp, t9)\nlogErr([[gw:]] .. dumpCfg(t9))\nreturn gwO(pp, t9)\nend\nend\nif type(p.SaveWidget) == [[function]] then\nlocal swO = p.SaveWidget\np.SaveWidget = function(pp, t9, w9)\nlogErr([[sw:]] .. dumpCfg(t9) .. [[ w=]] .. tostring(w9))\nreturn swO(pp, t9, w9)\nend\nend\nend\nlocal key2 = [[UMG.Backpack.Widgets.uw_backpack_choose_item2]]\nlocal mod2 = package.loaded[key2]\nif type(mod2) == [[table]] then\nfor k7, v7 in pairs(mod2) do\nif type(k7) == [[string]] and type(v7) == [[function]] then\ndumpFn(v7, [[ci_]] .. k7)\nend\nend\nend\n-- 112h：图录面板（BackpackRiki）探针。图录里秘宝「增益效果」全显示问号——\n-- dump 图录模块与实例函数拿字节码；wrap UI.OpenInner 在图录打开时抓实例。\nlocal key3 = [[UMG.Backpack.Widgets.uw_backpack_riki]]\nlocal mod3 = package.loaded[key3]\nif type(mod3) == [[table]] then\nfor kA, vA in pairs(mod3) do\nif type(kA) == [[string]] and type(vA) == [[function]] then\ndumpFn(vA, [[rk_]] .. kA)\nend\nend\nend\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump ~= true then\nUI.bMikuRikiDump = true\nlocal oldOI = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r1 = table.pack(oldOI(nm, ...))\nif nm == [[BackpackRiki]] and UI.bMikuRikiDumped ~= true then\nUI.bMikuRikiDumped = true\nlocal okR, rp = pcall(UI.GetUI, [[BackpackRiki]])\nif okR and type(rp) == [[table]] then\ndumpFn(rp.OnOpen, [[ri_OnOpen]])\ndumpFn(rp.Show, [[ri_Show]])\nlocal rmt = getmetatable(rp)\nif type(rmt) == [[table]] and type(rmt.__index) == [[table]] then\nfor kR, vR in pairs(rmt.__index) do\nif type(kR) == [[string]] and type(vR) == [[function]] then\ndumpFn(vR, [[cr_]] .. kR)\nend\nend\nend\nend\nend\nreturn table.unpack(r1, 1, r1.n)\nend\nend\n-- 112n：图录「？」详情函数补抓。box OnSelectChange 实锤详情渲染走\n-- self.ItemInfo:Show({tpCfg=item.info, nType=ShowItemType.Riki})，\n-- ItemInfo 类不在 uw_backpack_riki 模块表。两路补抓：一 wrap OpenInner\n-- 在图录打开时遍历页实例子 widget（含 ItemInfo）及其元表函数；二同一\n-- 时机枚举 package.loaded 全部 backpack 模块的函数。独立标志防旧\n-- guard（bMikuRikiDump/bMikuRikiDumped 跨重登持续）拦截。\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump3 ~= true then\nUI.bMikuRikiDump3 = true\nlocal oldOI3 = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r3 = table.pack(oldOI3(nm, ...))\nif nm == [[BackpackRiki]] and UI.bMikuRikiDumped2 ~= true then\nUI.bMikuRikiDumped2 = true\nlocal okR3, rp3 = pcall(UI.GetUI, [[BackpackRiki]])\nif okR3 and type(rp3) == [[table]] then\nfor kF, vF in pairs(rp3) do\nif type(kF) == [[string]] and type(vF) == [[table]] then\nfor kR3, vR3 in pairs(vF) do\nif type(kR3) == [[string]] and type(vR3) == [[function]] then\ndumpFn(vR3, [[cf_]] .. kF .. [[_]] .. kR3)\nend\nend\nlocal rmt3 = getmetatable(vF)\nif type(rmt3) == [[table]] and type(rmt3.__index) == [[table]] then\nfor kR4, vR4 in pairs(rmt3.__index) do\nif type(kR4) == [[string]] and type(vR4) == [[function]] then\ndumpFn(vR4, [[cx_]] .. kF .. [[_]] .. kR4)\nend\nend\nend\nend\nend\nend\nfor mk5, mv5 in pairs(package.loaded) do\nif type(mk5) == [[string]] and string.find(mk5, [[backpack]], 1, true) ~= nil\nand type(mv5) == [[table]] then\nfor mk6, mv6 in pairs(mv5) do\nif type(mk6) == [[string]] and type(mv6) == [[function]] then\ndumpFn(mv6, [[pk_]] .. mk5 .. [[_]] .. mk6)\nend\nend\nend\nend\nend\nreturn table.unpack(r3, 1, r3.n)\nend\nend\n-- 112o：nm==BackpackRiki 从未命中（ri_/cr_ 零产物），图录 OpenInner\n-- 注册名未知。放宽触发：面板名记录（oi: 前 30 个）与名字含 ackpack\n-- 的面板打开即 dump（页实例子 widget 与 backpack 模块全集）。\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump4 ~= true then\nUI.bMikuRikiDump4 = true\nlocal oldOI4 = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r4 = table.pack(oldOI4(nm, ...))\nlocal cnt4 = tonumber(UI.bMikuOICnt) or 0\nif math.min(cnt4, 30) ~= 30 then\nUI.bMikuOICnt = cnt4 - (-1)\nlogErr([[oi:]] .. tostring(nm))\nend\nif type(nm) == [[string]] and string.find(nm, [[ackpack]], 1, true) ~= nil\nand UI.bMikuRikiDumped2 ~= true then\nUI.bMikuRikiDumped2 = true\nlocal rp4 = nil\nlocal okR4, rpA = pcall(UI.GetUI, nm)\nif okR4 and type(rpA) == [[table]] then rp4 = rpA end\nif rp4 == nil then\nlocal okR5, rpB = pcall(UI.GetUI, [[BackpackRiki]])\nif okR5 and type(rpB) == [[table]] then rp4 = rpB end\nend\nif type(rp4) == [[table]] then\nfor kF4, vF4 in pairs(rp4) do\nif type(kF4) == [[string]] and type(vF4) == [[table]] then\nfor kR5, vR5 in pairs(vF4) do\nif type(kR5) == [[string]] and type(vR5) == [[function]] then\ndumpFn(vR5, [[cf_]] .. kF4 .. [[_]] .. kR5)\nend\nend\nlocal rmt4 = getmetatable(vF4)\nif type(rmt4) == [[table]] and type(rmt4.__index) == [[table]] then\nfor kR6, vR6 in pairs(rmt4.__index) do\nif type(kR6) == [[string]] and type(vR6) == [[function]] then\ndumpFn(vR6, [[cx_]] .. kF4 .. [[_]] .. kR6)\nend\nend\nend\nend\nend\nend\nfor mk7, mv7 in pairs(package.loaded) do\nif type(mk7) == [[string]] and string.find(mk7, [[backpack]], 1, true) ~= nil\nand type(mv7) == [[table]] then\nfor mk8, mv8 in pairs(mv7) do\nif type(mk8) == [[string]] and type(mv8) == [[function]] then\ndumpFn(mv8, [[pk_]] .. mk7 .. [[_]] .. mk8)\nend\nend\nend\nend\nend\nreturn table.unpack(r4, 1, r4.n)\nend\nend\nend\nend\nlogErr([[choose text fix installed]])\nend\nlocal function tryPatchAfterOpen()\n-- 第 108 节：背包归置 SetItemPos 崩溃护栏（用户报过一次 invalid parameter A，\n-- 复现不稳定）。包 uw_backpack_bag_jigsaw.SetItemPos：先走原函数，崩了再\n-- 手动摆有效项、坏项跳过并记日志（module 级幂等，新实例创建时拷到新副本）。\npcall(function()\nlocal kJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nlocal J = package.loaded[kJ]\nif type(J) == [[table]] and type(J.SetItemPos) == [[function]] and J.bMikuPosGuard ~= true then\nJ.bMikuPosGuard = true\nlocal origPos = J.SetItemPos\nJ.SetItemPos = function(s2, ...)\nlocal okC, errC = pcall(origPos, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\npcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nend\nend\nend\nlocal lf = io.open(sLog, [[a]])\nif lf then\nlf:write([[SetItemPos guard: ]] .. tostring(errC))\nlf:write(string.char(10))\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nlocal okCell = false\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos then okCell = true end\nend\nif not okCell then\nlf:write([[bad u=]] .. tostring(w and w.uId) .. [[ pos=]] .. tostring(w and w.nPos) ..\n[[ dir=]] .. tostring(w and w.nDir) ..\n[[ tp=]] .. tostring(w and w.tpCfg and w.tpCfg.ID))\nlf:write(string.char(10))\nend\nend\nlf:close()\nend\nend\nend\nend)\nlocal okI, pi = pcall(UI.GetUI, sUI)\nif okI and type(pi) == [[table]] then\nlocal did = patchShopInst(pi)\nif did and pi.__bOpen == true then\npcall(function() pi:RefreshShop() end)\nend\nend\npcall(patchChooseText)\n-- 111r：队伍 buff 被动探针。组合效果查看页读谁、返回什么结构，开页即记录。\n-- BagRogue 模块可写（bMiku* 旗标先例），幂等守标防重包。\ndo\nlocal BR = _G.BagRogue\nif type(BR) == [[table]] then\nlocal function dumpRow(t)\nif type(t) ~= [[table]] then return tostring(t) end\nlocal ps = {}\nfor k, v in pairs(t) do\nif type(k) == [[string]] or type(k) == [[number]] then\nif type(v) == [[string]] then\ntable.insert(ps, tostring(k) .. [[=]] .. string.sub(v, 1, 40))\nelseif type(v) == [[number]] or type(v) == [[boolean]] then\ntable.insert(ps, tostring(k) .. [[=]] .. tostring(v))\nelse\ntable.insert(ps, tostring(k) .. [[:(]] .. type(v) .. [[)]])\nend\nend\nend\ntable.sort(ps)\nreturn table.concat(ps, [[ | ]])\nend\nlocal function logRet(tag, rs)\nlocal lfp = io.open(sLog, [[a]])\nif lfp then\nlfp:write(tag .. [[ nret=]] .. tostring(#rs))\nfor ri = 1, #rs do\nlocal rv = rs[ri]\nif type(rv) == [[table]] then\nlfp:write([[ ret]] .. ri .. [[#=]] .. tostring(#rv))\nif #rv ~= 0 then\nfor ei = 1, math.min(#rv, 4) do\nlfp:write([[ e]] .. ei .. [[:]] .. dumpRow(rv[ei]))\nend\nelse\nlocal ci = 0\nfor k, v in pairs(rv) do\nci = ci - (-1)\nif ci == 1 or ci == 2 or ci == 3 or ci == 4 then\nlfp:write([[ k]] .. tostring(k) .. [[:]] .. dumpRow(v))\nend\nend\nend\nelse\nlfp:write([[ ret]] .. ri .. [[=]] .. type(rv))\nend\nend\nlfp:write(string.char(10))\nlfp:close()\nend\nend\nif type(BR.GetAllTeamBuff) == [[function]] and BR.bMikuTbLog ~= true then\nBR.bMikuTbLog = true\nlocal oldTb = BR.GetAllTeamBuff\nBR.GetAllTeamBuff = function(...)\nlocal rs = {oldTb(...)}\npcall(logRet, [[teambuff]], rs)\nreturn table.unpack(rs)\nend\nend\nif type(BR.GetRelicItemAll) == [[function]] and BR.bMikuRaLog ~= true then\nBR.bMikuRaLog = true\nlocal oldRa = BR.GetRelicItemAll\nBR.GetRelicItemAll = function(...)\nlocal rs = {oldRa(...)}\npcall(logRet, [[relicall]], rs)\nreturn table.unpack(rs)\nend\nend\nend\nend\nend\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and UI.bMikuShopInst ~= true then\nUI.bMikuShopInst = true\nlocal fnOpen = UI.Open\nUI.Open = function(...)\nlocal r = table.pack(fnOpen(...))\npcall(tryPatchAfterOpen)\nreturn table.unpack(r, 1, r.n)\nend\nend";

	/// <summary>
	/// 302：后勤基础特长重修解锁。
	/// 官方规则是「特长1、2 只能重修其中 1 条」，客户端在
	/// uw_logistics_rebuilditem.UpdateState(self, bLock, ...) 里按 bLock 画锁图标、
	/// 并把重修按钮收起来。这里把 bLock 恒置 false，两个词条永远可选；
	/// 服务端 SupporterCard_ResetInitialAffix 本来就按 Index 1/2 对称处理，两边一起放开。
	/// 模块可能在登录补丁之后才加载，所以额外挂一次 UI.Open 反复补装。
	/// </summary>
	public const string LogiRebuildUnlock = "local function mikuLogiRbUnlock()\nlocal P = package\nif type(P) ~= [[table]] or type(P.loaded) ~= [[table]] then return end\nlocal key = [[UMG.Support.LogisRebuild.Widgets.uw_logistics_rebuilditem]]\nlocal mod = P.loaded[key]\nif type(mod) ~= [[table]] then return end\nif mod.bMikuRbUnlock == true then return end\nlocal old = mod.UpdateState\nif type(old) ~= [[function]] then return end\nmod.bMikuRbUnlock = true\nmod.UpdateState = function(self, bLock, bSelected, nFixedId)\nlocal r = table.pack(pcall(old, self, false, bSelected, nFixedId))\nif r[1] then return table.unpack(r, 2, r.n) end\nreturn nil\nend\nend\nmikuLogiRbUnlock()\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuRbUnlockHook ~= true then\n_G.bMikuRbUnlockHook = true\nlocal oldOpen = UI.Open\nUI.Open = function(...)\npcall(mikuLogiRbUnlock)\nlocal q = table.pack(oldOpen(...))\npcall(mikuLogiRbUnlock)\nreturn table.unpack(q, 1, q.n)\nend\nend";

	/// <summary>
	/// LogiRebuildNoConfirm：后勤基础重修点词条时，原生会弹一个二次确认
	/// 「特长【A】将被重修，会导致特长【B】被锁定而无法重修，是否确认重修？」。
	/// 刷词条时每次都要点确认，很打断节奏。该弹窗由面板自己拼：
	/// UI.Open(MessageBox, sMsg, fOk)，fOk 是 uw_logistics_rebuildoriginal.lua:28 的闭包
	/// （upvalue 带 self，无参调用即可发出 Req_SelectInitialAffix）。
	/// 这里在 UI.Open 上做文本精确匹配，命中就直接调 fOk 并跳过开窗；
	/// 匹配串用 string.char 拼 UTF-8 字节，避免把中文写进补丁文本被转码。
	/// 不命中（其它确认框）一律原样放行。
	/// 幂等标记必须放全局变量（_G.MikuRbNoCbFn 记住上一次包装的函数）——
	/// Lua 里 function 不能挂字段，早先写成 UI.Open.bMikuRbNoCb 会直接
	/// attempt to index a function value，整段补丁在赋值前就崩掉，等于没装。
	/// </summary>
	public const string LogiRebuildNoConfirm = "local function mikuRbNoCb()\nlocal P = package\nif type(P) ~= [[table]] or type(P.loaded) ~= [[table]] then return end\nif type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] then return end\nif _G.MikuRbNoCbFn == UI.Open then return end\nlocal NEEDLE = string.char(229,176,134,232,162,171,233,135,141,228,191,174)\nlocal prev = UI.Open\nlocal wrap = function(w, a1, a2, ...)\nif type(a1) == [[string]] and type(a2) == [[function]] then\nif string.find(a1, NEEDLE, 1, true) then\nlocal okw, nm = pcall(tostring, w)\nif okw and nm == [[MessageBox]] then\nlocal ok = pcall(a2)\nif ok then return end\nend\nend\nend\nreturn prev(w, a1, a2, ...)\nend\n_G.MikuRbNoCbFn = wrap\nUI.Open = wrap\nend\nmikuRbNoCb()";

	/// <summary>
	/// LogiRebuildKeepIndex：后勤基础重修完成后，面板会自动把选中项跳回第 1 条，
	/// 想连着刷同一条就得每次都重新点一下。根因在
	/// uw_logistics_rebuildoriginal.SetAffixPanelState(2) 里：
	/// 它先 UpdateAffixPanel 把 AffixIndex 覆盖成卡槽 SLOT_AFFIXINDEX 的值（重修后是 0），
	/// 再调 UpdateRebuildState()（不传参），而该方法内部是 `AffixIndex = index or 1`，
	/// 于是选中项落到第 1 条。
	/// 做法：在 Logistics.Req_ResetInitialAffix 发出时记住 index，并在
	/// UpdateRebuildState 收到 nil 时把记住的 index 补回去，让刷新后仍停在原词条。
	/// 面板模块可能被重载，所以每次发请求前都再补装一次（幂等靠 _G 记住上一层函数）。
	/// </summary>
	public const string LogiRebuildKeepIndex = "local function mikuRbKeepIdx()\nlocal P = package\nif type(P) ~= [[table]] or type(P.loaded) ~= [[table]] then return end\nlocal KEY = [[UMG.Support.LogisRebuild.Widgets.uw_logistics_rebuildoriginal]]\nlocal function installPanel()\nlocal mod = P.loaded[KEY]\nif type(mod) ~= [[table]] then return end\nlocal old = mod.UpdateRebuildState\nif type(old) ~= [[function]] then return end\nif _G.MikuKeepIdxWrap == old then return end\nlocal wrap = function(self, idx)\nif idx == nil and _G.MikuKeepPend == true then\n_G.MikuKeepPend = false\nlocal k = _G.MikuKeepIdx\nif k == 1 or k == 2 then idx = k end\nend\nreturn old(self, idx)\nend\n_G.MikuKeepIdxWrap = wrap\nmod.UpdateRebuildState = wrap\nend\ninstallPanel()\nif type(Logistics) == [[table]] and type(Logistics.Req_ResetInitialAffix) == [[function]] and _G.MikuKeepReqWrap ~= Logistics.Req_ResetInitialAffix then\nlocal oreq = Logistics.Req_ResetInitialAffix\nlocal rw = function(card, index, fixedId, cb)\n_G.MikuKeepIdx = index\n_G.MikuKeepPend = true\ninstallPanel()\nreturn oreq(card, index, fixedId, cb)\nend\n_G.MikuKeepReqWrap = rw\nLogistics.Req_ResetInitialAffix = rw\nend\nend\nmikuRbKeepIdx()";

	/// <summary>
	/// LogiCardLock：后勤详情页 PanelLock 下的 BtnLock 点不动。
	/// 探针实测：控件存在、IsEnabled=true、Visible，但 OnClicked 里没有任何回调，
	/// 点下去连网络请求都不发（钩住 CallGSWithDependParam 也没抓到）。服务端原本也没有锁定接口。
	/// 这里补上绑定：读取卡片当前 Flag（FLAG_LOCK=2）判断是否已锁，点击翻转，
	/// 发 SupporterCard_SetLock 给服务端；同时在本地切换 Locked / Unlocked 两个图示面板做即时反馈。
	/// 面板每次打开时按卡片真实 Flag 校正一次，避免显示与服务端不一致。
	/// </summary>
	public const string LogiCardLock = "local function mikuCardLock()\nif type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] then return end\nif _G.MikuCardLockPrev == UI.Open then return end\nlocal function apply(on, onP, offP)\nif onP then pcall(function() onP:SetVisibility(on and 0 or 1) end) end\nif offP then pcall(function() offP:SetVisibility(on and 1 or 0) end) end\nend\nlocal function nodes()\nlocal w = nil\nfor _, v in pairs(UI.tbWidget or {}) do\nlocal ok, nm = pcall(function() return v.sName end)\nif ok and nm == [[logishow]] then w = v end\nend\nif not w or not w.Object then return end\nlocal btn, onP, offP\nlocal function walk(node, d)\nif not node or d == 14 then return end\nlocal ok, nm = pcall(function() return node:GetName() end)\nif ok then\nnm = tostring(nm)\nif nm == [[BtnLock]] then btn = node end\nif nm == [[Locked]] then onP = node end\nif nm == [[Unlocked]] then offP = node end\nend\nlocal okc, cnt = pcall(function() return node:GetChildrenCount() end)\nif okc and type(cnt) == [[number]] then\nfor i = 1, cnt do\nlocal okch, ch = pcall(function() return node:GetChildAt(i - 1) end)\nif okch and ch then walk(ch, d - (-1)) end\nend\nelse\nlocal okw, sub = pcall(function() return node.WidgetTree.RootWidget end)\nif okw and sub and sub ~= node then walk(sub, d - (-1)) end\nend\nend\nwalk(w.Object.WidgetTree.RootWidget, 0)\nreturn btn, onP, offP\nend\nlocal function onClick()\nlocal card = Logistics.CurCard\nif not card then return end\nlocal ok1, uid = pcall(function() return card:Id() end)\nif not ok1 or not uid then return end\nlocal ok2, flag = pcall(function() return card:Flag() end)\nif not ok2 or not flag then return end\nlocal has = math.floor((flag % 4) / 2) == 1\nlocal b2, on2, off2 = nodes()\napply(not has, on2, off2)\nif me then me:CallGS([[SupporterCard_SetLock]], json.encode({ Id = uid, Lock = (has and 0 or 1) })) end\nend\nlocal function install()\nlocal btn, onP, offP = nodes()\nif not btn or not onP or not offP then return end\nlocal card = Logistics.CurCard\nlocal has = false\nif card then\nlocal ok2, flag = pcall(function() return card:Flag() end)\nif ok2 and flag then has = math.floor((flag % 4) / 2) == 1 end\nend\napply(has, onP, offP)\nif _G.MikuLockBtn == btn then return end\n_G.MikuLockBtn = btn\nlocal okAdd = pcall(function() btn.OnClicked:Add(_G, onClick) end)\nif not okAdd then pcall(function() btn.OnClicked:Add(onClick) end) end\nend\nlocal prev = UI.Open\nlocal wrap = function(w, ...)\nlocal r = prev(w, ...)\npcall(install)\nreturn r\nend\n_G.MikuCardLockPrev = wrap\nUI.Open = wrap\npcall(install)\nend\nmikuCardLock()";

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
					await Push(connection, $"login@{ms}");
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("push failed: " + ex2.Message);
				}
			});
		}
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
					Log("resend failed: " + ex2.Message);
				}
			});
		}
	}

	public static async Task Push(Connection connection, string reason)
	{
		if (connection.Player == null)
		{
			return;
		}
		uint eraId = DlcPin.Override();
		long floor = ActivationFloor(eraId);
		// 轻量复刻期（1~4）：活动窗口按该期过滤，但 DLC 配置仍走默认期，
		// 否则主界面版本活动入口/面板会切到这套客户端里跑不通的老版本内容。
		uint confEra = EraLite.DlcEra(eraId);
		// 客户端脚本里到处是 `_G.MikuDmEra ~= 0` 的判断，默认态（pin=99）
		// 下发时必须还原成 0，否则客户端会当成「固定了某一期」而放开全部过滤（第 226 节）。
		uint clientEra = DlcPin.ClientEra(eraId);
		List<(string Name, string Lua)> patches = Build(floor, confEra, clientEra);
		int sent = 0;
		List<string> skipped = new List<string>();
		List<string> residues = new List<string>();
		foreach (var (name, lua) in patches)
		{
			// 占位符兜底自检（第 224 节）：模板化补丁一旦漏做 Replace，
			// 客户端拿到的是 nil，功能静默失效而日志里还是 N/N 全绿。
			if (lua != null && lua.Contains("MIKU_", StringComparison.Ordinal))
			{
				residues.Add(name);
			}
			if (await Send(connection, name, lua))
			{
				sent++;
			}
			else
			{
				skipped.Add(name);
			}
		}
		string eraLabel = DlcPin.IsDefault(eraId) ? "游戏默认" : ("第 " + eraId + " 期");
		Log($"{reason}: {eraLabel} floor={floor} 客户端补丁 {sent}/{patches.Count}" + ((skipped.Count > 0) ? (" 跳过[" + string.Join(",", skipped) + "]") : "") + ((residues.Count > 0) ? (" 残留占位符[" + string.Join(",", residues) + "]") : ""));
	}

	public static long ActivationFloor(uint eraId)
	{
		if (eraId != 0 && GameData.DlcActivityData.TryGetValue(eraId, out DlcActivityExcel value))
		{
			long num = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value.EnterStartTime));
			if (num > 0)
			{
				return num;
			}
		}
		return DlcRuntimeOverrides.ActivationFloor();
	}

	public static long ActivationCeil(uint eraId)
	{
		long num = ActivationFloor(eraId);
		if (num <= 0)
		{
			return 0L;
		}
		long num2 = 0L;
		foreach (DlcActivityExcel value in GameData.DlcActivityData.Values)
		{
			long num3 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value.EnterStartTime));
			if (num3 > num && (num2 == 0L || num3 < num2))
			{
				num2 = num3;
			}
		}
		return (num2 > 0) ? num2 : (num + 7776000);
	}

	private static string TryIdsLiteral(uint eraId)
	{
		try
		{
			if (eraId != 0 && GameData.DlcActivityData.TryGetValue(eraId, out DlcActivityExcel value) && value.TryIds != null)
			{
				return string.Join(",", value.TryIds);
			}
		}
		catch
		{
		}
		return "";
	}

	// 第 235 节（2026-11-11 23:5x）：活动商店统一走原生页签，追忆商店整体关闭。
	//
	// dlc7shop / dlc11shop 这类页面本身就是「追忆商店页」：页签固定调
	// ShopLogic.GetFormerActivityOpenTab(追忆活动 id)，原生返回 {401}~{404}
	// （往期活动的「追忆精选」摊位，卖 goodsId 101~115 那批老货）。
	// 守护脚本「共鸣卡池.ps1」的 Send-FormerShopFix 一直在往这个列表里补
	// 114/106/122/130，并把 FormerDlc.nCurActivityId 强制钉到某个追忆活动，
	// 于是活动商店显示的是往期追忆货，而不是当期的限量供应。
	//
	// 运行期实测（第 235 节）：把该函数换成返回 GetDlcOpenTab() 后，界面
	// tbShops 立刻变成 {113,114}（ui.DLC1shop1 / ui.DLC1shop2），nEndTime 为未来，
	// 商品正常渲染。所以这里直接接管，不再区分「追忆哪一期」。
	public const string NoFormerShop = "local function nfs()\nlocal SL = _G.ShopLogic\nif type(SL) ~= [[table]] then return end\nlocal o = rawget(SL, [[__MikuNfsOrig]])\nlocal cur = rawget(SL, [[GetFormerActivityOpenTab]])\nif type(o) == [[function]] and cur ~= nil and rawget(SL, [[__MikuNfsFn]]) == cur then return end\nif type(o) ~= [[function]] and type(cur) == [[function]] then\nrawset(SL, [[__MikuNfsOrig]], cur)\no = cur\nend\nlocal function native(a)\nlocal g = SL.GetDlcOpenTab\nif type(g) == [[function]] then\nlocal ok, r = pcall(g)\nif ok and type(r) == [[table]] and #r ~= 0 then return r end\nend\nif type(o) == [[function]] then\nlocal ok2, r2 = pcall(o, a)\nif ok2 and type(r2) == [[table]] then return r2 end\nend\nreturn {}\nend\nlocal f = function(a) return native(a) end\nrawset(SL, [[__MikuNfsFn]], f)\nrawset(SL, [[GetFormerActivityOpenTab]], f)\nprint([[NFSOK]])\nend\npcall(nfs)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuNfsHook ~= true then\n_G.bMikuNfsHook = true\nlocal oldOpenN = UI.Open\nUI.Open = function(...)\npcall(nfs)\nreturn oldOpenN(...)\nend\nend";
	public const string Dlc24Unlock = "local function d24fix()\nlocal ui = UI\nif not ui or type(ui.Open) ~= [[function]] then return end\nlocal tbDone = tbMikuD24Done\nif type(tbDone) ~= [[table]] then tbDone = {} tbMikuD24Done = tbDone end\nlocal tbAllow = tbMikuD24Allow\nif type(tbAllow) ~= [[table]] then\ntbAllow = { Dorm = true, Online = true, Sevendays = true, Dive = true }\ntbMikuD24Allow = tbAllow\nend\nlocal tbTarget = tbMikuD24Target\nif type(tbTarget) ~= [[table]] then\ntbTarget = { Dorm = [[rolefestivalmain]] }\ntbMikuD24Target = tbTarget\nend\nlocal oldOpen = tbMikuD24Open\nif type(oldOpen) ~= [[function]] then oldOpen = ui.Open tbMikuD24Open = oldOpen end\nui.Open = oldOpen\nlocal function d24log(s)\nlocal f = io.open([[D:/DSH/.tools/_d24click.txt]], [[a]])\nif f then f:write(s) f:write([[\n]]) f:close() end\nend\nlocal function d24wrap(inst)\nif type(inst) ~= [[table]] then return end\nif tbDone[inst] then return end\nlocal oldO = inst.OnClickAct\nif type(oldO) ~= [[function]] then return end\ntbDone[inst] = true\ninst.OnClickAct = function(self, key, ...)\nif tbAllow[key] then\nlocal tgt = tbTarget[key]\nd24log([[CLICK ]] .. tostring(key) .. [[ target=]] .. tostring(tgt))\nif tgt then\nlocal ok, err = pcall(oldOpen, tgt)\nd24log([[  open ]] .. tostring(tgt) .. [[ ok=]] .. tostring(ok) .. [[ err=]] .. tostring(err))\nend\nreturn true\nend\nreturn oldO(self, key, ...)\nend\nend\nlocal function d24scan()\nif type(ui.tbWidget) ~= [[table]] then return end\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and type(v.sName) == [[string]] and string.lower(v.sName) == [[dlc24main]] then d24wrap(v) end\nend\nend\nui.Open = function(sName, ...)\nlocal r = table.pack(oldOpen(sName, ...))\nd24scan()\nreturn table.unpack(r, 1, r.n)\nend\nd24scan()\nend\npcall(d24fix)";

	public const string PaintFix = "local function p8fix()\nlocal R = Resource\nif type(R) ~= [[table]] then return end\nlocal P = R.tbPainting\nif type(P) ~= [[table]] then return end\nif P[1210008] ~= nil then return end\nP[1210008] = { id = 1210008, define = 12, logo = 130010000, p1 = 130010001, p2 = 130010002, p3 = 130010003, p4 = 130010004, p5 = 130010005, p11 = 130010005, p6 = 130010006 }\nend\npcall(p8fix)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuP8Hook ~= true then\n_G.bMikuP8Hook = true\nlocal old8 = UI.Open\nUI.Open = function(...)\npcall(p8fix)\nreturn old8(...)\nend\nend";

	public const string DormAvatar = "local function dmResName(img)\nlocal ok, br = pcall(function() return img.Brush end)\nif not ok or br == nil then return [[]] end\nlocal ok2, res = pcall(function() return br.ResourceObject end)\nif not ok2 or res == nil then return [[]] end\nlocal ok3, n = pcall(function() return res:GetName() end)\nif not ok3 or type(n) ~= [[string]] then return [[]] end\nreturn n\nend\nlocal function dms(f, d)\nlocal ok, r = pcall(f)\nif ok then return r end\nreturn d\nend\nlocal function dmName(w)\nlocal n = dms(function() return w:GetName() end, nil)\nif type(n) == [[string]] then return n end\nreturn [[?]]\nend\nlocal function dmCls(w)\nlocal c = dms(function() return w:GetClass() end, nil)\nif c == nil then return [[]] end\nlocal n = dms(function() return c:GetName() end, nil)\nif type(n) == [[string]] then return n end\nreturn [[]]\nend\nlocal function dmTintA(w)\nlocal sc = dms(function() return w.Brush.TintColor.SpecifiedColor end, nil)\nif sc == nil then return nil end\nreturn sc.A\nend\nlocal function dmColor()\nlocal c = nil\npcall(function() c = UE4.FLinearColor(1, 1, 1, 1) end)\nif c == nil then pcall(function() c = UE.FLinearColor(1, 1, 1, 1) end) end\nreturn c\nend\nlocal tbDmList = {}\nlocal tbDmSet = {}\nlocal function dmRemember(it)\nif type(it) ~= [[table]] then return end\nif tbDmSet[it] ~= nil then return end\nif #tbDmList == 96 then return end\ntbDmSet[it] = true\ntable.insert(tbDmList, it)\nend\nlocal nDmFix = 0\nlocal function dmBump()\nnDmFix = nDmFix - (-1)\nend\nlocal nGLog = 0\nlocal function dmG(s)\nif nGLog == 200 then return end\nnGLog = nGLog - (-1)\nlocal h = io.open([[D:/DSH/.tools/_ghost3.txt]], [[a]])\nif h ~= nil then\nh:write(tostring(s) .. string.char(10))\nh:close()\nend\nend\nlocal function dmSelf(it, tag)\nlocal ro = dms(function() return it.RenderOpacity end, nil)\nif ro == nil or ro == 1 or ro == 0 then return 0 end\npcall(function() it:StopAllAnimations() end)\npcall(function() it:SetRenderOpacity(1) end)\nlocal co0 = dms(function() return it.ColorAndOpacity end, nil)\nif co0 ~= nil and co0.A ~= nil and co0.A ~= 1 and co0.A ~= 0 then\nlocal cc0 = dmColor()\nif cc0 ~= nil then pcall(function() it:SetColorAndOpacity(cc0) end) end\nend\ndmG([[SELF ]] .. tag .. [[ cls=]] .. dmCls(it) .. [[ nm=]] .. dmName(it) .. [[ ro=]] .. tostring(ro))\nreturn 1\nend\nlocal function dmAnc(it, tag)\nlocal w = it\nfor i = 0, 12 do\nlocal p = dms(function() return w:GetParent() end, nil)\nif p == nil then break end\nlocal ro = dms(function() return p.RenderOpacity end, nil)\nif ro ~= nil and ro ~= 1 and ro ~= 0 then\npcall(function() p:StopAllAnimations() end)\npcall(function() p:SetRenderOpacity(1) end)\ndmG([[ANC ]] .. tag .. [[ lv=]] .. tostring(i) .. [[ cls=]] .. dmCls(p) .. [[ nm=]] .. dmName(p) .. [[ ro=]] .. tostring(ro))\nend\nlocal co = dms(function() return p.ColorAndOpacity end, nil)\nif co ~= nil and co.A ~= nil and co.A ~= 1 and co.A ~= 0 then\nlocal cc = dmColor()\nif cc ~= nil then\npcall(function() p:SetColorAndOpacity(cc) end)\ndmG([[ANC-CO ]] .. tag .. [[ lv=]] .. tostring(i) .. [[ cls=]] .. dmCls(p) .. [[ nm=]] .. dmName(p) .. [[ A=]] .. tostring(co.A))\nend\nend\nw = p\nend\nend\nlocal function dmWant(nm, rn)\nif string.find(nm, [[ImgIcon]], 1, true) then return true end\nif nm == [[Role]] then return true end\nif nm == [[Image_199]] then return true end\nif rn ~= [[]] and string.find(string.lower(rn), [[icon_girl]], 1, true) then return true end\nreturn false\nend\nlocal function dmNorm(w, d, bAll)\nif w == nil or d == nil then return end\nlocal nm = dmName(w)\nlocal rn = dmResName(w)\nif bAll or dmWant(nm, rn) then\nlocal ro = dms(function() return w.RenderOpacity end, nil)\nif ro ~= nil and ro ~= 1 and ro ~= 0 then\npcall(function() w:SetRenderOpacity(1) end)\ndmBump()\nend\nlocal co = dms(function() return w.ColorAndOpacity end, nil)\nif co ~= nil and co.A ~= nil and co.A ~= 1 and co.A ~= 0 then\nlocal cc = dmColor()\nif cc ~= nil then\npcall(function() w:SetColorAndOpacity(cc) end)\ndmBump()\nend\nend\nlocal ta = dmTintA(w)\nif ta ~= nil and ta ~= 1 and ta ~= 0 then\nlocal cc2 = dmColor()\nif cc2 ~= nil then\npcall(function() w:SetBrushTintColor(cc2) end)\ndmBump()\nend\nend\nend\nif d ~= 0 then\nlocal c = dms(function() return w:GetChildrenCount() end, nil)\nif c ~= nil then\nfor i = 0, c - 1 do\nlocal ch = dms(function() return w:GetChildAt(i) end, nil)\nif ch ~= nil then dmNorm(ch, d - 1, bAll) end\nend\nend\nend\nend\nlocal function dmAreaWidget()\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return nil end\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and v.Object ~= nil then\nlocal cn = dmCls(v.Object)\nif cn ~= [[]] and string.find(cn, [[drag_area]], 1, true) then return v.Object end\nend\nend\nreturn nil\nend\nlocal function dmAreaRoot()\nlocal o = dmAreaWidget()\nif o == nil then return nil end\nlocal wt = dms(function() return o.WidgetTree end, nil)\nif wt == nil then return nil end\nreturn dms(function() return wt.RootWidget end, nil)\nend\nlocal function dmDragActive()\nlocal o = dmAreaWidget()\nif o == nil then return false end\nlocal v = dms(function() return o.Visibility end, nil)\nif v == nil then return false end\nreturn v ~= 1\nend\nlocal function dmItemRoot(it)\nlocal wt = dms(function() return it.WidgetTree end, nil)\nif wt == nil then return nil end\nreturn dms(function() return wt.RootWidget end, nil)\nend\nlocal function dmLogDrag(it)\nif it == nil then it = dmAreaRoot() end\nlocal o = {}\nlocal nMax = 300\nlocal function W(s)\nif #o ~= nMax then table.insert(o, tostring(s)) end\nend\nlocal function tree(w, d, path)\nif w == nil or d == nil then return end\nlocal nm = dmName(w)\nlocal p2 = path .. [[/]] .. nm\nlocal ro = dms(function() return w.RenderOpacity end, nil)\nlocal co = dms(function() return w.ColorAndOpacity end, nil)\nlocal ca = [[nil]]\nif co ~= nil and co.A ~= nil then ca = tostring(co.A) end\nlocal rn = dmResName(w)\nlocal ta = [[-]]\nif rn ~= [[]] then\nlocal t = dmTintA(w)\nif t ~= nil then ta = tostring(t) end\nend\nW(p2 .. [[ cls=]] .. dmCls(w) .. [[ vis=]] .. tostring(dms(function() return w.Visibility end, nil))\n.. [[ ro=]] .. tostring(ro) .. [[ cOA=]] .. ca .. [[ tintA=]] .. ta\n.. (rn ~= [[]] and ([[ res=]] .. rn) or [[]]))\nif d ~= 0 then\nlocal c = dms(function() return w:GetChildrenCount() end, nil)\nif c ~= nil then\nfor i = 0, c - 1 do\nlocal ch = dms(function() return w:GetChildAt(i) end, nil)\nif ch ~= nil then tree(ch, d - 1, p2) end\nend\nend\nend\nend\nlocal bIt = (it ~= nil)\nif bIt then W([[### item cls=]] .. dmCls(it) .. [[ vis=]] .. tostring(dms(function() return it.Visibility end, nil)) .. [[ ro=]] .. tostring(dms(function() return it.RenderOpacity end, nil))) end\nif bIt then\nlocal wp = it\nfor i = 0, 12 do\nlocal pp = dms(function() return wp:GetParent() end, nil)\nif pp == nil then break end\nW([[P]] .. tostring(i) .. [[ cls=]] .. dmCls(pp) .. [[ nm=]] .. dmName(pp) .. [[ ro=]] .. tostring(dms(function() return pp.RenderOpacity end, nil)))\nwp = pp\nend\nend\nif bIt then tree(dmItemRoot(it), 8, [[I]]) end\nW([[### drag area cls=]] .. tostring(dmAreaWidget() ~= nil))\ntree(dmAreaRoot(), 10, [[A]])\nlocal h = io.open([[D:/DSH/.tools/_draglog.txt]], [[w]])\nif h ~= nil then h:write(table.concat(o, string.char(10))) h:close() end\nend\nlocal function dmRoomOne(it)\ndmRemember(it)\nlocal ok, v = pcall(function() return it.Visibility end)\nlocal okpre, ipre = pcall(function() return it.InDrag end)\nif not (okpre and ipre == true) then\nif not ok or v == nil or v == 1 then return 0 end\nend\nlocal ok2, img = pcall(function() return it.ImgIconNew end)\nif not ok2 or img == nil then return 0 end\nlocal nm = dmResName(img)\nlocal okd, ind = pcall(function() return it.InDrag end)\nif okd and ind == true then\npcall(function() it:StopAllAnimations() end)\npcall(function() it:SetRenderOpacity(1) end)\npcall(function() img:SetRenderOpacity(1) end)\nlocal okv2 = pcall(function() img:SetVisibility(4) end)\npcall(dmLogDrag, it)\nif okv2 then return 1 end\nreturn 0\nend\nif not string.find(nm, [[_130_]], 1, true) then return 0 end\nlocal okr, op = pcall(function() return img.RenderOpacity end)\nlocal okt, iop = pcall(function() return it.RenderOpacity end)\nif (okr and op ~= nil and op ~= 1) or (okt and iop ~= nil and iop ~= 1) then\npcall(function() it:StopAnimation(it.ani_Drag_in) end)\npcall(function() it:SetRenderOpacity(1) end)\npcall(function() img:SetRenderOpacity(1) end)\npcall(dmLogDrag, it)\nreturn 1\nend\nlocal okc, cur = pcall(function() return img.Visibility end)\nif okc and cur ~= nil and cur ~= 1 then return 1 end\nlocal ok3 = pcall(function() img:SetVisibility(4) end)\nif ok3 then return 1 end\nreturn 0\nend\nlocal tbPanelName = { [[PanelRoom]], [[PanelRoom2]], [[PanelRoom3]], [[PanelHealthRoom]], [[PanelHotSpring]], [[PanelPub]], [[PanelBeach]], [[PanelArcade]], [[GridPanel]], [[GridPanel2]], [[GridPanel3]] }\nlocal function dmRooms()\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return 0 end\nlocal tbCount = {}\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and type(v.sName) == [[string]] then\nlocal sn = string.lower(v.sName)\nif sn == [[dormmap]] or sn == [[dormroom]] then\nlocal obj = v.Object\nif obj ~= nil then\nfor _, pn in ipairs(tbPanelName) do\nlocal okp, panel = pcall(function() return obj[pn] end)\nif okp and panel ~= nil then\nfor i = 1, 8 do\nlocal nam = [[Item]] .. i\nlocal oki, it = pcall(function() return panel[nam] end)\nif oki and type(it) == [[table]] then\nif dmRoomOne(it) == 1 then table.insert(tbCount, true) end\nend\nend\nend\nend\nend\nend\nend\nend\nreturn #tbCount\nend\nlocal function dmNpc()\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return 0 end\nlocal inst = nil\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and type(v.sName) == [[string]] and string.lower(v.sName) == [[dormmap]] then inst = v end\nend\nif type(inst) ~= [[table]] then return 0 end\nlocal nw = inst.tbNpcWidgets\nif type(nw) ~= [[table]] then return 0 end\nlocal tbCount = {}\nfor _, e in pairs(nw) do\nif type(e) == [[table]] and e.Object and e.IconPath then\nlocal img = e.Object.ImgIconNew\nif img then\nlocal okr, op = pcall(function() return img.RenderOpacity end)\nlocal okc, cur = pcall(function() return img.Visibility end)\nlocal need = true\nif okr and op ~= nil and op ~= 1 then need = false end\nif okc and cur ~= nil and cur ~= 1 then need = false end\nif need then\nlocal ok = pcall(function() img:SetVisibility(4) end)\nif ok then table.insert(tbCount, true) end\nend\nend\nend\nend\nreturn #tbCount\nend\nlocal tbMikuRi = {}\nlocal tMikuRi = 0\nlocal function dmRiScan()\nlocal list = {}\nlocal rm = nil\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return list end\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and type(v.sName) == [[string]] and string.lower(v.sName) == [[dormroom]] then rm = v end\nend\nif type(rm) ~= [[table]] or rm.Object == nil then return list end\nlocal okw, wt = pcall(function() return rm.Object.WidgetTree end)\nif not okw or wt == nil then return list end\nlocal okr, root = pcall(function() return wt.RootWidget end)\nif not okr or root == nil then return list end\nlocal function seek(w, d)\nif w == nil or d == nil then return end\nlocal okc, cn = pcall(function() return w:GetClass() end)\nif okc and cn ~= nil then\nlocal okn, nm = pcall(function() return cn:GetName() end)\nif okn and type(nm) == [[string]] and string.find(nm, [[uw_dorm_roomitem]], 1, true) then table.insert(list, w) dmRemember(w) end\nend\nif d ~= 0 then\nlocal okcc, c = pcall(function() return w:GetChildrenCount() end)\nif okcc and c ~= nil then\nfor i = 0, c - 1 do\nlocal okch, ch = pcall(function() return w:GetChildAt(i) end)\nif okch and ch ~= nil then seek(ch, d - 1) end\nend\nend\nend\nend\nseek(root, 12)\nreturn list\nend\nlocal function dmRiOne(it)\nlocal ok, op = pcall(function() return it.RenderOpacity end)\nif not ok or op == nil or op == 1 then return 0 end\npcall(function() it:StopAnimation(it.ani_Drag_in) end)\npcall(function() it:SetRenderOpacity(1) end)\npcall(dmLogDrag, it)\nreturn 1\nend\nlocal function dmRiTick()\nlocal t = os.clock()\nlocal ds = t - tMikuRi\nif math.max(ds, 2) == ds then\ntMikuRi = t\ntbMikuRi = dmRiScan()\nend\nlocal tb = {}\nfor i = 1, #tbMikuRi do\nif dmRiOne(tbMikuRi[i]) == 1 then table.insert(tb, true) end\nend\nreturn #tb\nend\nlocal bDmDragging = false\nlocal tDmWatch = 0\nlocal function dmDragWatch()\nlocal t = os.clock()\nlocal ds = t - tDmWatch\nif math.max(ds, 0.15) ~= ds then return 0 end\ntDmWatch = t\nlocal bAct = dmDragActive()\nif not bAct then\nbDmDragging = false\nreturn 0\nend\nnDmFix = 0\nif not bDmDragging then\nbDmDragging = true\nfor i = 1, #tbDmList do\npcall(function() tbDmList[i]:StopAllAnimations() end)\nend\nlocal pick = nil\nfor i = 1, #tbDmList do\nlocal ro = dms(function() return tbDmList[i].RenderOpacity end, nil)\nif ro ~= nil and ro ~= 1 then pick = tbDmList[i] end\nend\npcall(dmLogDrag, pick)\nend\nfor i = 1, #tbDmList do\ndmNorm(dmItemRoot(tbDmList[i]), 8, false)\nend\ndmNorm(dmAreaRoot(), 10, false)\nreturn nDmFix\nend\n-- dmGhostProbe removed: the GetAll* native interface triggers a process-level\n-- EXCEPTION_ACCESS_VIOLATION on this client; pcall cannot catch it.\nlocal tbDmPar = {}\nlocal function dmDragFix()\nlocal n = 0\nfor i = 1, #tbDmList do\nlocal it = tbDmList[i]\nlocal bHit = false\nif dmSelf(it, [[item]]) == 1 then bHit = true end\nlocal par = dms(function() return it:GetParent() end, nil)\nif par ~= nil then\nlocal oldp = tbDmPar[it]\nif oldp ~= nil and oldp ~= par then\nbHit = true\ndmG([[REPARENT cls=]] .. dmCls(it) .. [[ newp=]] .. dmCls(par) .. [[/]] .. dmName(par))\nend\ntbDmPar[it] = par\nend\nif bHit then\ndmAnc(it, [[item]])\ndmNorm(dmItemRoot(it), 8, true)\nif n == 0 then\npcall(dmLogDrag, it)\nend\nn = n - (-1)\nend\nend\nreturn n\nend\nlocal tbPar2 = {}\nlocal tScan = 0\nlocal nScan = 0\nlocal function dmWalkInst(w, d, out)\nif w == nil then return end\nlocal cn = dmCls(w)\nlocal lc = string.lower(cn)\nif string.find(lc, [[dorm_roleitem]], 1, true) or string.find(lc, [[dorm_roomitem]], 1, true) then\ntable.insert(out, w)\nend\nif d ~= 0 then\nlocal c = dms(function() return w:GetChildrenCount() end, nil)\nif c ~= nil then\nfor i = 0, c - 1 do\nlocal ch = dms(function() return w:GetChildAt(i) end, nil)\nif ch ~= nil then dmWalkInst(ch, d - 1, out) end\nend\nend\nend\nend\nlocal function dmRoots()\nlocal out = {}\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return out end\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and v.Object ~= nil then\nlocal wt = dms(function() return v.Object.WidgetTree end, nil)\nif wt ~= nil then\nlocal r = dms(function() return wt.RootWidget end, nil)\nif r ~= nil then table.insert(out, r) end\nend\nend\nend\nreturn out\nend\nlocal function dmScanAll()\nlocal t = os.clock()\nlocal ds = t - tScan\nif math.max(ds, 1.0) ~= ds then return 0 end\ntScan = t\nnScan = nScan - (-1)\nlocal roots = dmRoots()\nlocal inst = {}\nfor i = 1, #roots do dmWalkInst(roots[i], 12, inst) end\nfor i = 1, #inst do\nlocal it = inst[i]\nlocal par = dms(function() return it:GetParent() end, nil)\nlocal pn = dmName(par)\nlocal old = tbPar2[it]\nlocal ro = dms(function() return it.RenderOpacity end, nil)\nif old == nil then\nif nScan ~= 1 then\ndmG([[NEW cls=]] .. dmCls(it) .. [[ par=]] .. pn .. [[ ro=]] .. tostring(ro))\nend\nif ro ~= nil and ro ~= 1 and ro ~= 0 then\npcall(function() it:StopAllAnimations() end)\npcall(function() it:SetRenderOpacity(1) end)\nend\nelseif old ~= pn then\ndmG([[MOVE cls=]] .. dmCls(it) .. [[ from=]] .. tostring(old) .. [[ to=]] .. pn .. [[ ro=]] .. tostring(ro))\ndmAnc(it, [[move]])\ndmNorm(dmItemRoot(it), 8, true)\npcall(dmLogDrag, it)\nend\ntbPar2[it] = pn\ndmRemember(it)\nend\nreturn #inst\nend\nlocal tbDeepSeen = {}\nlocal nDeepLog = 0\nlocal function dmDeepLog(s)\nif nDeepLog == 400 then return end\nnDeepLog = nDeepLog - (-1)\nlocal h = io.open([[D:/DSH/.tools/_ghost4.txt]], [[a]])\nif h ~= nil then\nh:write(tostring(s) .. string.char(10))\nh:close()\nend\nend\nlocal function dmDeepOne(w, tag)\nif w == nil then return 0 end\nlocal ro = dms(function() return w.RenderOpacity end, nil)\nlocal co = dms(function() return w.ColorAndOpacity end, nil)\nlocal ca = nil\nif co ~= nil then ca = co.A end\nlocal bFix = false\nif ro ~= nil and ro ~= 1 and ro ~= 0 then bFix = true end\nif ca ~= nil and ca ~= 1 and ca ~= 0 then bFix = true end\nlocal okd, ind = pcall(function() return w.InDrag end)\nlocal bDrag = false\nif okd == true and ind == true then bDrag = true end\nlocal okimg, img = pcall(function() return w.ImgIconNew end)\nlocal ivis = nil\nlocal inm = [[]]\nlocal bIcon = false\nif okimg == true and img ~= nil then\nivis = dms(function() return img.Visibility end, nil)\ninm = dmResName(img)\nif string.find(inm, [[_130_]], 1, true) ~= nil then bIcon = true end\nif bDrag == true and ivis ~= nil and ivis == 1 then bFix = true end\nend\nif bFix == false and bDrag == false then return 0 end\npcall(function() w:StopAllAnimations() end)\nif ro ~= nil and ro ~= 1 and ro ~= 0 then pcall(function() w:SetRenderOpacity(1) end) end\nif ca ~= nil and ca ~= 1 and ca ~= 0 then\nlocal cc = dmColor()\nif cc ~= nil then pcall(function() w:SetColorAndOpacity(cc) end) end\nend\nif bIcon == true then\npcall(function() img:SetRenderOpacity(1) end)\npcall(function() img:SetVisibility(4) end)\nend\nif tbDeepSeen[w] == nil then\ntbDeepSeen[w] = 1\nlocal par = dms(function() return w:GetParent() end, nil)\ndmDeepLog([[FIX ]] .. tostring(tag) .. [[ ro=]] .. tostring(ro) .. [[ cA=]] .. tostring(ca) .. [[ ivis=]] .. tostring(ivis) .. [[ res=]] .. inm .. [[ drag=]] .. tostring(bDrag) .. [[ par=]] .. dmName(par) .. [[/]] .. dmCls(par))\nend\nreturn 1\nend\nlocal tDeep = 0\nlocal function dmDeepScan()\nlocal t = os.clock()\nlocal ds = t - tDeep\nif math.max(ds, 0.3) ~= ds then return 0 end\ntDeep = t\nlocal roots = dmRoots()\nlocal n = 0\nlocal function walk(w, d)\nif w == nil or d == nil then return end\nlocal cn = dmCls(w)\nif cn ~= [[]] then\nif string.find(string.lower(cn), [[dorm_roleitem]], 1, true) ~= nil then\nif dmDeepOne(w, [[scan]]) == 1 then n = n - (-1) end\nend\nend\nif d ~= 0 then\nlocal c = dms(function() return w:GetChildrenCount() end, nil)\nif c ~= nil then\nfor i = 0, c - 1 do\nlocal ch = dms(function() return w:GetChildAt(i) end, nil)\nwalk(ch, d - 1)\nend\nend\nend\nend\nfor i = 1, #roots do walk(roots[i], 16) end\nreturn n\nend\nlocal function dmGhostHook(w, tag)\nif w == nil then return end\nlocal ro = dms(function() return w.RenderOpacity end, nil)\nlocal par = dms(function() return w:GetParent() end, nil)\ndmDeepLog([[HOOK ]] .. tostring(tag) .. [[ cls=]] .. dmCls(w) .. [[ ro=]] .. tostring(ro) .. [[ par=]] .. dmName(par))\npcall(dmDeepOne, w, [[hook]])\nend\nlocal tbHookMod = { [[UMG.House.Widgets.uw_dorm_roleitem]], [[UMG.House.Widgets.uw_dorm_roomitem]] }\nlocal tbHookFn = { [[DealDragItem]], [[OnDragBegin]], [[OnDragCancel]], [[OnDragOff]] }\nlocal function dmHookCls()\nlocal n = 0\nfor i = 1, #tbHookMod do\nlocal mn = tbHookMod[i]\nlocal m = package.loaded[mn]\nif type(m) == [[table]] then\nfor j = 1, #tbHookFn do\nlocal k = tbHookFn[j]\nlocal key = mn .. [[.]] .. k\nif _G[key] == nil and type(m[k]) == [[function]] then\n_G[key] = 1\nlocal old = m[k]\nm[k] = function(self, a, b, c, d)\npcall(old, self, a, b, c, d)\npcall(dmGhostHook, self, k)\nend\nn = n - (-1)\nend\nend\nend\nend\nreturn n\nend\nlocal function dmHookArea()\nlocal mn = [[UMG.House.Widgets.uw_room_drag_area]]\nlocal m = package.loaded[mn]\nif type(m) ~= [[table]] then return 0 end\nlocal n = 0\nif _G.bMikuAreaEnter == nil and type(m.OnDragEnter) == [[function]] then\n_G.bMikuAreaEnter = 1\nlocal old = m.OnDragEnter\nm.OnDragEnter = function(self, op, a, b)\npcall(old, self, op, a, b)\nlocal dv = nil\nif op ~= nil then pcall(function() dv = op.DefaultDragVisual end) end\nif dv ~= nil then pcall(dmGhostHook, dv, [[enter]]) end\nend\nn = n - (-1)\nend\nreturn n\nend\nlocal function dmHooks()\nlocal a = dmHookCls()\nlocal b = dmHookArea()\nreturn a, b\nend\nlocal nDmWU = 0\nlocal function dmWULog(s)\nif nDmWU == 120 then return end\nnDmWU = nDmWU - (-1)\nlocal h = io.open([[D:/DSH/.tools/_ghost5.txt]], [[a]])\nif h ~= nil then\nh:write(tostring(s) .. string.char(10))\nh:close()\nend\nend\n-- Wrapper on WidgetUtils.SetCollapsedOrSelfHitTestInvisible.\n-- This is a pure-Lua module function, so wrapping it takes effect immediately\n-- (unlike UFunction-bound class methods, whose pointers UnLua caches at bind time).\n-- During drag, RoomDisplay collapses the avatar (ImgIconNew) through this call,\n-- which is why the drag ghost showed only the ring and the heart.\n-- IMPORTANT: another bundled Lua module also wraps this same function and\n-- rawset()s its own wrapper on top, turning the b flag into false for any name\n-- ending with the New suffix. Our wrapper would then never see b == true. So we\n-- must stay on the OUTERMOST layer, which is why the marker below is re-checked\n-- every tick instead of being a one-shot flag.\nlocal tbWUFn = {}\nlocal nDmWUInst = 0\nlocal nDmWUCall = 0\nlocal function dmWUSame(f)\nif type(f) ~= [[function]] then return false end\nif tbWUFn[f] == true then return true end\nreturn false\nend\nlocal function dmWU()\nnDmWUCall = nDmWUCall - (-1)\nif nDmWUCall == 300 then dmWULog([[HEARTBEAT]]) end\nlocal W = WidgetUtils\nif type(W) ~= [[table]] then return 0 end\nlocal old = W.SetCollapsedOrSelfHitTestInvisible\nif type(old) ~= [[function]] then return 0 end\nif dmWUSame(old) then return 0 end\nlocal nf = function(obj, b, c)\nif b == true and obj ~= nil then\nlocal okn, nm = pcall(function() return obj:GetName() end)\nif okn and type(nm) == [[string]] and nm == [[ImgIconNew]] then\npcall(function() obj:SetVisibility(4) end)\npcall(function() obj:SetRenderOpacity(1) end)\ndmWULog([[BLOCK ]] .. nm)\nreturn\nend\nend\nreturn old(obj, b, c)\nend\ntbWUFn[nf] = true\nW.SetCollapsedOrSelfHitTestInvisible = nf\nnDmWUInst = nDmWUInst - (-1)\nif nDmWUInst == 1 then\ndmWULog([[INSTALLED]])\nelse\ndmWULog([[REINSTALL n=]] .. tostring(nDmWUInst))\nend\nreturn 1\nend\nlocal function dmForce()\nlocal ok1, r1 = pcall(dmNpc)\nlocal ok2, r2 = pcall(dmRooms)\nlocal ok3, r3 = pcall(dmRiTick)\nlocal ok9, r9 = pcall(dmWU)\nreturn ok1, r1, ok2, r2, ok3, r3, ok9, r9\nend\nlocal function dmWrap()\nlocal ui = UI\nif type(ui) ~= [[table]] or type(ui.tbWidget) ~= [[table]] then return end\nfor _, v in pairs(ui.tbWidget) do\nif type(v) == [[table]] and type(v.sName) == [[string]] and string.lower(v.sName) == [[dormmap]] and not v.__mikuDormAvatar then\nv.__mikuDormAvatar = true\nlocal oldShow = v.ShowFloor\nif type(oldShow) == [[function]] then\nv.ShowFloor = function(self, ...)\nlocal r = table.pack(oldShow(self, ...))\npcall(dmForce)\nreturn table.unpack(r, 1, r.n)\nend\nend\nend\nend\nend\nlocal dmLastT = 0\nlocal dmBusy = false\nlocal function dmTick()\nif dmBusy then return end\nif _G.MikuPlotToken ~= nil then return end\ndmBusy = true\nlocal t = os.clock()\nlocal dt = t - dmLastT\nif math.max(dt, 0.05) == dt then\ndmLastT = t\npcall(dmForce)\nend\ndmBusy = false\nend\nlocal function dmArm()\nif _G.bMikuDmTick == true then return end\n_G.bMikuDmTick = true\nlocal ES = _G.EventSystem\nif type(ES) ~= [[table]] or type(ES.Trigger) ~= [[function]] then return end\nlocal oldTrig = ES.Trigger\nES.Trigger = function(e, ...)\nlocal r = table.pack(oldTrig(e, ...))\npcall(dmTick)\nreturn table.unpack(r, 1, r.n)\nend\nend\nlocal function dmHook(name, flag)\nlocal ui = UI\nif type(ui) ~= [[table]] then return end\nif type(ui[name]) ~= [[function]] then return end\nif _G[flag] then return end\n_G[flag] = true\nlocal old = ui[name]\nui[name] = function(...)\nlocal r = table.pack(old(...))\npcall(dmWrap)\npcall(dmForce)\npcall(dmArm)\nreturn table.unpack(r, 1, r.n)\nend\nend\nlocal function dmFix()\ndmHook([[Open]], [[bMikuDormAvatarOpen]])\ndmHook([[SafeOpen]], [[bMikuDormAvatarSafe]])\ndmWrap()\ndmForce()\ndmArm()\nend\npcall(dmFix)\n_G.__dmDbg = function() return #tbDmList, nDmFix, nScan, nDeepLog end\n";


	public const string LoginGuard = "-- LoginGuard: the stock login flow opens umg_common_connection (full screen\n-- spinner over the lobby) and closes it when the async preload finishes. If a\n-- preloaded package is missing from the paks, that callback never fires and the\n-- spinner stays forever over an otherwise usable lobby.\n-- Guard: once the lobby widget (main) is open and the connection overlay has\n-- stayed open for 8 seconds, close it with UI.CloseByName and log the event.\nlocal LOG = [[D:/DSH/.tools/loginguard.log]]\nlocal function lg(s)\nlocal f = io.open(LOG, [[a]])\nif f then\nf:write(os.date([[%H:%M:%S]]))\nf:write([[ ]])\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend\nlocal lgAt = 0\nlocal function lgScan()\nlocal U = UI\nif type(U) ~= [[table]] then return end\nlocal tb = U.tbWidget\nif type(tb) ~= [[table]] then return end\nlocal conn = nil\nlocal bMain = false\nfor _, w in pairs(tb) do\nif type(w) == [[table]] then\nlocal sn = w.sName\nif type(sn) == [[string]] then\nif sn == [[connection]] and w.__bOpen == true then conn = w end\nif sn == [[main]] and w.__bOpen == true then bMain = true end\nend\nend\nend\nif conn == nil then return end\nif bMain ~= true then return end\nlocal t = os.clock()\nif type(conn.nMikuSeen) ~= [[number]] then\nconn.nMikuSeen = t\nreturn\nend\nlocal dt = t - conn.nMikuSeen\nif math.max(dt, 8) ~= dt then return end\nconn.nMikuSeen = nil\nif type(U.CloseByName) == [[function]] then pcall(U.CloseByName, [[connection]]) end\nlg([[closed connection overlay after ]] .. tostring(math.floor(dt)) .. [[s]])\nend\nlocal function lgTick()\nlocal t = os.clock()\nlocal dt = t - lgAt\nif math.max(dt, 1) ~= dt then return end\nlgAt = t\npcall(lgScan)\nend\nlocal tbLgHk = {}\nlocal function lgHook()\nlocal oh, om, oc = debug.gethook()\nif type(oh) == [[function]] and tbLgHk[oh] == true then return end\nlocal n = oc\nif type(n) ~= [[number]] then n = 20000 end\nif n == 0 then n = 20000 end\nlocal m = om\nif type(m) ~= [[string]] then m = [[]] end\nlocal chain = function()\nif type(oh) == [[function]] then pcall(oh) end\nlgTick()\nend\ntbLgHk[chain] = true\npcall(debug.sethook, chain, m, n)\nend\npcall(lgScan)\npcall(lgHook)";

	public const string ThrowChallengeContinue = "-- ThrowChallengeContinue: 冰趣蜜弹「连战」续航补丁。\n-- 原生 EndGame 在挑战时间还有剩余时会走 GameSettlement、ChangeChallengeLevel、\n-- SkipToSelect（结算界面、换关、回选关）。这里保留原生的换关与回选关——\n-- 少女由原生 SkipToSelect 里的 HandleOrderProgress(TransToDialogue) 摆位、\n-- 再由原生进入序列切镜头——只跳过结算弹窗，避免打完一关就整局结算。\n-- 挑战时间耗尽或非连战模式仍走原流程，正常整局结算。\nlocal LOG = [[D:/DSH/.tools/throwchallenge.log]]\nlocal function tcl(s)\nlocal f = io.open(LOG, [[a]])\nif f then\nf:write(os.date([[%H:%M:%S]]))\nf:write([[ ]])\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend\nlocal nMark = 0\nlocal nCnt = 0\nlocal function tcPatch(m)\nif type(m) ~= [[table]] then return end\nif m.bMikuTCC == true then return end\nif type(m.EndGame) ~= [[function]] then return end\nm.bMikuTCC = true\nlocal fnEnd = m.EndGame\ntcl([[installed]])\nm.EndGame = function(self, ...)\nif tonumber(self.GameMode) == 2 and self.bMikuTCRun ~= true then\nlocal ok, remain = pcall(function() return self:HouseRemainingTime(self.ChallengeTimerExample) end)\nif ok and type(remain) == [[number]] and math.max(remain, 0) == remain and remain ~= 0 then\nlocal t = os.time()\nlocal dt = t - nMark\nif nMark == 0 or math.max(dt, 60) == dt then nCnt = 0 end\nnMark = t\nnCnt = nCnt - (-1)\nif math.max(nCnt, 20) == nCnt then\ntcl([[soak-stop ]] .. tostring(nCnt))\nnCnt = 0\nreturn fnEnd(self, ...)\nend\nself.bMikuTCRun = true\nlocal okC = pcall(function() self:ChangeChallengeLevel() end)\nself.bIsChallenge = true\nlocal okS = pcall(function() self:SkipToSelect(true) end)\nself.bMikuTCRun = nil\ntcl([[continue lvl=]] .. tostring(self.CurLevel) .. [[ remain=]] .. tostring(remain) .. [[ girl=]] .. tostring(self.ChallengeGirlNum) .. [[ gid=]] .. tostring(self.CurGirlData and self.CurGirlData.GirlID) .. [[ chg=]] .. tostring(okC) .. [[ skip=]] .. tostring(okS))\nreturn\nend\nend\nnCnt = 0\nlocal okr, rv = pcall(function() return self:HouseRemainingTime(self.ChallengeTimerExample) end)\ntcl([[normal-end mode=]] .. tostring(self.GameMode) .. [[ remain=]] .. tostring(rv))\nreturn fnEnd(self, ...)\nend\nend\nlocal function tcApply()\nlocal E = _G.HouseMiniGameEntrence\nif type(E) ~= [[table]] then return end\nif type(E.GetCurGameMgr) == [[function]] then\nlocal ok, m = pcall(E.GetCurGameMgr, E)\nif ok then tcPatch(m) end\nend\ntcPatch(E.CurGameMgr)\nend\npcall(tcApply)\nif _G.bMikuTCOpen ~= true and type(UI) == [[table]] and type(UI.Open) == [[function]] then\n_G.bMikuTCOpen = true\nlocal fnOpen = UI.Open\nUI.Open = function(...)\nlocal r = table.pack(fnOpen(...))\npcall(tcApply)\nreturn table.unpack(r, 1, r.n)\nend\nend\nif _G.bMikuTCCall ~= true and type(UI) == [[table]] and type(UI.Call2) == [[function]] then\n_G.bMikuTCCall = true\nlocal fnCall2 = UI.Call2\nUI.Call2 = function(...)\nlocal r = table.pack(fnCall2(...))\npcall(tcApply)\nreturn table.unpack(r, 1, r.n)\nend\nend\n";

	/// <summary>
	/// Repression4NodeFix（第 413 节）：复施黎明战幕地图上的「临时阵地」小节点不显示。
	/// 地图只有三个大图标（闪击/驻防/重装战线），Dot*_* 小节点恒为 Collapsed，
	/// 看起来像缺关卡。真因：umg_repression4_main.UpdateInfo 用
	/// WidgetUtils.Visible 点亮这些节点，而本构建里 Visible 对 GameCanvasPanel
	/// 走了动画事件分支后静默返回、可见性没落地；直接 SetVisibility(0) 立即可见。
	/// 这里只对名字匹配 Dot数字_数字 的控件补一刀，外加打开 repression4 后
	/// 按当前难度的 tbLevelBranch 逐格刷一次，别的界面完全不受影响。
	/// </summary>
	public const string Repression4NodeFix = "-- Repression4NodeFix：复施黎明战幕地图上「临时阵地」小节点不显示。\n-- 现象：地图只有 3 个大图标（闪击/驻防/重装战线），Dot*_* 小节点恒为 Collapsed，\n--       看起来像缺关卡；其实右侧面板能正常打开，功能没坏。\n-- 真因（第 413 节实测）：节点显隐由 umg_repression4_main.UpdateInfo 负责，\n--       它对每格调 WidgetUtils.Visible(Dot..i.._..j)；本构建里这些\n--       GameCanvasPanel 容器带进场动画，Visible 走了动画事件分支后直接返回，\n--       可见性没落地（同一函数对普通 TextBlock 正常）。直接 SetVisibility 有效。\n-- 修法：不碰被众多补丁层层包装的 UI.Open / WidgetUtils.Visible，\n--       改钩 Repression_Chapter.GetChapterCfgByDifficult——UpdateInfo 每帧都会\n--       先调它取配置，钩子返回后顺手按 tbLevelBranch 把六格节点刷成应该的样子\n--       （分支为 0 的空位折叠）。0.5 秒节流，只在 repression4 界面生效。\n-- 注意：钩子里必须用包装前保存的原函数取配置，否则会自递归。\nlocal LOG = [[D:/DSH/.tools/rp4node.log]]\nlocal function rp4log(s)\nlocal ok, f = pcall(io.open, LOG, [[a]])\nif ok and f then\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend\nlocal rp4orig = nil\nlocal rp4last = 0\nlocal function rp4apply(inst)\nlocal obj = inst and inst.Object\nif not obj then return 0 end\nlocal C = _G.Repression_Chapter\nif type(C) ~= [[table]] or type(rp4orig) ~= [[function]] then return 0 end\nlocal ok, cfg = pcall(rp4orig, C.GetChapterDifficult())\nif not ok or type(cfg) ~= [[table]] then return 0 end\nlocal n = 0\nfor i, ch in ipairs(cfg) do\nlocal br = ch and ch.tbLevelBranch\nif type(br) == [[table]] then\nfor j = 1, 4 do\nlocal key = [[Dot]] .. i .. [[_]] .. j\nlocal d = obj[key]\nif d then\nlocal v = br[j]\nlocal shown = false\nif type(v) == [[number]] and math.max(v, 0) == v and v ~= 0 then shown = true end\npcall(function() d:SetVisibility(shown and 0 or 1) end)\nif shown then n = n - (-1) end\nend\nend\nend\nend\nreturn n\nend\nlocal function rp4tick()\nlocal t = os.clock()\nlocal dt = t - rp4last\nif math.max(dt, 0.5) ~= dt then return end\nrp4last = t\nlocal U = _G.UI\nlocal top = U and U.GetTop and U.GetTop()\nif type(top) == [[table]] and top.sName == [[repression4]] then\nrp4apply(top)\nend\nend\nif _G.bMikuRp4Fix ~= true then\nlocal C = _G.Repression_Chapter\nif type(C) == [[table]] and type(C.GetChapterCfgByDifficult) == [[function]] then\nrp4orig = C.GetChapterCfgByDifficult\nC.GetChapterCfgByDifficult = function(...)\nlocal cfg = rp4orig(...)\npcall(rp4tick)\nreturn cfg\nend\n_G.bMikuRp4Fix = true\nrp4log([[installed]])\nend\nend\n";

	private static List<(string Name, string Lua)> Build(long floor, uint eraId, uint rawEra)
	{
		List<(string, string)> list = new List<(string, string)>();
		string floorText = floor.ToString(CultureInfo.InvariantCulture);
		string eraText = rawEra.ToString(CultureInfo.InvariantCulture);
		string tryIds = (rawEra != 0) ? TryIdsLiteral(rawEra) : "";
		HashSet<string> hashSet = Disabled();
		foreach (string name in Order)
		{
			if (hashSet.Contains(name))
			{
				continue;
			}
			string? lua;
			switch (name)
			{
			case "GlobalTimeWindows":
				lua = "local function gtwfix()\nif not IsInTime then return end\nlocal floor = MIKU_ACTIVATION_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuGlobalTimePatched and MikuGlobalEraFloor == floor and MikuGlobalEraId == era then return end\nMikuGlobalTimePatched = true\nMikuGlobalEraFloor = floor\nMikuGlobalEraId = era\nlocal old = MikuRawIsInTime or IsInTime\nMikuRawIsInTime = old\nIsInTime = function(nStart, nEnd, nNow)\nif old(nStart, nEnd, nNow) then return true end\nif not nEnd or nEnd == -1 or nEnd == 0 then return false end\nif floor ~= 0 and math.max(nEnd, floor) ~= nEnd then return false end\nlocal t = nNow or GetTime()\nif old(-1, nEnd, t) then return false end\nreturn true\nend\nif Activity and Activity.LoadActivitiesConfig and Activity.bMikuEraReset ~= era then\nActivity.bMikuEraReset = era\nActivity.LoadActivitiesConfig()\nend\nend\ngtwfix()".Replace("MIKU_ACTIVATION_FLOOR", floorText, StringComparison.Ordinal).Replace("MIKU_ACT_ERA", eraText, StringComparison.Ordinal);
				break;
			case "DefaultModeWindows":
				lua = "local era = MIKU_ACT_ERA\n_G.MikuDmEra = era\n_G.MikuDmProbe = tostring(era) .. [[/]] .. type(era)\n_G.bMikuHideEntry = MIKU_HIDE_ENTRY\n-- 主线故事「往迹」左栏第一项对应 chapter_group 里 ID=100 的占位组：\n-- 该组 ChapterGroupDes 是空串、ChapterID 也是空，客户端查不到名字就按当前期号\n-- 拼兜底 key ui.TxtDLC{期号}.BtnStory。3.2.0 语言包里 7~12 与 24~36 期有这个键，\n-- 1~6 期只有 Title，于是那一格直接把 key 原文当文案画了出来。\n-- 这里给缺 BtnStory 的期号补上：优先取同期 Title，其次 Story。\n-- 放在脚本最前面执行：后面任何一处运行期报错都不影响它生效。\nlocal function dmLangFix()\nlocal L = _G.Localization\nif type(L) ~= [[table]] then return end\nlocal tb = L.tbData\nif type(tb) ~= [[table]] then return end\nlocal pre = [[ui.TxtDLC]]\nfor e = 1, 40 do\nlocal p = pre .. e\nlocal k = p .. [[.BtnStory]]\nif tb[k] == nil then\nlocal ts = [[.Title]]\nlocal ss = [[.Story]]\nlocal v = tb[p .. ts]\nif v == nil then v = tb[p .. ss] end\nif v ~= nil then tb[k] = v end\nend\nend\nend\npcall(dmLangFix)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuLangHook ~= true then\n_G.bMikuLangHook = true\nlocal oldOpenL = UI.Open\nUI.Open = function(...) pcall(dmLangFix) local rL = table.pack(oldOpenL(...)) pcall(dmLangFix) return table.unpack(rL, 1, rL.n) end\nend\n-- 本期「队员试用」活动 id，由服务端按 dlc_activities.TryIds 直接注入。\n-- 不再依赖客户端 DLC_Logic 的 conf.tbTryID：第 2/3 期那张表在 3.2.0 客户端里是空的，\n-- 导致 gtry() 直接 return，试用行的 nStartTime 还是 2023 年，被时间窗过滤掉。\nlocal MikuTryList = {MIKU_TRY_IDS}\n-- 光把行时间改成「现在」不够：gacha_try 这一类在 IsOpen 里还要过\n-- GachaTry.IsGachaTryAvalible，条件不满足照样返回 false，整组就被列表丢掉。\n-- 客户端有一套现成的强制开放表 tbDfwForceAct（DlcFuncWindowFix 用它包 IsOpen），\n-- 把本期的试用 id 塞进去最省事；再自己包一层 IsOpen 兜底，防止那套包装还没装上。\nlocal function dmTryForceSet()\nlocal set = _G.tbMikuTryForce\nif type(set) ~= [[table]] then set = {} _G.tbMikuTryForce = set end\nlocal prev = _G.tbMikuTryPrev\nif type(prev) == [[table]] then\nfor _, id in ipairs(prev) do\nset[id] = nil\nlocal tf0 = _G.tbDfwForceAct\nif type(tf0) == [[table]] then tf0[id] = nil end\nend\nend\n_G.tbMikuTryPrev = MikuTryList\nlocal tf = _G.tbDfwForceAct\nif type(tf) ~= [[table]] then tf = {} _G.tbDfwForceAct = tf end\nfor _, id in ipairs(MikuTryList) do\nset[id] = true\ntf[id] = true\nend\nend\nlocal function dmTryHookOpen()\nif type(Activity) ~= [[table]] or type(Activity.IsOpen) ~= [[function]] then return end\nif Activity.bMikuTryForce == true then return end\nActivity.bMikuTryForce = true\nlocal oldIsOpen = Activity.IsOpen\nActivity.IsOpen = function(nId, ...)\nlocal set = _G.tbMikuTryForce\nif type(set) == [[table]] and set[nId] then return true, {} end\nreturn oldIsOpen(nId, ...)\nend\nend\nlocal function dmTry()\ndmTryForceSet()\ndmTryHookOpen()\nif type(Activity) ~= [[table]] or type(Activity.tbConfig) ~= [[table]] then return end\nif #MikuTryList == 0 then return end\nlocal now = GetTime()\nlocal off1 = 3600\nlocal off2 = -2592000\nfor _, id in ipairs(MikuTryList) do\nlocal row = Activity.tbConfig[id]\nif type(row) == [[table]] then\nrow.nStartTime = now - off1\nrow.nEndTime = now - off2\nrow.tbCondition = {}\nend\nend\nend\npcall(dmTry)\nif Activity and type(Activity.LoadActivitiesConfig) == [[function]] and Activity.bMikuDmTryHook ~= true then\nActivity.bMikuDmTryHook = true\nlocal oldLoad2 = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) local r2 = oldLoad2(...) pcall(dmTry) return r2 end\nend\nlocal function dmAct()\nif not Activity or type(Activity.GetAllGroup) ~= [[function]] then return end\nif Activity.bMikuDmList ~= true then\nActivity.bMikuDmList = true\nlocal oldg = Activity.GetAllGroup\nActivity.GetAllGroup = function(...)\nlocal r = oldg(...)\nif type(r) ~= [[table]] then return r end\nif _G.MikuDmEra ~= 0 then return r end\nlocal keep = {}\nfor i = 1, #r do\nlocal e = r[i]\nif type(e) == [[table]] then\nfor j = 1, #e do\nlocal row = e[j]\nif type(row) == [[table]] then\nif tostring(row.sClass) == [[signin_monthly]] then table.insert(keep, e) break end\nend\nend\nend\nend\nreturn keep\nend\nend\nend\n-- 主界面「战斗」左上角那块版本活动入口（BtnDLC 按钮与 BlankDLC 大图）。\n-- 以前是「pin 任意期都整块取消」，结果切到 5 期及以后入口也不出来（第 231 节）。\n-- 现在按服务端下发的开关走：MIKU_HIDE_ENTRY=true 才收（游戏默认 / 1~4 期 / EraLite），\n-- 其余期交给游戏原生 ShowBtnDlc 显示，服务端只负责把进入窗口推到现在。\nlocal function dmHideEntry()\nlocal main\nfor _, x in pairs(UI.tbWidget) do\nif type(x) == [[table]] and tostring(x.sName) == [[main]] and x.BtnDLC then main = x break end\nend\nif not main then return end\npcall(function() WidgetUtils.Collapsed(main.BtnDLC) end)\nif main.BlankDLC then pcall(function() WidgetUtils.Collapsed(main.BlankDLC) end) end\nif main.NewDLC then pcall(function() WidgetUtils.Collapsed(main.NewDLC) end) end\nif main.DLCLock then pcall(function() WidgetUtils.Collapsed(main.DLCLock) end) end\nif main.Image_48 then pcall(function() WidgetUtils.Collapsed(main.Image_48) end) end\nif main.ImgLock then pcall(function() WidgetUtils.Collapsed(main.ImgLock) end) end\nif main.BtnPreview then pcall(function() WidgetUtils.Collapsed(main.BtnPreview) end) end\nif main.TipTime then pcall(function() WidgetUtils.Collapsed(main.TipTime) end) end\nif main.TextBlock_82 then pcall(function() WidgetUtils.Collapsed(main.TextBlock_82) end) end\nif main.bMikuDlcHidden ~= true and type(main.ShowBtnDlc) == [[function]] then\nmain.bMikuDlcHidden = true\nmain.bMikuDlcShow = main.ShowBtnDlc\nlocal old = main.ShowBtnDlc\nmain.ShowBtnDlc = function(self, ...) local r = old(self, ...) pcall(WidgetUtils.Collapsed, self.BtnDLC) pcall(dmHideEntry) return r end\nend\nend\nlocal function dmShowEntry()\nlocal main\nfor _, x in pairs(UI.tbWidget) do\nif type(x) == [[table]] and tostring(x.sName) == [[main]] and x.BtnDLC then main = x break end\nend\nif not main then return end\n-- 之前可能被 dmHideEntry 包过 ShowBtnDlc（包完还会自己再收一次），这里拆掉再显式调一次。\nif main.bMikuDlcHidden == true and type(main.bMikuDlcShow) == [[function]] then\nmain.ShowBtnDlc = main.bMikuDlcShow\nmain.bMikuDlcHidden = nil\nmain.bMikuDlcShow = nil\nend\nif type(main.ShowBtnDlc) == [[function]] then pcall(function() main:ShowBtnDlc() end) end\nend\nif _G.bMikuHideEntry then pcall(dmHideEntry) else pcall(dmShowEntry) end\n-- 活动列表（左栏）重复条目修复：\n-- activity 窗口的 ShowGroups 每次调用都往 ListActivity 里 AddChild，自身不清空。\n-- 正常游玩时界面只建一次看不出来；我们反复切期/replay 触发多次刷新，\n-- 就会累成「队员试用」三行同名。这里给 ShowGroups 包一层，调用前先清子项。\nlocal function dmShopTimeFix()\nlocal SL = ShopLogic\nif type(SL) ~= [[table]] then return end\nlocal tb = SL.tbShopTabConf\nif type(tb) ~= [[table]] then return end\nlocal now = GetTime()\nif type(now) ~= [[number]] or now == 0 then return end\nfor k, c in pairs(tb) do\nlocal fid = tonumber(k) or 0\nif type(c) == [[table]] and c.bDLCShop == true and not (fid == 401 or fid == 402 or fid == 403 or fid == 404) then\nlocal e = tonumber(c.nEnd) or 0\nif e ~= 0 and math.max(e, 0) == e and math.min(e, now) == e then\nc.nBegin = now - 3600\nc.nEnd = now - (-2592000)\nend\nend\nend\nend\n_G.MikuDmShopTime = dmShopTimeFix\n\n-- 潮流体验（时装试用）整块摘掉：不管切到哪一期都不出现。\n-- 活动列表左栏那一段 = gacha_try 里 sGroupName 为 activity.TxtActivityTry2 的行。\n-- 个别期把「灼阳假期」(TxtActivityTry2_4) 标成新品体验，但它和浅憩之处/静聆潮韵\n-- 是同一组时装试用入口，一起摘；否则会剩一个孤零零的同名条目。\n-- nHide 字段对这一层无效（实测置 1 后 GetCaseByGroup 照旧返回），只能改返回值。\nlocal function dmFashion()\nlocal A = Activity\nif type(A) ~= [[table]] then return end\nif type(A.GetCaseByGroup) ~= [[function]] then return end\nif A.bMikuFashion == true then return end\nA.bMikuFashion = true\nlocal tbGrp = {}\ntbGrp[ [[activity.TxtActivityTry2]] ] = true\nlocal tbTitle = {}\ntbTitle[ [[activity.TxtActivityTry2_40]] ] = true\ntbTitle[ [[activity.TxtActivityTry2_41]] ] = true\ntbTitle[ [[activity.TxtActivityTry2_4]] ] = true\nlocal function dmFashionRow(row)\nif type(row) ~= [[table]] then return false end\nlocal g = row.sGroupName\nif type(g) == [[string]] and tbGrp[g] then return true end\nlocal t = row.sTitleDes\nif type(t) == [[string]] and tbTitle[t] then return true end\nreturn false\nend\nlocal function dmFashionFilt(r)\nif type(r) ~= [[table]] then return r, 0 end\nlocal keep = {}\nfor i = 1, #r do\nlocal row = r[i]\nif not dmFashionRow(row) then table.insert(keep, row) end\nend\nreturn keep, #r - #keep\nend\nlocal oldCase = A.GetCaseByGroup\nA.GetCaseByGroup = function(id, ...)\nlocal r = oldCase(id, ...)\nif type(r) ~= [[table]] then return r end\nlocal keep, nDrop = dmFashionFilt(r)\nif nDrop ~= 0 then return keep end\nreturn r\nend\nif type(A.GetSortAllGroup) == [[function]] and A.bMikuFashionSort ~= true then\nA.bMikuFashionSort = true\nlocal oldSort = A.GetSortAllGroup\nA.GetSortAllGroup = function(...)\nlocal r = oldSort(...)\nif type(r) ~= [[table]] then return r end\nlocal keep = {}\nfor i = 1, #r do\nlocal e = r[i]\nlocal gid = type(e) == [[table]] and tonumber(e[1]) or nil\nlocal bKeep = true\nif gid then\nlocal ok, rows = pcall(oldCase, gid)\nif ok and type(rows) == [[table]] and #rows ~= 0 then\nlocal _, nDrop = dmFashionFilt(rows)\nif nDrop == #rows then bKeep = false end\nend\nend\nif bKeep then table.insert(keep, e) end\nend\nreturn keep\nend\nend\nend\n_G.MikuDmFashion = dmFashion\npcall(dmFashion)\n\n-- 心动派对（Richmen）补配置：旧期的客户端 conf 里被裁掉了这一项，\n-- 导致 DLC_Logic.Richmen.nConfId = nil，点开窗口时 RichmanLogic.MapID 还是 nil，\n-- GetMapDay 之类直接算 nil 报错，整片白屏。第 9 期对应棋类活动 id 3（地图 1），\n-- 第 30 期原生就是 4。只补这两期，别的期不动。\nlocal function dmRichmen()\nlocal D = DLC_Logic\nif type(D) ~= [[table]] then return end\nlocal ok, conf = pcall(D.GetCurConf)\nif not ok or type(conf) ~= [[table]] then return end\nlocal era = tonumber(conf.nId) or 0\nlocal nWant = 0\nif era == 9 then nWant = 3 elseif era == 30 then nWant = 4 end\nif nWant == 0 then return end\nlocal t = conf.tbDlcFuncConfId\nif type(t) ~= [[table]] then t = {} conf.tbDlcFuncConfId = t end\nif t.Richmen == nil then t.Richmen = nWant end\nlocal F = D.Richmen\nif type(F) == [[table]] and F.nConfId == nil then F.nConfId = t.Richmen end\nend\n_G.MikuDmRichmen = dmRichmen\npcall(dmRichmen)\n-- 心动派对（RichManLove）读取保护。\n-- 这一期客户端的该模块没有任何存档属性（GetAttribute(121,10) 为空），\n-- 界面一打开就 nil 运算报错，表现为整屏白掉。这里给读接口加保护壳：\n-- 正常时原样返回，出错时给安全默认值，并把报错写进 loveguard.log 方便定位。\nlocal function dmLoveGuard()\nlocal L = _G.RichManLoveLogic\nif type(L) ~= [[table]] then return end\nif L.bMikuLoveGuard == true then return end\nL.bMikuLoveGuard = true\nlocal pack = table.pack\nlocal unpack = table.unpack or unpack\nif type(pack) ~= [[function]] then\npack = function(...) return { n = select([[#]], ...), ... } end\nend\nlocal function dmLoveLog(nm, err)\nlocal f = io.open([[D:/GAME/SnowBreak/S1/Config/Logs/loveguard.log]], [[a]])\nif f then f:write(tostring(nm) .. [[ : ]] .. tostring(err) .. string.char(10)) f:close() end\nend\nlocal function dmLoveMapId()\nlocal R = _G.RichmanLogic\nlocal v = R and tonumber(R.MapID)\nif v and v ~= 0 then return v end\nlocal RL = L.MapID\nif type(RL) == [[number]] and RL ~= 0 then return RL end\nreturn 1\nend\nlocal function dmLoveTrace(nm, err)\nlocal tb = select(2, pcall(debug.traceback, [[ ]], 3))\npcall(dmLoveLog, nm, tostring(err) .. [[ | ]] .. tostring(tb))\nend\nlocal WR = _G.MikuDmLoveWrapped\nif type(WR) ~= [[table]] then WR = {} _G.MikuDmLoveWrapped = WR end\nlocal function dmLoveWrapMap(nm, fb)\nlocal f = L[nm]\nif type(f) ~= [[function]] or WR[nm] == true then return end\nWR[nm] = true\nL[nm] = function(a, ...)\nif type(a) ~= [[number]] or a == 0 then a = dmLoveMapId() end\nlocal r = pack(pcall(f, a, ...))\nif r[1] then return unpack(r, 2, r.n) end\npcall(dmLoveTrace, nm, r[2])\nif type(fb) == [[function]] then return fb() end\nreturn fb\nend\nend\nlocal function dmLoveWrap(nm, fb)\nlocal f = L[nm]\nif type(f) ~= [[function]] or WR[nm] == true then return end\nWR[nm] = true\nL[nm] = function(...)\nlocal r = pack(pcall(f, ...))\nif r[1] then return unpack(r, 2, r.n) end\npcall(dmLoveTrace, nm, r[2])\nif type(fb) == [[function]] then return fb() end\nreturn fb\nend\nend\ndmLoveWrapMap([[GetMapDay]], 1)\ndmLoveWrapMap([[GetMapIsEntered]], false)\ndmLoveWrapMap([[GetMapAccumulatedMoney]], 0)\ndmLoveWrap([[GetSaveData]], [[{}]])\ndmLoveWrap([[GetRichmanParam]], function() return {} end)\ndmLoveWrap([[GetAllUnlockEnd]], function() return {} end)\ndmLoveWrap([[GetEndUnlockNum]], 0)\ndmLoveWrap([[HasNew]], false)\ndmLoveWrap([[IsReceiveDice]], false)\ndmLoveWrap([[IsReceiveEnd]], false)\nlocal PA = _G.MikuDmLoveDeepSet\nif type(PA) ~= [[table]] then PA = {} _G.MikuDmLoveDeepSet = PA end\nlocal function dmLoveDeep()\nlocal mods = { _G.RichManLoveLogic, _G.DLC_RichManLove }\nfor mi = 1, 2 do\nlocal mod = mods[mi]\nif type(mod) == [[table]] then\nlocal keys = {}\nfor k, v in pairs(mod) do\nif type(v) == [[function]] and type(k) == [[string]] then table.insert(keys, k) end\nend\nfor i = 1, #keys do\nlocal nm = keys[i]\nlocal f = mod[nm]\nlocal tag = tostring(mi) .. [[/]] .. nm\nif type(f) == [[function]] and PA[tag] ~= true then\nPA[tag] = true\nmod[nm] = function(...)\nlocal r = pack(pcall(f, ...))\nif r[1] then return unpack(r, 2, r.n) end\npcall(dmLoveTrace, tag, r[2])\nreturn nil\nend\nend\nend\nend\nend\nend\npcall(dmLoveDeep)\nend\n_G.MikuDmLoveGuard = dmLoveGuard\npcall(dmLoveGuard)\n-- 心动派对第一次打开会同时弹「活动说明」大白页和一条新手引导（60004）。\n-- 那条引导在等棋盘上的某个按钮，棋盘没建好就一直挂全屏遮罩并吃掉输入，\n-- 表现就是整屏发白、点不动、连 ESC 都关不掉（第 245 节）。这里把这条引导拦掉。\nlocal function dmLoveGuide()\nlocal G = _G.GuideLogic\nif type(G) ~= [[table]] or type(G.BeginGuide) ~= [[function]] then return end\nif G.bMikuLoveSkip == true then return end\nG.bMikuLoveSkip = true\nlocal oldBegin = G.BeginGuide\nG.BeginGuide = function(p1, p2, ...)\nif tonumber(p1) == 60004 or tonumber(p2) == 60004 then return end\nreturn oldBegin(p1, p2, ...)\nend\nend\n_G.MikuDmLoveGuide = dmLoveGuide\npcall(dmLoveGuide)\n-- 兜底：万一引导已经从别的入口起来了，开完心动派对窗口顺手关掉它。\nlocal function dmLoveGuideClean()\nif type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] then return end\nif _G.bMikuLoveGuideHook == true then return end\n_G.bMikuLoveGuideHook = true\nlocal oldOpenG = UI.Open\nUI.Open = function(a, ...)\nlocal rG = table.pack(oldOpenG(a, ...))\nif a == [[richmanlove]] or a == [[richmanlovegame]] or a == [[RichmanLove]] then\nlocal ok1, b1 = pcall(UI.IsOpen, [[guide]])\nif ok1 and b1 then pcall(UI.CloseByName, [[guide]]) end\nlocal ok2, b2 = pcall(UI.IsOpen, [[Guide]])\nif ok2 and b2 then pcall(UI.CloseByName, [[Guide]]) end\nend\nif a == [[Guide]] then pcall(dmLoveGuide) end\nreturn table.unpack(rG, 1, rG.n)\nend\nend\npcall(dmLoveGuideClean)\n-- 版本活动入口那张大图（控件 BlankDLC）用的是 DLC_Logic 配置里的 nIcon，\n-- 老版本的 Localization 贴图早就从 3.2 客户端里剪掉了，取到的是空图 → 战斗左边的白框。\n-- 这里把当期配置的 nIcon 指向客户端自带的最新一期版本海报，白框就没有了。\nlocal function dmBanner()\nlocal res = _G.Resource\nif not res or type(res.tbResource) ~= [[table]] then return end\nif (tonumber(_G.MikuDmEra) or 0) == 0 then return end\nlocal conf\nif DLC_Logic and type(DLC_Logic.GetCurConf) == [[function]] then conf = DLC_Logic.GetCurConf() end\nif type(conf) ~= [[table]] then return end\nlocal icon = tonumber(conf.nIcon)\nif not icon then return end\n-- 只补 3.2.0 客户端里已被裁掉的那两张（第 1/2 期），\n-- 第 3 期及以后的原生海报是完整的，不要覆盖，否则切期会串图。\nif icon ~= 2400001 and icon ~= 2400002 then return end\nres.tbResource[icon] = [[/Game/UI/Picture/Localization/DLC27/gui_dlc27_btn001_01_Mul.gui_dlc27_btn001_01_Mul]]\nend\n-- 第 4 期「斑斓漫想」(活动 69) 的原始三张图 1602235/1602234/1602233 指向\n-- DLC04/pic_activity26_* ，那套贴图在 3.2.0 客户端里已经没了，解析失败就是一块白。\n-- 这里钉成「北境巡游」(活动 61) 那套 1602225/1602224/1602223；\n-- 同时把 1602224 的资源路径写成带子包后缀的正确格式，否则同样解析失败。\n-- 注意必须在 Activity 模块加载之后再钉，登录推送时它往往还是 nil，\n-- 所以挂在 UI.Open 上每次开界面重钉一遍。\nlocal function dmEra4()\nif (tonumber(_G.MikuDmEra) or 0) ~= 4 then return end\n-- 这个客户端里 Activity 只挂在全局上，package.loaded.Activity 恒为 nil，\n-- 之前按 package.loaded 取会直接 return，整段换图逻辑等于没跑。\nlocal A = _G.Activity\nif type(A) ~= [[table]] and package and package.loaded then A = package.loaded.Activity end\nlocal res = _G.Resource\nif res and type(res.tbResource) == [[table]] then\nres.tbResource[1602224] = [[/Game/UI/Picture/Localization/DLC4/pic_activity24_002_Mul.pic_activity24_002_Mul]]\nend\nif type(A) ~= [[table]] then return end\nlocal r = A.tbConfig and A.tbConfig[69]\nif type(r) == [[table]] then r.nBg = 1602225 r.nTitle = 1602224 r.nImgPic = 1602223 end\nlocal g = A.tbModelGroup and A.tbModelGroup[69]\nif type(g) == [[table]] then\nfor _, v in pairs(g) do\nif type(v) == [[table]] then v.nBg = 1602225 v.nTitle = 1602224 v.nImgPic = 1602223 end\nend\nend\n-- 第 7 天奖励格的图标也是同一套缺失贴图\nlocal sg = A.Sign and A.Sign.tbSignShortReward and A.Sign.tbSignShortReward[69]\nif type(sg) == [[table]] then\nlocal d7 = sg[7]\nif type(d7) == [[table]] then d7.param1 = 1602223 end\nend\nend\npcall(dmEra4)\nif UI and type(UI.Open) == [[function]] and _G.bMikuEra4Hook ~= true then\n_G.bMikuEra4Hook = true\nlocal oldOpen4 = UI.Open\nUI.Open = function(...) pcall(dmEra4) local r4 = table.pack(oldOpen4(...)) pcall(dmEra4) return table.unpack(r4, 1, r4.n) end\nend\npcall(dmBanner)\npcall(dmAct)\n-- 活动列表左栏的实际数据源是 GetSortAllGroup（返回 {组id,排序} 列表），\n-- dmAct 包的 GetAllGroup 只影响另一条取值路径，所以之前列表里\n-- 「队员试用 / 新品体验」照旧整组显示。这里按组内是否有 gacha_try 剔除整组。\nlocal function dmSort()\nif type(Activity) ~= [[table]] or type(Activity.GetSortAllGroup) ~= [[function]] then return end\nif Activity.bMikuDmSort == true then return end\nActivity.bMikuDmSort = true\nlocal olds = Activity.GetSortAllGroup\n-- 默认模式下要从活动列表整组摘掉的类：\n--   gacha_try  = 「队员试用 / 新品体验」（当期限定角色试用）；\n--   former_dlc = 「荒城栖花-追忆」这类往期版本追忆（activity.FormerDlc*_title）。\nlocal HIDE = { gacha_try = true, former_dlc = true }\nlocal function isHideGroup(gid)\nlocal ok, rows = pcall(Activity.GetCaseByGroup, gid)\nif not ok or type(rows) ~= [[table]] then return false end\nfor i = 1, #rows do\nlocal row = rows[i]\nif type(row) == [[table]] and HIDE[tostring(row.sClass)] then return true end\nend\nreturn false\nend\nActivity.GetSortAllGroup = function(...)\nlocal r = olds(...)\nif type(r) ~= [[table]] then return r end\nif (tonumber(_G.MikuDmEra) or 0) ~= 0 then return r end\nlocal keep = {}\nfor i = 1, #r do\nlocal e = r[i]\nlocal gid = type(e) == [[table]] and tonumber(e[1]) or nil\nif not gid or not isHideGroup(gid) then table.insert(keep, e) end\nend\nreturn keep\nend\nend\n_G.MikuDmSort = dmSort\npcall(dmSort)".Replace("MIKU_ACT_ERA", eraText, StringComparison.Ordinal).Replace("MIKU_TRY_IDS", tryIds, StringComparison.Ordinal);
				break;
			case "Dlc11StoryLevel":
				lua = Dlc11StoryLevel;
				break;
			case "ShootScoreUnlock":
				lua = ShootScoreUnlock;
				break;
			case "Act283Open":
				lua = Act283Open;
				break;
			case "ChessWin":
				lua = ChessWin;
				break;
			case "VcTrialWindows":
				// 第 351 节：新星开拓「试炼吧」永久开启（客户端侧）。
				// 客户端 trial_timelist 最新一期到 2026-09-13 就结束了，界面按本地表判「已结束」，
				// 不管服务端给什么 id 都显示锁。这里把试炼时间表整表推到「昨天 ~ 2100 年」，
				// 兼容两种行结构：配置原样（StartTime/EndTime 字符串）与读表后（nStartTime/nEndTime 数字）。
				// 命中情况写 D:/DSH/.tools/vctrial.log，方便确认表名（服务端只看这份日志就知道该收紧到哪张表）。
				lua = "local function vcTrial()\n"
					+ "local C = _G.VirCaptureConfig\n"
					+ "if type(C) ~= [[table]] then return false end\n"
					+ "local now = GetTime()\n"
					+ "if type(now) ~= [[number]] or now == 0 then return false end\n"
					+ "local s = now - 86400\n"
					+ "local e = 4102444800\n"
					+ "local hit = {}\n"
					+ "local function one(row)\n"
					+ "if type(row) ~= [[table]] then return false end\n"
					+ "local b = false\n"
					+ "if type(row.nStartTime) == [[number]] then row.nStartTime = s b = true end\n"
					+ "if type(row.nEndTime) == [[number]] then row.nEndTime = e b = true end\n"
					+ "if type(row.nAwardTime) == [[number]] and row.nAwardTime ~= 0 then row.nAwardTime = e end\n"
					+ "if type(row.StartTime) == [[string]] then row.StartTime = os.date([[%Y%m%d%H%M]], s) b = true end\n"
					+ "if type(row.EndTime) == [[string]] then row.EndTime = os.date([[%Y%m%d%H%M]], e) b = true end\n"
					+ "return b\n"
					+ "end\n"
					+ "local function sweep(mod, tag, wild)\n"
					+ "if type(mod) ~= [[table]] then return end\n"
					+ "for k, v in pairs(mod) do\n"
					+ "local kn = tostring(k)\n"
					+ "if type(v) == [[table]] and (wild or string.find(kn, [[rial]])) then\n"
					+ "local n = 0\n"
					+ "for _, row in pairs(v) do\n"
					+ "if one(row) then n = n - (-1) end\n"
					+ "end\n"
					+ "if n ~= 0 then table.insert(hit, tag .. [[.]] .. kn .. [[=]] .. tostring(n)) end\n"
					+ "end\n"
					+ "end\n"
					+ "end\n"
					+ "sweep(C, [[cfg]], false)\n"
					+ "sweep(_G.VirCaptureTrial, [[trial]], true)\n"
					+ "if #hit ~= 0 then\n"
					+ "local f = io.open([[D:/DSH/.tools/vctrial.log]], [[w]])\n"
					+ "if f then\n"
					+ "for i = 1, #hit do f:write(hit[i]) f:write(string.char(10)) end\n"
					+ "f:close()\n"
					+ "end\n"
					+ "end\n"
					+ "return true\n"
					+ "end\n"
					+ "pcall(vcTrial)\n"
					+ "if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuVcTrialHook ~= true then\n"
					+ "_G.bMikuVcTrialHook = true\n"
					+ "local oldOpenT = UI.Open\n"
					+ "UI.Open = function(...) pcall(vcTrial) local rT = table.pack(oldOpenT(...)) pcall(vcTrial) return table.unpack(rT, 1, rT.n) end\n"
					+ "end";
				break;
			case "GachaTryWindows":
				lua = "local function gtry()\nif not Activity or type(Activity.tbConfig) ~= [[table]] then return end\nif not DLC_Logic or type(DLC_Logic.GetCurConf) ~= [[function]] then return end\nlocal floor = MIKU_ACT_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuTryWin2 and MikuTryWinFloor == floor and MikuTryWinEra == era then return end\nlocal conf = DLC_Logic.GetCurConf()\nif type(conf) ~= [[table]] or type(conf.tbTryID) ~= [[table]] then return end\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nif MikuTryOrigEra ~= era then MikuTryOrigEra = era MikuTryOrig = {} MikuTryOrigE = {} end\nMikuTryOrig = MikuTryOrig or {}\nMikuTryOrigE = MikuTryOrigE or {}\nlocal ids = {}\nlocal firstId = nil\nlocal firstStart = nil\nfor _, v in pairs(conf.tbTryID) do\nlocal id = tonumber(v)\nif id then\ntable.insert(ids, id)\nlocal row = Activity.GetActivityConfig(id)\nif type(row) == [[table]] then\nlocal s = tonumber(MikuTryOrig[id])\nif s == nil then\ns = tonumber(row.nStartTime) or 0\nMikuTryOrig[id] = s\nMikuTryOrigE[id] = tonumber(row.nEndTime) or 0\nend\nif firstStart == nil or math.min(firstStart, s) == s then\nfirstStart = s\nfirstId = id\nend\nend\nend\nend\nlocal defaultMode = true\nfor _, id in ipairs(ids) do\nlocal row = Activity.GetActivityConfig(id)\nif type(row) == [[table]] then\nif defaultMode or id == firstId then\nrow.nStartTime = now - off1\nrow.nEndTime = now - off2\nrow.tbCondition = {}\nelse\nrow.nStartTime = MikuTryOrig[id] or row.nStartTime\nrow.nEndTime = MikuTryOrigE[id] or row.nEndTime\nend\nend\nend\nif not defaultMode and firstId then\nMikuTrySkip = {}\nfor _, id in ipairs(ids) do\nif id ~= firstId then MikuTrySkip[id] = true end\nend\nif not Activity.bMikuTryIso then\nActivity.bMikuTryIso = true\nlocal oldIso = Activity.IsOpen\nActivity.IsOpen = function(a, b)\nlocal n = tonumber(a)\nif n and MikuTrySkip and MikuTrySkip[n] then return false end\nreturn oldIso(a, b)\nend\nend\nelse\nMikuTrySkip = nil\nend\nMikuTryWin2 = true\nMikuTryWinFloor = floor\nMikuTryWinEra = era\nend\nMikuTryApply = gtry\ngtry()\nif not MikuTryHooked2 then\nMikuTryHooked2 = true\nif DLC_Logic and DLC_Logic.InitFuncObj then\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) if MikuTryApply then MikuTryApply() end return r end\nend\nif Activity and Activity.LoadActivitiesConfig then\nlocal oldLoad = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) MikuTryWin2 = nil local r = oldLoad(...) if MikuTryApply then MikuTryApply() end return r end\nend\nend".Replace("MIKU_ACT_FLOOR", floorText, StringComparison.Ordinal).Replace("MIKU_ACT_ERA", eraText, StringComparison.Ordinal);
				break;
			case "LevelUpQuiet":
				// 满级（100）时经验条永远"溢出"：客户端在每次经验同步/结算后都会
				// 触发 LevelUp 事件（参数是新旧等级，实测两个都是 100），
				// MainMap 的处理函数 FunctionRouter.ShowLevelUpTip 直接开「账号等级提升」弹窗，
				// 于是每次登录、每把速战都弹一次「升到 100 级」（第 230 节）。
				// 这里只在「目标等级 = 配置里的最高等级」时拦掉这个开窗函数，
				// 其它监听 LevelUp 的逻辑（HUD 刷新等）不动。
				lua = "local function lvMax()\nlocal P = _G.Player\nif type(P) ~= [[table]] then return 0 end\nlocal c = P.tbLevelCfg\nif type(c) ~= [[table]] then return 0 end\nlocal m = 0\nfor k in pairs(c) do\nlocal n = tonumber(k)\nif n and math.max(n, m) == n then m = n end\nend\nreturn m\nend\nlocal function lvQuiet(a, b)\nlocal m = lvMax()\nif m == 0 then return false end\nlocal x = tonumber(a) or 0\nlocal y = tonumber(b) or 0\nreturn math.max(x, y) == m\nend\nlocal function lvFix()\nif type(FunctionRouter) ~= [[table]] then return end\nif type(FunctionRouter.ShowLevelUpTip) ~= [[function]] then return end\nif FunctionRouter.bMikuLvQuiet == true then return end\nFunctionRouter.bMikuLvQuiet = true\nlocal old = FunctionRouter.ShowLevelUpTip\nFunctionRouter.ShowLevelUpTip = function(a, b, ...)\nif lvQuiet(a, b) then\n_G.MikuLvQuietHit = true\nreturn\nend\nreturn old(a, b, ...)\nend\nend\npcall(lvFix)\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuLvQuietHook ~= true then\n_G.bMikuLvQuietHook = true\nlocal oldOpen = UI.Open\nUI.Open = function(...)\npcall(lvFix)\nreturn oldOpen(...)\nend\nend";
				break;
			case "Repression4NodeFix":
				lua = Repression4NodeFix;
				break;
			default:
				lua = ByName(name);
				break;
			}
			if (name == "GachaTryWindows" && EraLite.Is(rawEra))
			{
				lua = lua.Replace("local conf = DLC_Logic.GetCurConf()", "local conf = (rawget(DLC_Logic, [[tbActivityConf]]) or {})[" + eraText + "]");
			}
			if (lua != null)
			{
				// 占位符替换（第 224 节恢复）。11-11 源码回退把这一段整个丢了，
				// 补丁带着 MIKU_ACT_ERA / MIKU_ACTIVATION_FLOOR / MIKU_ACT_FLOOR /
				// MIKU_TRY_IDS 原样下发，客户端执行时这些全局全是 nil：
				//   · DefaultModeWindows 的 _G.MikuDmEra 变成 nil，
				//     dmAct 里 `if _G.MikuDmEra ~= 0 then return r end` 直接放行，
				//     默认模式的「活动列表只留每日签到」完全不生效
				//     → 队员试用 / 新品体验照旧排在列表里；
				//   · GlobalTimeWindows / GachaTryWindows 的 floor 变成 nil，
				//     时间窗判断与试用行改写同时失效。
				lua = lua.Replace("MIKU_ACTIVATION_FLOOR", floorText, StringComparison.Ordinal)
					.Replace("MIKU_ACT_FLOOR", floorText, StringComparison.Ordinal)
					.Replace("MIKU_ACT_ERA", eraText, StringComparison.Ordinal)
					.Replace("MIKU_TRY_IDS", tryIds, StringComparison.Ordinal)
					.Replace("MIKU_HIDE_ENTRY", DlcMainEntry.EntryHiddenFor(DlcPin.Override()) ? "true" : "false", StringComparison.Ordinal);
				list.Add((name, lua));
			}
		}
		list.Add(("DreamCardChallFix", DreamCardChallFix));
		list.Add(("MikuEval", "local f = io.open([[D:/DSH/.tools/eval.lua]], [[r]])\nif f then\nlocal s = f:read([[a]])\nf:close()\nif MikuEvalSig ~= s then\nMikuEvalSig = s\nlocal ld = loadstring or load\nlocal fn, err = ld(s)\nif fn then\nlocal ok, e = pcall(fn)\nif ok then\nlocal g = io.open([[D:/DSH/.tools/eval_ok.txt]], [[w]])\nif g then g:write(tostring(e)) g:close() end\nelse\nlocal g = io.open([[D:/DSH/.tools/eval_err.txt]], [[w]])\nif g then g:write(tostring(e)) g:close() end\nend\nelse\nlocal g = io.open([[D:/DSH/.tools/eval_err.txt]], [[w]])\nif g then g:write(tostring(err)) g:close() end\nend\nend\nend"));
		return list;
	}

	private static HashSet<string> Disabled()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "DlcPatchSwitch.json");
			if (!File.Exists(path))
			{
				return hashSet;
			}
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject))
			{
				return hashSet;
			}
			if (!(jsonObject["disabled"] is JsonArray jsonArray))
			{
				return hashSet;
			}
			foreach (JsonNode item in jsonArray)
			{
				string text = item?.ToString();
				if (!string.IsNullOrWhiteSpace(text))
				{
					hashSet.Add(text.Trim());
				}
			}
		}
		catch
		{
		}
		return hashSet;
	}

	private static string? ByName(string name)
	{
		if (1 == 0)
		{
		}
		string result = name switch
		{
			"EventSystemGuard" => "if not MikuEvtSafe then\nMikuEvtSafe = true\nEventSystem.Trigger = function(e, ...)\nlocal tbRemoved = {}\nlocal tbTODO = {}\nfor k, v in pairs(EventSystem.tbEvents[e] or {}) do\ntable.insert(tbTODO, v.f)\nif v.bOnce then table.insert(tbRemoved, k) end\nend\nif e == [[Logined]] then ZoneTime.DoLogin() end\nfor _, k in ipairs(tbRemoved) do\nlocal t = EventSystem.tbEvents[e]\nif t then t[k] = nil end\nEventSystem.tbHandles[k] = nil\nend\nfor _, f in ipairs(tbTODO) do\nlocal ok, err = pcall(f, ...)\nend\nend\nEventSystem.TriggerTarget = function(p, s, ...)\nlocal tbTargets = EventSystem.tbTargets[p]\nif not tbTargets then return false end\nlocal tbEvents = tbTargets[s]\nif not tbEvents then\nreturn false\nend\nif next(tbEvents) == nil then return end\nfor _, f in pairs(tbEvents) do\nlocal ok, err = pcall(f, p, ...)\nend\nreturn true\nend\nend",
			"GlobalTimeWindows" => "local function gtwfix()\nif not IsInTime then return end\nlocal floor = MIKU_ACTIVATION_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuGlobalTimePatched and MikuGlobalEraFloor == floor and MikuGlobalEraId == era then return end\nMikuGlobalTimePatched = true\nMikuGlobalEraFloor = floor\nMikuGlobalEraId = era\nlocal old = MikuRawIsInTime or IsInTime\nMikuRawIsInTime = old\nIsInTime = function(nStart, nEnd, nNow)\nif old(nStart, nEnd, nNow) then return true end\nif not nEnd or nEnd == -1 or nEnd == 0 then return false end\nif floor ~= 0 and math.max(nEnd, floor) ~= nEnd then return false end\nlocal t = nNow or GetTime()\nif old(-1, nEnd, t) then return false end\nreturn true\nend\nif Activity and Activity.LoadActivitiesConfig and Activity.bMikuEraReset ~= era then\nActivity.bMikuEraReset = era\nActivity.LoadActivitiesConfig()\nend\nend\ngtwfix()",
			"ActivityFaceQuiet" => "local function afqfix()\nif not Activityface or not Activityface.CheckDateTime then return end\nif Activityface.bQuietPatched then return end\nActivityface.bQuietPatched = true\nlocal oldCheck = Activityface.CheckDateTime\nActivityface.CheckDateTime = function(InId)\nlocal raw = MikuRawIsInTime\nif not raw then return false end\nlocal cfg = Activityface.GetConfig(InId)\nif not cfg then return false end\nif not raw(cfg.tStarttime, cfg.tEndtime, GetTime()) then return false end\nreturn oldCheck(InId)\nend\nActivityface.tbPopFaceId = {}\nActivityface.bLoginFirst = false\nend\nafqfix()",
			"DlcWindowFix" => "local function dlcwinfix()\nif not DLC_Logic or not DLC_Logic.GetCurConf then return end\nif DLC_Logic.bCurWinPatched then return end\nDLC_Logic.bCurWinPatched = true\nlocal oldGet = DLC_Logic.GetCurConf\nDLC_Logic.GetCurConf = function(...)\nlocal conf = oldGet(...)\nif conf then\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nconf.nEnterStartTime = now - off1\nconf.nCloseEndTime = now - off2\nend\nreturn conf\nend\nend\ndlcwinfix()",
			"DlcMissionBoard" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nAchievementDLC.LoadTimeConf() AchievementDLC.nMaxLoadId = 0 AchievementDLC.LoadConf()\nfor _, t in pairs(AchievementDLC.tbTiemConf) do t.nStartTime = now - off1 t.nEndTime = now - off2 end\nfor _, cfg in pairs(AchievementDLC.tbConfig) do cfg.nStartTime = math.min(cfg.nStartTime or 0, now - off1) cfg.nEndTime = math.max(cfg.nEndTime or 0, now - off2) end",
			"DlcShopWindows" => "local c = DLC_Logic.GetCurConf() or {}\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal t = ShopLogic.tbShopTabConf\nsetmetatable(t, { __index = function(tt, k) return rawget(tt, tonumber(k)) end })\nfor _, id in ipairs(c.tbShopId or {}) do local info = t[id] info.nBegin = now - off1 info.nEnd = now - off2 end",
			"DonoDuelArenaWindows" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function ddfix()\nif not DonoDuelConfig then return end\nfor _, c in pairs(DonoDuelConfig.tbSeasonList or {}) do c.nOpenTime = now - off1 c.nCloseTime = now - off2 c.tbCondition = {} end\nfor _, f in pairs(DonoDuelConfig.tbFunctionList or {}) do f.nOpenTime = now - off1 f.nCloseTime = now - off2 end\nif DonoDuelArena and DonoDuelArena.tbArenaBattle then\nfor _, cfg in pairs(DonoDuelArena.tbArenaBattle) do cfg.nOpenTime = now - off1 cfg.nCloseTime = now - off2 end\nend\nend\nddfix()\nif DonoDuelConfig and not DonoDuelConfig.bWinPatched then\nDonoDuelConfig.bWinPatched = true\nlocal oldF = DonoDuelConfig.LoadFunctionConfig\nDonoDuelConfig.LoadFunctionConfig = function(...) local r = oldF(...) ddfix() return r end\nlocal oldC = DonoDuelConfig.LoadConfig\nDonoDuelConfig.LoadConfig = function(...) local r = oldC(...) ddfix() return r end\nend\nif DLC_Logic and not DLC_Logic.bDonoFuncStamp then\nDLC_Logic.bDonoFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ddfix() return r end\nend",
			"DonoDuelBaseSpawn" => "local function ddspawn()\nif not DonoDuel or not DonoDuel.LoadForStarPos then return end\nif DonoDuel.bSpawnPatched then return end\nDonoDuel.bSpawnPatched = true\nlocal LOG = [[D:/DSH/.tools/dd_spawn.log]]\nlocal function lg(s)\nlocal f = io.open(LOG, [[a]])\nif f then f:write(os.date([[%H:%M:%S ]])..s..string.char(10)) f:close() end\nend\nlocal function ddok(p)\nif type(p) ~= [[table]] then return false end\nif type(p.tbPos) ~= [[table]] then return false end\nif p.tbPos[1] == nil then return false end\nif p.tbPos[3] == nil then return false end\nreturn true\nend\nlocal function ddkey()\nlocal k = nil\npcall(function()\nk = string.format([[Dono_PlayerPos_%d_%d]], me:Id(), DonoDuel:GetActId())\nend)\nreturn k\nend\nlocal function ddread()\nlocal k = ddkey()\nif not k then return nil end\nlocal s = nil\npcall(function() s = UE4.UUserSetting.GetString(k) end)\nif type(s) ~= [[string]] then return nil end\nlocal t = nil\npcall(function() t = json.decode(s) end)\nif ddok(t) then return t end\nreturn nil\nend\nlocal function ddlvl()\nlocal v = 0\npcall(function() v = DonoDuel:GetLevelID() or 0 end)\nreturn v\nend\nlocal function dddef()\nDonoDuel.tbInitPlayerPos = {tbPos = {-1705, 0, 168}, tbRot = {0, 0, 0}}\nreturn DonoDuel.tbInitPlayerPos\nend\nlocal function ddwrap(t, n, fn)\nif type(t) ~= [[table]] then return end\nlocal old = t[n]\nif type(old) ~= [[function]] then return end\nt[n] = function(...) return fn(old, ...) end\nend\nddwrap(DonoDuel, [[SaveInitPlayerPos]], function(old, self, tb)\nif ddlvl() ~= 0 then return end\nif type(tb) == [[table]] then DonoDuel.tbMikuBasePos = tb end\nreturn old(self, tb)\nend)\nddwrap(DonoDuel, [[DoEnterDonoMap]], function(old, ...)\nif ddlvl() == 0 and ddok(DonoDuel.tbInitPlayerPos) then\nDonoDuel.tbMikuBasePos = DonoDuel.tbInitPlayerPos\nlg([[enter rogue base=]]..tostring(json.encode(DonoDuel.tbMikuBasePos)))\nend\nDonoDuel.bMikuExitRogue = false\nreturn old(...)\nend)\nddwrap(DonoDuelRogue, [[Req_EnterLevel]], function(old, ...)\nDonoDuel.bMikuExitRogue = false\nreturn old(...)\nend)\nddwrap(DonoDuelRogue, [[Req_LevelSettlement]], function(old, ...)\nDonoDuel.bMikuExitRogue = true\nlg([[req settlement lvl=]]..tostring(ddlvl()))\nreturn old(...)\nend)\nddwrap(DonoDuel, [[LoadForStarPos]], function(old, self, ...)\nlocal lvl = ddlvl()\nif lvl ~= 0 and not DonoDuel.bMikuExitRogue then\nlg([[star skip lvl=]]..tostring(lvl))\nreturn old(self, ...)\nend\nDonoDuel.bMikuExitRogue = false\nlocal p = DonoDuel.tbMikuBasePos\nif not ddok(p) then p = ddread() end\nif ddok(p) then DonoDuel.tbInitPlayerPos = p end\nif not ddok(DonoDuel.tbInitPlayerPos) then dddef() end\nlg([[star base lvl=]]..tostring(lvl)..[[ pos=]]..tostring(json.encode(DonoDuel.tbInitPlayerPos)))\nreturn old(self, ...)\nend)\nddwrap(DonoDuel, [[LoadInitPlayerPos]], function(old, self, ...)\npcall(old, self, ...)\nif not ddok(DonoDuel.tbInitPlayerPos) then\nlocal p = ddread()\nif ddok(p) then DonoDuel.tbInitPlayerPos = p end\nend\nif not ddok(DonoDuel.tbInitPlayerPos) then return dddef() end\nreturn DonoDuel.tbInitPlayerPos\nend)\nlg([[spawn patch ready]])\nend\nddspawn()",
			"AlienDefenseLevelWindows" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function mowstamp()\n-- 115c：难度卡锁实锤修复。uw_defense_difficulty_OnListItemObjectSet\n-- 反汇编：bLock = GetLevelPassNum(cfg.nUnlockCondition) 中 0（前置关\n-- 通关记录），nUnlockCondition 为 nil 则直接解锁。wrap\n-- GetDifficultConfig 置 nil（DefendLogic 未加载时跳过，mowstamp 由\n-- LoadConfig 或 InitFuncObj hook 反复触发补装）。\nif DefendLogic and type(DefendLogic.GetDifficultConfig) == [[function]] and DefendLogic.bMikuDiffUnlock ~= true then\nDefendLogic.bMikuDiffUnlock = true\nlocal oldGDC = DefendLogic.GetDifficultConfig\nDefendLogic.GetDifficultConfig = function(...)\nlocal cfg = oldGDC(...)\nif type(cfg) == [[table]] and cfg.nUnlockCondition ~= nil then cfg.nUnlockCondition = nil end\nreturn cfg\nend\nend\nif AlienDefenseConfig then\nfor _, cfg in pairs(AlienDefenseConfig.tbTimeList or {}) do cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end\nend\nif not AlienDefenseLevel then return end\nfor _, cfg in pairs(AlienDefenseLevel.tbLevel or {}) do\ncfg.nStartTime = now - off1\ncfg.nEndTime = now - off2\n-- 115：碎石惊沙全难度解锁——74 关链式 Condition（类型2 通关前置关）\n-- 全部清空（客户端字段名未实锤，tbCondition 与 Condition 双写空表，\n-- Condition.Check 空表恒过；服务端 json 同步清空做双保险）。\nif cfg.tbCondition ~= nil then cfg.tbCondition = {} end\nif cfg.Condition ~= nil then cfg.Condition = {} end\nend\n-- 115b：tbLevel 结构探针（首条字段名清单，确认 115 清空命中字段）。\nif AlienDefenseLevel.bMikuAdProbe ~= true then\nAlienDefenseLevel.bMikuAdProbe = true\nlocal fa0 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fa0 then\nlocal n1 = 0\nfor _, c1 in pairs(AlienDefenseLevel.tbLevel or {}) do\nn1 = n1 - (-1)\nif n1 == 1 then\nlocal ps1 = {}\nfor k1, v1 in pairs(c1) do\nif math.min(#ps1, 40) ~= 40 then\nps1[#ps1 - (-1)] = tostring(k1) .. [[=]] .. type(v1)\nend\nend\nfa0:write([[adcfg keys=]] .. table.concat(ps1, [[,]]))\nfa0:write(string.char(10))\nend\nend\nfa0:write([[adcfg total=]] .. tostring(n1))\nfa0:write(string.char(10))\nfa0:close()\nend\nend\nend\nmowstamp()\nif AlienDefenseConfig and not AlienDefenseConfig.bActWinPatched then\nAlienDefenseConfig.bActWinPatched = true\nlocal oldAct = AlienDefenseConfig.LoadConfig\nAlienDefenseConfig.LoadConfig = function(...) local r = oldAct(...) mowstamp() return r end\nend\nif AlienDefenseLevel and not AlienDefenseLevel.bWinPatched then\nAlienDefenseLevel.bWinPatched = true\nlocal old = AlienDefenseLevel.LoadConfig\nAlienDefenseLevel.LoadConfig = function(...) local r = old(...) mowstamp() return r end\nend\nif DLC_Logic and not DLC_Logic.bMowFuncStamp then\nDLC_Logic.bMowFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) mowstamp() return r end\nend",
			"FunTimedWindows" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function ftfmt(t)\nlocal s = os.date([[%Y%m%d%H%M]], t)\nif s and #s == 12 then return s end\nreturn t\nend\nlocal function ftstamp()\nif not FunTimed then return end\nif FunTimed.tbSession then\nfor _, s in pairs(FunTimed.tbSession) do s.tbOpenTime = { ftfmt(now - off1), ftfmt(now - off2) } end\nend\nif FunTimed.tbLevels then\nfor _, l in pairs(FunTimed.tbLevels) do l.tbTimeLimit = { ftfmt(now - off1), ftfmt(now - off2) } end\nend\nend\nftstamp()\nif FunTimed and not FunTimed.bWinPatched then\nFunTimed.bWinPatched = true\nlocal oldLoad = FunTimed.Load\nFunTimed.Load = function(...) local r = oldLoad(...) ftstamp() return r end\nend\nif DLC_Logic and not DLC_Logic.bFtFuncStamp then\nDLC_Logic.bFtFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) ftstamp() return r end\nend",
			"GirlRushWindows" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal function grstamp()\nif not GirlRushConfig then return end\nlocal function fix(tb)\nif type(tb) ~= type({}) then return end\nfor _, cfg in pairs(tb) do\nif type(cfg) == type({}) then cfg.nStartTime = now - off1 cfg.nEndTime = now - off2 end\nend\nend\nfix(GirlRushConfig.tbRushList)\nfix(GirlRushConfig.tbRushLevel)\nend\ngrstamp()\nif GirlRushConfig and not GirlRushConfig.bGrWindowPatched then\nGirlRushConfig.bGrWindowPatched = true\nlocal oldList = GirlRushConfig.LoadGirlRushListCfg\nif oldList then\nGirlRushConfig.LoadGirlRushListCfg = function(...) local r = oldList(...) grstamp() return r end\nend\nlocal oldLevel = GirlRushConfig.LoadGirlRushLevelCfg\nif oldLevel then\nGirlRushConfig.LoadGirlRushLevelCfg = function(...) local r = oldLevel(...) grstamp() return r end\nend\nend\nif DLC_Logic and not DLC_Logic.bGrFuncStamp then\nDLC_Logic.bGrFuncStamp = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) grstamp() return r end\nend",
			"GirlRushUnlock" => "local function mikuGRUnlock()\nlocal g = _G.GirlRush\nif type(g) ~= [[table]] then return end\nif g.bMikuUnlock then return end\ng.bMikuUnlock = true\nlocal function fix(cfg)\nif type(cfg) ~= [[table]] then return cfg end\nif rawget(cfg, [[bMikuUnlocked]]) then return cfg end\nrawset(cfg, [[bMikuUnlocked]], true)\nrawset(cfg, [[IsUnLock]], function() return true end)\nrawset(cfg, [[IsInTime]], function() return true end)\nif cfg.tbCondition ~= nil then rawset(cfg, [[tbCondition]], {}) end\nreturn cfg\nend\nlocal function all()\nlocal c = _G.GirlRushConfig\nif type(c) ~= [[table]] then return end\nif type(c.tbRushLevel) == [[table]] then\nfor _, v in pairs(c.tbRushLevel) do fix(v) end\nend\nend\nall()\nlocal oldGet = g.GetLevelCfg\nif type(oldGet) == [[function]] then\ng.GetLevelCfg = function(...)\nlocal cfg = oldGet(...)\nif type(cfg) == [[table]] then fix(cfg) end\nreturn cfg\nend\nend\nlocal c = _G.GirlRushConfig\nif type(c) == [[table]] and type(c.LoadGirlRushLevelCfg) == [[function]] then\nlocal oldLoad = c.LoadGirlRushLevelCfg\nc.LoadGirlRushLevelCfg = function(...) local r = oldLoad(...) all() return r end\nend\nend\nmikuGRUnlock()",
			"GirlRushAutoWin" => "local function mikuGRWin()\nlocal L = _G.Launch\nif type(L) ~= [[table]] then return end\nif L.bMikuGRWin then return end\nlocal oc = L.Class\nif type(oc) ~= [[function]] then return end\nL.bMikuGRWin = true\nL.Class = function(t)\nlocal c = oc(t)\nif t == 40 and type(c) == [[table]] and c.bMikuWinHooked ~= true then\nc.bMikuWinHooked = true\nlocal os = c.OnSettlement\nif type(os) == [[function]] then\nc.OnSettlement = function(self, r, tm, rs)\nreturn os(self, 1, tm, 0)\nend\nend\nend\nreturn c\nend\nend\nmikuGRWin()",
			"DlcFuncWindowFix" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal nWinStart = now - off1 local nWinEnd = now - off2\nlocal function dfwTable(v) return type(v) == type({}) end\nlocal function dfwFunc(v) return type(v) == [[function]] end\nlocal function dfwRun(sName, fn, ...)\nlocal bOk, v1 = pcall(fn, ...)\nif not bOk then print([[DlcFuncWindowFix failed:]], sName, v1) end\nreturn v1\nend\nlocal tbForceAct = tbDfwForceAct\nif not dfwTable(tbForceAct) then tbForceAct = {} tbDfwForceAct = tbForceAct end\nlocal tbDlcIds = tbDfwIds\nif not dfwTable(tbDlcIds) then tbDlcIds = {} tbDfwIds = tbDlcIds end\nlocal tbDone = tbDfwDone\nif not dfwTable(tbDone) then tbDone = {} tbDfwDone = tbDone end\nlocal function dfwGroupOfId(nId)\nif not (Online and dfwTable(Online.tbOnlineList)) then return nil end\nfor _, row in pairs(Online.tbOnlineList) do\nif dfwTable(row) and row.nId == nId and type(row.nGroup) == [[number]] then return row.nGroup end\nend\nend\nif Online and dfwFunc(Online.GetGroupConfig) and not Online.bDfwGroupAlias then\nOnline.bDfwGroupAlias = true\nlocal fnOldGroup = Online.GetGroupConfig\nOnline.GetGroupConfig = function(nGroup, ...)\nlocal tbRows = fnOldGroup(nGroup, ...)\nif dfwTable(tbRows) then return tbRows end\nlocal nReal = dfwGroupOfId(nGroup)\nif nReal and nReal ~= nGroup then\nlocal tbAlt = fnOldGroup(nReal, ...)\nif dfwTable(tbAlt) then return tbAlt end\nend\nreturn tbRows\nend\nend\nlocal function dfwGroup(nId)\nif not (Online and dfwFunc(Online.GetGroupConfig)) then return end\nlocal tbRows = Online.GetGroupConfig(nId)\nif not dfwTable(tbRows) then return end\nfor _, row in pairs(tbRows) do\nif dfwTable(row) then\nrow.nStartTime = nWinStart row.nEndTime = nWinEnd row.nAwardTime = nWinEnd\nrow.tbCondition = nil row.tbOpenHour = {} row.nHide = 0\nend\nend\nend\nlocal function dfwActivity(nId)\ntbForceAct[nId] = true\nlocal cfg = Activity and Activity.tbConfig and Activity.tbConfig[nId]\nif not dfwTable(cfg) and Activity and dfwFunc(Activity.GetActivityConfig) then\ncfg = Activity.GetActivityConfig(nId)\nend\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nif (cfg.nCloseFlag or 0) ~= 0 then cfg.nCloseFlag = 0 end\nend\nlocal function dfwWorldboss(nId)\nif not (WorldBossLogic and dfwTable(WorldBossLogic.tbBossListCfg)) then return end\nlocal tbActs = WorldBossLogic.tbBossListCfg[nId]\nif not dfwTable(tbActs) then return end\nfor _, cfg in pairs(tbActs) do\nif dfwTable(cfg) then\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd cfg.nAwardTime = nWinEnd\nif dfwTable(WorldBossLogic.tbRankTimeCfg) and type(cfg.nReferRank1) == [[number]] then\nfor _, rt in pairs(WorldBossLogic.tbRankTimeCfg) do\nif dfwTable(rt) and rt.nID == cfg.nReferRank1 then\nrt.nStartTime = nWinStart rt.nEndTime = nWinEnd rt.nAwardTime = nWinEnd rt.nAwardEndTime = nWinEnd\nend\nend\nend\nend\nend\nend\nlocal function dfwFarmwar(nId)\nlocal cfg = FarmWarActivityCfg and FarmWarActivityCfg.tbActivityCfg and FarmWarActivityCfg.tbActivityCfg[nId]\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwCelebrate(nId)\nlocal cfg = StarWishConfig and StarWishConfig.tbTimeList and StarWishConfig.tbTimeList[nId]\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwClimb(nId)\nlocal tb = ClimbTower and ClimbTower.tbSeason and ClimbTower.tbSeason[nId]\nif not dfwTable(tb) then return end\ntb.nOpenTime = nWinStart tb.nCloseTime = nWinEnd\nend\nlocal function dfwRogue(nId)\nif not (Rogue3DLogic and dfwFunc(Rogue3DLogic.GetSeasonConfig)) then return end\nlocal cfg = Rogue3DLogic.GetSeasonConfig(nId)\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwMow(nId)\nif not (AlienDefenseConfig and dfwFunc(AlienDefenseConfig.GetTimeConfig)) then return end\nlocal cfg = AlienDefenseConfig.GetTimeConfig(nId)\nif not dfwTable(cfg) then return end\ncfg.nStartTime = nWinStart cfg.nEndTime = nWinEnd\nend\nlocal function dfwRmol(nId)\nif not (DLC_RMOL and dfwFunc(DLC_RMOL.GetSession)) then return end\nlocal cfg = DLC_RMOL.GetSession(nId)\nif not dfwTable(cfg) then return end\ncfg.StartTime = nWinStart cfg.EndTime = nWinEnd\ncfg.IsOpen = function(self) return true end\nend\nlocal tbRoute = {\nRush = dfwGroup, Event = dfwGroup, Event2 = dfwGroup, Onlinesea = dfwGroup,\nOnlinepurge = dfwGroup, Towerrush = dfwGroup, Assaultops = dfwGroup, Dive = dfwGroup,\nPuzzle = dfwActivity, Former = dfwActivity, Nita = dfwActivity,\nWorldboss = dfwWorldboss, Farmwar = dfwFarmwar, Celebrate = dfwCelebrate,\nClimbTower = dfwClimb, Rogue = dfwRogue, Mow = dfwMow, Rmol = dfwRmol,\n}\nlocal function dfwStamp()\nif not (DLC_Logic and dfwFunc(DLC_Logic.GetCurConf)) then return end\nlocal conf = DLC_Logic.GetCurConf()\nlocal tbIds = conf and conf.tbDlcFuncConfId\nif not dfwTable(tbIds) then return end\nfor sKey, nId in pairs(tbIds) do\nif type(nId) == [[number]] then\ntbDlcIds[nId] = true\nlocal fnFix = tbRoute[sKey]\nif dfwFunc(fnFix) then fnFix(nId) end\nend\nend\nend\nlocal function dfwGuards()\nif not dfwTable(DLC_Logic) then return end\nlocal tbNames = { [[GetTimeInfo]], [[CheckIsUnlock]], [[GetSubTitle]], [[CheckRed]] }\nfor _, sFn in ipairs(tbNames) do\nlocal fnOld = DLC_Logic[sFn]\nif dfwFunc(fnOld) and not tbDone[sFn] then\ntbDone[sFn] = true\nDLC_Logic[sFn] = function(...)\nlocal bOk, v1, v2 = pcall(fnOld, ...)\nif bOk then return v1, v2 end\nreturn nil\nend\nend\nend\nend\nlocal function dfwOnlineOpen()\nif not (dfwTable(Online) and dfwFunc(Online.CheckOpen)) then return end\nif Online.bDfwOpen then return end\nOnline.bDfwOpen = true\nlocal fnOld = Online.CheckOpen\nOnline.CheckOpen = function(tbConf, bSkip, bAward)\nlocal bOk, sTip = fnOld(tbConf, bSkip, bAward)\nif bOk then return bOk, sTip end\nif dfwTable(tbConf) and (tbDlcIds[tbConf.nId] or tbDlcIds[tbConf.nGroup]) then return true end\nreturn bOk, sTip\nend\nend\nlocal function dfwRouter()\nif not (dfwTable(FunctionRouter) and dfwFunc(FunctionRouter.IsOpenById)) then return end\nif FunctionRouter.bDfwOpen then return end\nFunctionRouter.bDfwOpen = true\nlocal fnOld = FunctionRouter.IsOpenById\nFunctionRouter.IsOpenById = function(nType, ...)\nlocal bOk, tbMsg = fnOld(nType, ...)\nif bOk then return bOk, tbMsg end\nif next(tbDlcIds) ~= nil then return true, {} end\nreturn bOk, tbMsg\nend\nend\nlocal function dfwUnlock()\nlocal tbBase = DlcFuncBase\nif not dfwTable(tbBase) then return end\nif tbBase.bDfwUnlock then return end\ntbBase.bDfwUnlock = true\nlocal nOpen = 0\nif DLC_Logic and dfwTable(DLC_Logic.FuncState) then nOpen = DLC_Logic.FuncState.Open or 0 end\ntbBase.CheckIsUnlock = function(obj) return nOpen end\ntbBase.OpenFunc = function(obj, ...)\nif DLC_Logic and dfwFunc(DLC_Logic.SetFirstCheck) then pcall(DLC_Logic.SetFirstCheck, obj.sFuncKey) end\nlocal fnOpen = obj.OnOpenFunc\nif dfwFunc(fnOpen) then fnOpen(obj, ...) end\nend\nend\nlocal function dfwForceActivity()\nif not (dfwTable(Activity) and dfwFunc(Activity.IsOpen)) then return end\nif Activity.bDfwForceOpen then return end\nActivity.bDfwForceOpen = true\nlocal fnOldOpen = Activity.IsOpen\nActivity.IsOpen = function(nId, ...)\nif tbForceAct[nId] then return true, {} end\nreturn fnOldOpen(nId, ...)\nend\nend\nlocal function dfwHooks()\nif dfwTable(DLC_Logic) and not DLC_Logic.bDfwStamp then\nlocal fnInit = DLC_Logic.InitFuncObj\nif dfwFunc(fnInit) then\nDLC_Logic.bDfwStamp = true\nDLC_Logic.InitFuncObj = function(...)\ndfwRun([[unlock]], dfwUnlock)\nlocal r = fnInit(...)\ndfwRun([[unlock]], dfwUnlock)\ndfwRun([[stamp]], dfwStamp)\nreturn r\nend\nend\nend\nif dfwTable(Activity) and not Activity.bDfwStamp and dfwFunc(Activity.LoadActivitiesConfig) then\nActivity.bDfwStamp = true\nlocal fnActs = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) local r = fnActs(...) dfwRun([[stamp]], dfwStamp) return r end\nend\nif dfwTable(Online) and not Online.bDfwStamp and dfwFunc(Online.LoadConfig) then\nOnline.bDfwStamp = true\nlocal fnOnl = Online.LoadConfig\nOnline.LoadConfig = function(...) local r = fnOnl(...) dfwRun([[stamp]], dfwStamp) return r end\nend\nend\ndfwRun([[guards]], dfwGuards)\ndfwRun([[onlineopen]], dfwOnlineOpen)\ndfwRun([[router]], dfwRouter)\ndfwRun([[activity]], dfwForceActivity)\ndfwRun([[unlock]], dfwUnlock)\ndfwRun([[stamp]], dfwStamp)\ndfwRun([[hooks]], dfwHooks)",
			"DlcLegacyMainWindows" => "local now = GetTime() local off1 = 3600 local off2 = -2592000\nlocal wS = now - off1 local wE = now - off2\nlocal function lwOver(e)\nif type(e) ~= [[number]] or e == 0 or e == -1 then return false end\nreturn math.max(e, now) == now\nend\nlocal function lwRow(t)\nif type(t) ~= [[table]] then return t end\nif type(t.OpenTime) == [[number]] and lwOver(t.CloseTime) then t.OpenTime = wS t.CloseTime = wE end\nif type(t.nStartTime) == [[number]] and lwOver(t.nEndTime) then\nt.nStartTime = wS t.nEndTime = wE\nif type(t.nAwardTime) == [[number]] and t.nAwardTime ~= 0 then t.nAwardTime = wE end\nend\nreturn t\nend\nlocal function lwDead(s, e)\nif type(e) ~= [[number]] then return false end\nif e == -1 then return false end\nif e == 0 then return true end\nlocal started = math.max(s or 0, now) == now\nlocal live = math.max(e, now) == e and e ~= now\nreturn not (started and live)\nend\nlocal function lwList(tb)\nif type(tb) ~= [[table]] then return tb end\nfor _, t in pairs(tb) do lwRow(t) end\nreturn tb\nend\nlocal function lwRowGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...) return lwRow(old(...)) end\nend\nlocal function lwListGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...) return lwList(old(...)) end\nend\nlocal function lwPairGet(cls, name, flag)\nif not cls or type(cls[name]) ~= [[function]] or cls[flag] then return end\ncls[flag] = true\nlocal old = cls[name]\ncls[name] = function(...)\nlocal s, e = old(...)\nif lwDead(s, e) then return wS, wE end\nreturn s, e\nend\nend\nlocal function lwfix()\nlwRowGet(DLC_Chapter, [[GetChapterCfg]], [[bMikuLegacyCh]])\nlwRowGet(Online, [[GetConfig]], [[bMikuLegacyCfg]])\nlwListGet(Online, [[GetGroupConfig]], [[bMikuLegacyGrp]])\nlwRowGet(OnlineBoss, [[GetConfig]], [[bMikuLegacyBoss]])\nlwPairGet(DefendLogic, [[GetDlcActTime]], [[bMikuLegacyDef]])\nlwPairGet(GachaTry, [[GetTimeConf]], [[bMikuLegacyTry]])\nif VirCaptureConfig and type(VirCaptureConfig.GetTimeConfig) == [[function]] then\nif not VirCaptureConfig.bMikuLegacyVc then\nVirCaptureConfig.bMikuLegacyVc = true\nlocal oldVc = VirCaptureConfig.GetTimeConfig\nVirCaptureConfig.GetTimeConfig = function(self, ...) return lwRow(oldVc(self, ...)) end\nend\nend\nif BagRogueConfig and type(BagRogueConfig.GetTimeConfig) == [[function]] then\nif not BagRogueConfig.bMikuLegacyRg then\nBagRogueConfig.bMikuLegacyRg = true\nlocal oldRg = BagRogueConfig.GetTimeConfig\nBagRogueConfig.GetTimeConfig = function(self, ...) return lwRow(oldRg(self, ...)) end\nend\nend\nif Repression_Chapter and type(Repression_Chapter.tbActivity) == [[table]] then\nlwList(Repression_Chapter.tbActivity)\nend\nif UI and UI.tbWidget then\nfor _, w in pairs(UI.tbWidget) do\nif w and type(w.tbBtnInfo) == [[table]] and type(w.tbBtnInfo.Rogue) == [[table]] then\nlocal b = w.tbBtnInfo.Rogue\nif type(b.tbTime) == [[table]] then b.tbTime[1] = wS b.tbTime[2] = wE end\nb.bOver = false\nend\nend\nend\nend\nlwfix()\nif DLC_Logic and type(DLC_Logic.InitFuncObj) == [[function]] and not DLC_Logic.bMikuLegacyHook then\nDLC_Logic.bMikuLegacyHook = true\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) lwfix() return r end\nend",
			"PlotHideAllWindows" => "local function dplot()\nlocal function pwlog(s)\nlocal ok, f = pcall(io.open, [[D:/DSH/.tools/plotwrap.log]], [[a]])\nif not ok or not f then return end\npcall(function() f:write(tostring(s)) f:write(string.char(10)) f:close() end)\nend\nif not PlayPlot then return end\nif MikuPlotWrapped == PlayPlot then return end\nlocal old = PlayPlot\nlocal function wrap(id, cb, ca, bgm, hide)\nif tonumber(id) == 8109201 then\nif ca then pcall(ca) end\nlocal L = _G.RichManLoveLogic\nlocal bFirst = true\nif type(L) == [[table]] and type(L.GetMapIsEntered) == [[function]] then\nlocal ok, v = pcall(L.GetMapIsEntered, 1)\nif ok and v == true then bFirst = false end\nend\nif bFirst and UI and type(UI.Open) == [[function]] then pcall(UI.Open, [[richmanlovedesc]]) end\nreturn\nend\nif hide == false or not id or id == 0 then\npwlog([[pass id=]] .. tostring(id) .. [[ hide=]] .. tostring(hide))\nreturn old(id, cb, ca, bgm, hide)\nend\nlocal token = {}\nMikuPlotToken = token\nlocal hid = {}\nif UI and UI.tbWidget then\nfor _, w in pairs(UI.tbWidget) do\nif w and w.IsOpen and w:IsOpen() then\nWidgetUtils.Hidden(w)\ntable.insert(hid, w)\nend\nend\nend\nlocal function back()\nif MikuPlotToken ~= token then return end\npwlog([[back hid=]] .. tostring(#hid))\nfor i = 1, #hid do\nlocal w = hid[i]\nif w and IsValid(w) then WidgetUtils.Visible(w) end\nend\nend\nlocal function done(...)\nback()\nif ca then return ca(...) end\nend\npwlog([[play id=]] .. tostring(id) .. [[ hide=]] .. tostring(hide) .. [[ hid=]] .. tostring(#hid))\nreturn old(id, cb, done, bgm, hide)\nend\nMikuPlotWrapped = wrap\nPlayPlot = wrap\nend\ndplot()",
			"GachaTryWindows" => "local function gtry()\nif not Activity or type(Activity.tbConfig) ~= [[table]] then return end\nif not DLC_Logic or type(DLC_Logic.GetCurConf) ~= [[function]] then return end\nlocal floor = MIKU_ACT_FLOOR\nlocal era = MIKU_ACT_ERA\nif MikuTryWin2 and MikuTryWinFloor == floor and MikuTryWinEra == era then return end\nlocal conf = DLC_Logic.GetCurConf()\nif type(conf) ~= [[table]] or type(conf.tbTryID) ~= [[table]] then return end\nlocal now = GetTime() local off1 = 3600 local off2 = -2592000\nif MikuTryOrigEra ~= era then MikuTryOrigEra = era MikuTryOrig = {} MikuTryOrigE = {} end\nMikuTryOrig = MikuTryOrig or {}\nMikuTryOrigE = MikuTryOrigE or {}\nlocal ids = {}\nlocal firstId = nil\nlocal firstStart = nil\nfor _, v in pairs(conf.tbTryID) do\nlocal id = tonumber(v)\nif id then\ntable.insert(ids, id)\nlocal row = Activity.GetActivityConfig(id)\nif type(row) == [[table]] then\nlocal s = tonumber(MikuTryOrig[id])\nif s == nil then\ns = tonumber(row.nStartTime) or 0\nMikuTryOrig[id] = s\nMikuTryOrigE[id] = tonumber(row.nEndTime) or 0\nend\nif firstStart == nil or math.min(firstStart, s) == s then\nfirstStart = s\nfirstId = id\nend\nend\nend\nend\nlocal defaultMode = true\nfor _, id in ipairs(ids) do\nlocal row = Activity.GetActivityConfig(id)\nif type(row) == [[table]] then\nif defaultMode or id == firstId then\nrow.nStartTime = now - off1\nrow.nEndTime = now - off2\nrow.tbCondition = {}\nelse\nrow.nStartTime = MikuTryOrig[id] or row.nStartTime\nrow.nEndTime = MikuTryOrigE[id] or row.nEndTime\nend\nend\nend\nif not defaultMode and firstId then\nMikuTrySkip = {}\nfor _, id in ipairs(ids) do\nif id ~= firstId then MikuTrySkip[id] = true end\nend\nif not Activity.bMikuTryIso then\nActivity.bMikuTryIso = true\nlocal oldIso = Activity.IsOpen\nActivity.IsOpen = function(a, b)\nlocal n = tonumber(a)\nif n and MikuTrySkip and MikuTrySkip[n] then return false end\nreturn oldIso(a, b)\nend\nend\nelse\nMikuTrySkip = nil\nend\nMikuTryWin2 = true\nMikuTryWinFloor = floor\nMikuTryWinEra = era\nend\nMikuTryApply = gtry\ngtry()\nif not MikuTryHooked2 then\nMikuTryHooked2 = true\nif DLC_Logic and DLC_Logic.InitFuncObj then\nlocal oldInit = DLC_Logic.InitFuncObj\nDLC_Logic.InitFuncObj = function(...) local r = oldInit(...) if MikuTryApply then MikuTryApply() end return r end\nend\nif Activity and Activity.LoadActivitiesConfig then\nlocal oldLoad = Activity.LoadActivitiesConfig\nActivity.LoadActivitiesConfig = function(...) MikuTryWin2 = nil local r = oldLoad(...) if MikuTryApply then MikuTryApply() end return r end\nend\nend",
			"ShopPresaleGuard" => "local M = IBLogic\nif type(M) == [[table]] and M.bMikuPresaleFix ~= true then\nM.bMikuPresaleFix = true\nlocal fnOld = M.CheckPreSellItem\nif type(fnOld) == [[function]] then\nM.CheckPreSellItem = function(...)\nlocal ok, a, b = pcall(fnOld, ...)\nif ok then return a, b end\nreturn false\nend\nend\nend",
			"BagRogueRelicUnlock" => "local M = BagRogueConfig\nif type(M) == [[table]] and M.bMikuRelicFix ~= true then\nM.bMikuRelicFix = true\nlocal fnOld = M.GetCurTimeConfig\nif type(fnOld) == [[function]] then\nlocal tbPool = {\n{ 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010 },\n{ 1011, 1012, 1013, 1014, 1015, 1016, 1017, 1018, 1019, 1020 },\n{ 1021, 1022, 1023, 1024, 1025, 1026, 1027, 1028, 1029, 1030 },\n}\nM.GetCurTimeConfig = function(...)\nlocal cfg = fnOld(...)\nif type(cfg) == [[table]] then\nlocal ru = cfg.tbRelicUnlock\nif type(ru) ~= [[table]] or #ru == 0 then\ncfg.tbRelicUnlock = tbPool\nend\n-- 112v：行前整备入口解锁。CheckOpenConfig 第三道关实锤\n-- Condition.Check(tbCondition)（{1={1=1,2=30}}，类型1参数30，\n-- error.condition_limit 的来源）。清空条件判定恒过。\ncfg.tbCondition = {}\nend\nreturn cfg\nend\nend\nend",
			"BagRogueRelicOption" => "-- 115i：GM 段修真根因。115h 埋点实锤：attempt to call a table\n-- value (field loaded)——package.loaded 后紧跟长括号字面量在\n-- Lua 里是调用语法（f 带字符串参数形式），不是取索引！115d 起\n-- 段内第一句就炸，pcall 静默吞掉，115g 错误处理器又被文件锁挡\n-- 住。修复：模块名走临时变量加单括号索引（与 probe 段同款写\n-- 法）；恢复元表 __index 双层 wrap（rawget 与 rawset）；埋点与\n-- 错误双写保留；dump 保留（60 函数 300KB 上限）。\nlocal le0 = io.open([[D:/DSH/.tools/c115_gm_err.txt]], [[a]])\nlocal lg0 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nlocal function glog(s0)\nif lg0 then\nlg0:write(s0)\nlg0:write(string.char(10))\nend\nend\nlocal function gelog(s0)\nif le0 then\nle0:write(s0)\nle0:write(string.char(10))\nend\nend\nlocal okg9, errg9 = pcall(function()\nglog([[=== chunk v115z enter ===]])\n-- 115ah：GM 屏蔽归口到 pak 覆盖包（2026-11-08 定稿）。\n-- 覆盖包 Game/Content/Paks/mods/Patch_Xpand_NoGM_S1-Windows_100_P.pak\n-- 把 GMCommandRegister.lua 与 UMG/GM/umg_adingm.lua 覆盖成 0 字节，\n-- GM 指令与面板逻辑即消失；删掉该 pak 就全部恢复，服务端不用改。\n-- 但 0 字节只让 lua 模块挂不上，umg_adingm 的 UMG 控件资源仍在主 UI\n-- pak 里：进战斗 PreRelease.EnterFight 之后游戏自己会 UI.Open(AdinGM)，\n-- 控件照样被创建，露出设计器默认控件（白块 / 99999999），表现为\n-- 「战斗场景图标错乱、GM 图标重叠」。因此下面 115ak 只补一道兜底。\n-- 115ak：GM 兜底（判据 = 覆盖包在不在）。在 → GM 已被 pak 停用，拦住\n-- AdinGM 的打开并隐藏残留实例；不在（删掉或改名 .off）→ GM 恢复，两道\n-- 都不生效，游戏原样打开面板。这样「删 pak 即恢复 GM」依然成立。\n-- 注意：本段别用加号、尖括号、和号与引号，Send 会整条丢弃。\nlocal function gmPakOff()\nlocal paths = {\n[[D:/GAME/SnowBreak/Game/Content/Paks/mods/Patch_Xpand_NoGM_S1-Windows_100_P.pak]],\n[[../../Content/Paks/mods/Patch_Xpand_NoGM_S1-Windows_100_P.pak]],\n[[../../../Content/Paks/mods/Patch_Xpand_NoGM_S1-Windows_100_P.pak]],\n[[../../../Game/Content/Paks/mods/Patch_Xpand_NoGM_S1-Windows_100_P.pak]],\n}\nfor pi = 1, #paths do\nlocal fh = io.open(paths[pi], [[rb]])\nif fh then\nfh:close()\nreturn true\nend\nend\nreturn false\nend\nlocal function gmname(s2)\nlocal okg, g = pcall(function() return s2:GetName() end)\nif okg == true and type(g) == [[string]] then\nlocal lo = string.lower(g)\nif string.find(lo, [[adingm]], 1, true) ~= nil or string.find(lo, [[pnlgm]], 1, true) ~= nil or string.find(lo, [[btngm]], 1, true) ~= nil or string.find(lo, [[newgm]], 1, true) ~= nil then\nreturn true, g\nend\nreturn false, g\nend\nreturn false, [[?]]\nend\nlocal function gmhide(v)\nlocal WU = rawget(_G, [[WidgetUtils]])\nif type(WU) ~= [[table]] then WU = package.loaded[[UMG.WidgetUtils]] end\nif type(WU) == [[table]] and type(WU.Hidden) == [[function]] then\npcall(WU.Hidden, v)\nreturn true\nend\nreturn false\nend\nlocal function gmscan()\nif gmPakOff() ~= true then return 0 end\nlocal n = 0\nlocal UIh = rawget(_G, [[UI]])\nif type(UIh) ~= [[table]] then return n end\nlocal tbw = rawget(UIh, [[tbWidget]])\nif type(tbw) ~= [[table]] then return n end\nfor wk, wv in pairs(tbw) do\nif type(wv) == [[table]] then\nfor fk, fv in pairs(wv) do\nif type(fv) == [[userdata]] and gmname(fv) == true then\ngmhide(fv)\nn = n - (-1)\nend\nend\nelseif type(wv) == [[userdata]] and gmname(wv) == true then\ngmhide(wv)\nn = n - (-1)\nend\nend\nreturn n\nend\nlocal gmtick = 0\nlocal function gmrecheck()\ngmtick = gmtick - (-1)\nif gmtick % 200 ~= 0 then return end\npcall(gmscan)\nend\nlocal lastt = 0\nlocal function gmkeep()\nif gmPakOff() ~= true then return end\nlocal okc, vc = pcall(os.clock)\nif okc == true and type(vc) == [[number]] then\nlocal d = vc - lastt\nif lastt ~= 0 and d - 0.15 ~= math.abs(d - 0.15) then return end\nlastt = vc\nend\nlocal hh = gmscan()\nif hh ~= 0 then\nglog([[gm keep hit=]] .. tostring(hh))\nend\nend\nlocal WUk = rawget(_G, [[WidgetUtils]])\nif type(WUk) ~= [[table]] then WUk = package.loaded[[UMG.WidgetUtils]] end\nif type(WUk) == [[table]] then\nfor _, fnk in pairs({ [[SetText]], [[Visible]], [[DisplayItems]], [[AddChildToPanel]], [[SetIcon]] }) do\nlocal ofk = rawget(WUk, fnk)\nif type(ofk) == [[function]] then\nrawset(WUk, fnk, function(...)\npcall(gmkeep)\nreturn ofk(...)\nend)\nend\nend\nend\nif rawget(_G, [[bMikuGmGate]]) ~= true then\nrawset(_G, [[bMikuGmGate]], true)\nlocal seen = {}\nlocal cands = {}\nlocal function addt(tb)\nif type(tb) ~= [[table]] then return end\nif seen[tb] == true then return end\nseen[tb] = true\nif type(rawget(tb, [[OpenInner]])) == [[function]] or type(rawget(tb, [[Open]])) == [[function]] then\ncands[#cands - (-1)] = tb\nend\nend\npcall(function()\nfor k, v in pairs(package.loaded) do\nif type(k) == [[string]] then addt(v) end\nend\nend)\npcall(function()\nfor k, v in pairs(_G) do\nif type(k) == [[string]] then addt(v) end\nend\nend)\nlocal function agnm(a1, a2)\nif type(a1) == [[string]] then return a1 end\nif type(a2) == [[string]] then return a2 end\nreturn nil\nend\nlocal function agtake(n2)\nif type(n2) ~= [[string]] then return false end\nlocal lo2 = string.lower(n2)\nif string.find(lo2, [[adingm]], 1, true) ~= nil then return true end\nif string.find(lo2, [[luaerror]], 1, true) ~= nil then return true end\nreturn false\nend\nlocal function agwrap(ofn)\nreturn function(a1, a2, a3, ...)\nif agtake(agnm(a1, a2)) == true then\nif gmPakOff() == true then\nreturn\nend\nelse\ngmrecheck()\nend\nreturn ofn(a1, a2, a3, ...)\nend\nend\nlocal hit = 0\nfor ci = 1, #cands do\nlocal ctb = cands[ci]\nlocal f1 = rawget(ctb, [[OpenInner]])\nif type(f1) == [[function]] then\nrawset(ctb, [[OpenInner]], agwrap(f1))\nhit = hit - (-1)\nend\nlocal f2 = rawget(ctb, [[Open]])\nif type(f2) == [[function]] then\nrawset(ctb, [[Open]], agwrap(f2))\nhit = hit - (-1)\nend\nlocal mt = getmetatable(ctb)\nif type(mt) == [[table]] and type(mt.__index) == [[table]] then\nlocal ix = mt.__index\nlocal f3 = rawget(ix, [[OpenInner]])\nif type(f3) == [[function]] then\nrawset(ix, [[OpenInner]], agwrap(f3))\nhit = hit - (-1)\nend\nlocal f4 = rawget(ix, [[Open]])\nif type(f4) == [[function]] then\nrawset(ix, [[Open]], agwrap(f4))\nhit = hit - (-1)\nend\nend\nend\nglog([[gm gate hooked=]] .. tostring(hit) .. [[ pkgoff=]] .. tostring(gmPakOff()))\nlocal h0 = 0\npcall(function() h0 = gmscan() end)\nglog([[gm scan hide=]] .. tostring(h0))\nend\n\nlocal esw0 = io.open([[D:/DSH/.tools/miku_switch.txt]], [[r]])\nlocal eon0 = true\nif esw0 then\nlocal c0 = esw0:read([[*l]]) or [[]]\nesw0:close()\nif string.find(c0, [[false]], 1, true) ~= nil then\neon0 = false\nend\nend\nglog([[errpop config=]] .. tostring(eon0))\nif eon0 == false and rawget(_G, [[bMikuErrPopDone]]) ~= true then\nrawset(_G, [[bMikuErrPopDone]], true)\nlocal cz0 = utf8.char(0x62A5, 0x9519, 0x60C5, 0x51B5)\nlocal ecnt0 = 0\nlocal function escan0(efn, eowner, ekey, emodname)\nif type(efn) ~= [[function]] or type(ekey) ~= [[string]] or math.min(ecnt0, 39) == 39 then\nreturn\nend\nlocal oke, dpe = pcall(string.dump, efn)\nif oke and type(dpe) == [[string]] then\nif string.find(dpe, [[Lua error message]], 1, true) ~= nil or string.find(dpe, [[stack traceback]], 1, true) ~= nil or string.find(dpe, cz0, 1, true) ~= nil then\nrawset(eowner, ekey, function() end)\necnt0 = ecnt0 - (-1)\nglog([[errpop wrap: ]] .. emodname .. [[.]] .. ekey)\nend\nend\nend\nfor emk, emv in pairs(package.loaded) do\nif type(emk) == [[string]] and type(emv) == [[table]] and math.min(ecnt0, 39) ~= 39 then\npcall(function()\nfor ek0, ev0 in pairs(emv) do\nescan0(ev0, emv, ek0, emk)\nend\nlocal emt = getmetatable(emv)\nif type(emt) == [[table]] and type(emt.__index) == [[table]] then\nfor ek1, ev1 in pairs(emt.__index) do\nescan0(ev1, emt.__index, ek1, emk)\nend\nend\nend)\nend\nend\nglog([[errpop done got=]] .. tostring(ecnt0))\nend\n-- 115af：GirlRush 报错弹窗消除（第二版）。115ae 一条日志都没留下，\n-- 说明按 GirlRush.GirlRushLevelCfg 这个 key 取出来的不是函数表，\n-- 或该模块此刻还没进 package.loaded。改为模糊收集候选表：\n-- package.loaded 与 _G 里 key 含 GirlRush 的所有 table，逐个表的\n-- 函数用 debug.getinfo 的 linedefined 找 515~535 区间（报错栈是\n-- :523），命中就地 pcall 包装、出错静默返回 nil；全量字节码存\n-- c115af.bin，过程写 c115af.txt（含每个候选表的函数数与命中数）。\nif rawget(_G, [[bMikuGrWrap2]]) ~= true then\nrawset(_G, [[bMikuGrWrap2]], true)\nlocal afn = 0\nlocal afbuf = [[]]\nlocal afbufn = 0\nlocal function aflog(s)\nif math.min(afn, 60) == 60 then return end\nafn = afn - (-1)\npcall(function()\nlocal f = io.open([[D:/DSH/.tools/c115af.txt]], [[a]])\nif f then\nf:write(tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend)\nend\naflog([[af enter]])\nlocal cands = {}\nlocal seen = {}\nlocal function addcand(name, tb)\nif type(tb) ~= [[table]] then return end\nif seen[tb] == true then return end\nseen[tb] = true\ncands[#cands - (-1)] = { name, tb }\nend\nfor ck, cv in pairs(package.loaded) do\nif type(ck) == [[string]] and string.find(ck, [[GirlRush]], 1, true) ~= nil then\naddcand([[P:]] .. ck, cv)\nend\nend\npcall(function()\nfor ck2, cv2 in pairs(_G) do\nif type(ck2) == [[string]] and string.find(ck2, [[GirlRush]], 1, true) ~= nil then\naddcand([[G:]] .. ck2, cv2)\nend\nend\nend)\naflog([[cands=]] .. tostring(#cands))\nlocal found = 0\nlocal wrapn2 = 0\nfor ci = 1, #cands do\nlocal cnm = cands[ci][1]\nlocal ctb = cands[ci][2]\nlocal list = {}\nlocal inrange = 0\nfor fk, fv in pairs(ctb) do\nif type(fk) == [[string]] and type(fv) == [[function]] then\nlocal ln = -1\npcall(function()\nlocal info = debug.getinfo(fv, [[S]])\nif type(info) == [[table]] and type(info.linedefined) == [[number]] then\nln = info.linedefined\nend\nend)\nif math.min(afbufn, 80) ~= 80 then\nlocal okd, dd = pcall(string.dump, fv)\nif okd and type(dd) == [[string]] and math.min(#afbuf, 700000) ~= 700000 then\nafbufn = afbufn - (-1)\nafbuf = afbuf .. [[###]] .. cnm .. [[.]] .. fk .. [[#ln=]] .. tostring(ln) .. [[###]] .. string.char(10) .. dd .. string.char(10)\nend\nend\nlist[#list - (-1)] = { fk, fv, ln }\nif math.min(ln, 515) == 515 and math.min(ln, 535) == ln then\ninrange = inrange - (-1)\nfound = found - (-1)\nend\nend\nend\naflog([[cand ]] .. cnm .. [[ funcs=]] .. tostring(#list) .. [[ inrange=]] .. tostring(inrange))\nfor li = 1, #list do\nlocal fk2 = list[li][1]\nlocal fv2 = list[li][2]\nlocal ln2 = list[li][3]\nif math.min(ln2, 515) == 515 and math.min(ln2, 535) == ln2 then\nrawset(ctb, fk2, function(a1, a2, a3, ...)\nlocal okr, r1, r2, r3 = pcall(fv2, a1, a2, a3, ...)\nif okr == true then\nreturn r1, r2, r3\nend\naflog([[hit ]] .. cnm .. [[.]] .. fk2 .. [[ ln=]] .. tostring(ln2) .. [[ err=]] .. tostring(r1))\nreturn nil\nend)\nwrapn2 = wrapn2 - (-1)\nend\nend\nend\nlocal afp = io.open([[D:/DSH/.tools/c115af.bin]], [[wb]])\nif afp then\nafp:write(afbuf)\nafp:close()\nend\naflog([[done found=]] .. tostring(found) .. [[ wrap=]] .. tostring(wrapn2))\nend\n-- 115ab：闪屏终局修复（客户端赛季时间窗矫正）。115aa 探针实锤：\n-- 每秒一次的 BossPvpLogic.GetOpenID 全部来自\n-- umg_dungeonsboss_main.lua:841 的 Tick；而 GetTimeCfg 返回的是\n-- 本地表（challenge/bosspvp/boss_challenge.txt）里 ID 3 那一行的\n-- 2023 年窗口，nEndTime 为 1700452800，早已过期。Tick 的倒计时\n-- 归零判定于是永远成立，每秒重问服务端、每秒判定仍不满足、每秒\n-- ShowConnection 重开又关、每秒重播 dungeonsboss 相机，即闪屏。\n-- 服务端下发的窗口本来是合法的（当天 10:00 到 22:00），但客户端\n-- 按 id 取本地表，压根不看下发值。这里就地矫正 GetTimeCfg 的返回\n-- 值：本地 10:00 与 22:00 为 12 小时轮换分界（与服务端同一网格），\n-- 窗口一旦过期就改成当前这一格，倒计时恢复、轮询消失。\nif rawget(_G, [[bMikuBpHook]]) ~= true then\nrawset(_G, [[bMikuBpHook]], true)\nlocal abn = 0\nlocal function ablog(s7)\nif math.min(abn, 8) == 8 then return end\nabn = abn - (-1)\npcall(function()\nlocal f7 = io.open([[D:/DSH/.tools/c115ab.txt]], [[a]])\nif f7 then\nf7:write(tostring(s7))\nf7:write(string.char(10))\nf7:close()\nend\nend)\nend\nlocal function abwin(now)\nlocal d = os.date([[*t]], now)\nlocal h10 = os.time({ year = d.year, month = d.month, day = d.day, hour = 10, min = 0, sec = 0 })\nlocal h22 = os.time({ year = d.year, month = d.month, day = d.day, hour = 22, min = 0, sec = 0 })\nif math.min(now - h22, 0) == 0 then\nreturn h22, h22 - (-43200)\nend\nif math.min(now - h10, 0) == 0 then\nreturn h10, h22\nend\nreturn h10 - 43200, h10\nend\nlocal function abfix(cfg)\nif type(cfg) ~= [[table]] then return end\nlocal now = os.time()\nlocal en = cfg.nEndTime\nif type(en) ~= [[number]] then\nen = 0\nend\nlocal left = en - now\nif math.min(left, 0) == 0 and left ~= 0 then\nreturn\nend\nlocal st8, en8 = abwin(now)\ncfg.nStartTime = st8\ncfg.nEndTime = en8\nif type(cfg.nRewardTime) == [[number]] then\ncfg.nRewardTime = en8\nend\nablog([[fix id=]] .. tostring(cfg.nID) .. [[ old-en=]] .. tostring(en) .. [[ new-en=]] .. tostring(en8) .. [[ now=]] .. tostring(now))\nend\nlocal function abinstall()\nif rawget(_G, [[bMikuBpFix]]) == true then return end\nlocal BPA = rawget(_G, [[BossPvpLogic]])\nif type(BPA) ~= [[table]] then return end\nlocal fc = rawget(BPA, [[GetTimeCfg]])\nif type(fc) ~= [[function]] then return end\nrawset(_G, [[bMikuBpFix]], true)\nrawset(BPA, [[GetTimeCfg]], function(...)\nlocal cfgA = fc(...)\npcall(abfix, cfgA)\nreturn cfgA\nend)\nablog([[installed]])\nend\n-- 115ac：背包拼图 SetItemPos 护栏前置化。108b/112q 的护栏挂在\n-- UI.Open 上（等面板打开才装），且只靠 J.bMikuPosGuard 守标；\n-- 登录时若 bag_jigsaw 模块已在 package.loaded 里就直接装，不等\n-- 面板事件。装的时候顺手 dump 一次原生 SetItemPos 字节码\n-- （c115ac_bag.bin），下次真炸了就有原文可读，能照抄实现。\nlocal function abposguard()\nif rawget(_G, [[bMikuPosFix]]) == true then return end\nlocal okJ, J = pcall(function()\nlocal nmJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nreturn package.loaded[nmJ]\nend)\nif not okJ or type(J) ~= [[table]] then return end\nlocal op = rawget(J, [[SetItemPos]])\nif type(op) ~= [[function]] then return end\nrawset(_G, [[bMikuPosFix]], true)\npcall(function()\nlocal okD, dg = pcall(string.dump, op)\nif okD and type(dg) == [[string]] then\nlocal fg = io.open([[D:/DSH/.tools/c115ac_bag.bin]], [[wb]])\nif fg then\nfg:write(dg)\nfg:close()\nend\nend\nend)\nif rawget(J, [[bMikuPosGuard]]) == true then\nablog([[pos guard already]])\nreturn\nend\nrawset(J, [[bMikuPosGuard]], true)\nrawset(J, [[SetItemPos]], function(s2, ...)\nlocal okC, errC = pcall(op, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nlocal fixed = 0\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\nlocal okp = pcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nif okp then\nfixed = fixed - (-1)\nend\nend\nend\nend\nablog([[SetItemPos guard hit fixed=]] .. tostring(fixed) .. [[ err=]] .. tostring(errC))\nend)\nablog([[pos guard inst2]])\nend\nabposguard()\nabinstall()\npcall(function()\nlocal WU9 = rawget(_G, [[WidgetUtils]])\nif type(WU9) ~= [[table]] then return end\nfor _, k9 in pairs({ [[SetText]], [[Visible]] }) do\nlocal f9 = rawget(WU9, k9)\nif type(f9) == [[function]] then\nrawset(WU9, k9, function(...)\nif rawget(_G, [[bMikuBpFix]]) ~= true then pcall(abinstall) end\nif rawget(_G, [[bMikuPosFix]]) ~= true then pcall(abposguard) end\nreturn f9(...)\nend)\nend\nend\nend)\nend\nend)\nif not okg9 then\nglog([[GM ERR: ]] .. tostring(errg9))\ngelog([[GM ERR: ]] .. tostring(errg9))\nend\nif lg0 then\nlg0:close()\nend\nif le0 then\nle0:close()\nend\nlocal C = BagRogueConfig\nif type(C) == [[table]] and C.bMikuRelicOpt ~= true then\nC.bMikuRelicOpt = true\nlocal defs = {\n{ 3091, 301 }, { 3092, 301 },\n{ 3191, 311 }, { 3192, 311 },\n{ 3291, 321 }, { 3292, 321 },\n{ 3391, 331 }, { 3392, 331 },\n}\nfor i = 1, #defs do\nlocal d = defs[i]\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nif C.tbAdventure[d[1]] == nil then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = d[2], nOptionType = 9, nBuffId = 0,\nnOptionIcon = 110010331,\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = [[bagrogue.MikuOption9]],\nsOptionDesc = [[bagrogue.MikuDes9]],\ntbOption = { 1 }, tbUnlockEnding = {},\n}\nend\nend\nlocal L = Localization\nif type(L) == [[table]] and type(L.tbData) == [[table]] and type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nlocal k1 = [[bagrogue.MikuOption9]]\nlocal k2 = [[bagrogue.MikuDes9]]\nif L.tbData[k1] == nil then\nL.tbData[k1] = utf8.char(0x968F, 0x673A, 0x79D8, 0x5B9D) .. [[*{1}]]\nend\nif L.tbData[k2] == nil then\nL.tbData[k2] = utf8.char(0x4F60,0x4ECE,0x884C,0x56CA,0x6DF1,0x5904,0x7FFB,0x51FA,0x4E86,0x4E00,0x679A,0x672A,0x77E5,0x7684,0x79D8,0x5B9D,0x3002,0x82E5,0x8EAB,0x4E0A,0x5947,0x5668,0x672A,0x6EE1,0x4E09,0x4EF6,0x5219,0x7ACB,0x5373,0x88C5,0x5165,0xFF0C,0x82E5,0x5DF2,0x6EE1,0x4E09,0x4EF6,0x5219,0x6298,0x7B97,0x6210,0x6F20,0x5317,0x661F,0x7802,0x002A,0x0032,0x0030,0x0030,0x3002)\nend\nlocal k3 = [[bagrogue.MikuDes10]]\nif L.tbData[k3] == nil then\nL.tbData[k3] = utf8.char(0x628A, 0x5B83, 0x5E26, 0x5728, 0x8EAB, 0x4E0A, 0x5427, 0x2014, 0x2014, 0x5947, 0x5668, 0x7ACB, 0x5373, 0x751F, 0x6548, 0xFF0C, 0x901A, 0x5173, 0x540E, 0x56DE, 0x6536, 0x3002)\nend\nend\nend\nif type(C) == [[table]] and C.bMikuRelicOpt2 ~= true then\nC.bMikuRelicOpt2 = true\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nlocal rdefs = {\n{ 9501, 1001, 1001431, 1 }, { 9502, 1002, 1001432, 2 }, { 9503, 1003, 1001435, 3 }, { 9504, 1004, 1001436, 4 }, { 9505, 1005, 1001433, 5 }, { 9506, 1006, 1001434, 6 }, { 9507, 1007, 1001437, 7 }, { 9508, 1008, 1001438, 8 }, { 9509, 1009, 1001439, 9 }, { 9510, 1010, 1001440, 10 }, { 9511, 1011, 1001441, 11 }, { 9512, 1012, 1001442, 12 }, { 9513, 1013, 1001443, 13 }, { 9514, 1014, 1001444, 14 }, { 9515, 1015, 1001447, 15 }, { 9516, 1016, 1001448, 16 }, { 9517, 1017, 1001445, 17 }, { 9518, 1018, 1001446, 18 }, { 9519, 1019, 1001449, 19 }, { 9520, 1020, 1001450, 20 }, { 9521, 1021, 1001481, 21 }, { 9522, 1022, 1001482, 22 }, { 9523, 1023, 1001483, 23 }, { 9524, 1024, 1001484, 24 }, { 9525, 1025, 1001485, 25 }, { 9526, 1026, 1001486, 26 }, { 9527, 1027, 1001487, 27 }, { 9528, 1028, 1001488, 28 }, { 9529, 1029, 1001489, 29 }, { 9530, 1030, 1001490, 30 }\n}\nfor i = 1, #rdefs do\nlocal d = rdefs[i]\nif C.tbAdventure[d[1]] == nil then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = 0, nOptionType = 10, nBuffId = 0,\nnOptionIcon = d[3],\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = [[bagrogue.RelicName]] .. tostring(d[4]),\nsOptionDesc = [[bagrogue.MikuDes10]],\ntbOption = { d[2] }, tbUnlockEnding = {},\n}\nend\nend\nend\n-- 112e：秘宝卡客户端注入。gac 探针实锤客户端 adventure 表不是从\n-- adventure.json 实时读的——奇器卡 9501..9530 靠上面的运行时注入才有，\n-- 秘宝卡 12011..12275 同样必须注入（GetAdventureConfig 才查得到）。\n-- 名字/图标运行时从 GetJigsawConfig 取，描述用通用文案 MikuJigDes。\nif type(C) == [[table]] and C.bMikuJigOpt ~= true then\nC.bMikuJigOpt = true\nif type(C.tbAdventure) ~= [[table]] then C.tbAdventure = {} end\nlocal jdefs = {\n{ 12011, 1011 }, { 12012, 1012 }, { 12013, 1013 }, { 12014, 1014 }, { 12015, 1015 }, { 12021, 1021 }, { 12022, 1022 }, { 12023, 1023 },\n{ 12024, 1024 }, { 12025, 1025 }, { 12031, 1031 }, { 12032, 1032 }, { 12033, 1033 }, { 12034, 1034 }, { 12035, 1035 }, { 12041, 1041 },\n{ 12042, 1042 }, { 12043, 1043 }, { 12044, 1044 }, { 12045, 1045 }, { 12051, 1051 }, { 12052, 1052 }, { 12053, 1053 }, { 12054, 1054 },\n{ 12055, 1055 }, { 12061, 1061 }, { 12062, 1062 }, { 12063, 1063 }, { 12064, 1064 }, { 12065, 1065 }, { 12071, 1071 }, { 12072, 1072 },\n{ 12073, 1073 }, { 12074, 1074 }, { 12075, 1075 }, { 12081, 1081 }, { 12082, 1082 }, { 12083, 1083 }, { 12084, 1084 }, { 12085, 1085 },\n{ 12091, 1091 }, { 12092, 1092 }, { 12093, 1093 }, { 12094, 1094 }, { 12095, 1095 }, { 12101, 1101 }, { 12102, 1102 }, { 12103, 1103 },\n{ 12104, 1104 }, { 12105, 1105 }, { 12111, 1111 }, { 12112, 1112 }, { 12113, 1113 }, { 12114, 1114 }, { 12115, 1115 }, { 12121, 1121 },\n{ 12122, 1122 }, { 12123, 1123 }, { 12124, 1124 }, { 12125, 1125 }, { 12131, 1131 }, { 12132, 1132 }, { 12133, 1133 }, { 12134, 1134 },\n{ 12135, 1135 }, { 12141, 1141 }, { 12142, 1142 }, { 12143, 1143 }, { 12144, 1144 }, { 12145, 1145 }, { 12151, 1151 }, { 12152, 1152 },\n{ 12153, 1153 }, { 12154, 1154 }, { 12155, 1155 }, { 12161, 1161 }, { 12162, 1162 }, { 12163, 1163 }, { 12164, 1164 }, { 12165, 1165 },\n{ 12171, 1171 }, { 12172, 1172 }, { 12173, 1173 }, { 12174, 1174 }, { 12175, 1175 }, { 12181, 1181 }, { 12182, 1182 }, { 12183, 1183 },\n{ 12184, 1184 }, { 12185, 1185 }, { 12191, 1191 }, { 12192, 1192 }, { 12193, 1193 }, { 12194, 1194 }, { 12195, 1195 }, { 12201, 1201 },\n{ 12202, 1202 }, { 12203, 1203 }, { 12204, 1204 }, { 12205, 1205 }, { 12221, 1221 }, { 12222, 1222 }, { 12223, 1223 }, { 12224, 1224 },\n{ 12225, 1225 }, { 12231, 1231 }, { 12232, 1232 }, { 12233, 1233 }, { 12234, 1234 }, { 12235, 1235 }, { 12241, 1241 }, { 12242, 1242 },\n{ 12243, 1243 }, { 12244, 1244 }, { 12245, 1245 }, { 12251, 1251 }, { 12252, 1252 }, { 12253, 1253 }, { 12254, 1254 }, { 12255, 1255 },\n{ 12261, 1261 }, { 12262, 1262 }, { 12263, 1263 }, { 12264, 1264 }, { 12265, 1265 }, { 12271, 1271 }, { 12272, 1272 }, { 12273, 1273 },\n{ 12274, 1274 }, { 12275, 1275 },\n}\nfor i = 1, #jdefs do\nlocal d = jdefs[i]\nif C.tbAdventure[d[1]] == nil then\nlocal okj, jc = pcall(C.GetJigsawConfig, C, d[2])\nif okj and type(jc) == [[table]] then\nC.tbAdventure[d[1]] = {\nnId = d[1], nGroupId = 0, nOptionType = 10, nBuffId = 0,\nnOptionIcon = jc.nIcon,\nsEventName = [[bagrogue.CommonEvent1]],\nsOptionName = jc.sName,\nsOptionDesc = [[bagrogue.MikuJigDes]],\ntbOption = { d[2] }, tbUnlockEnding = {},\n}\nend\nend\nend\nlocal L = Localization\nif type(L) == [[table]] and type(L.tbData) == [[table]] and type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nlocal k4 = [[bagrogue.MikuJigDes]]\nif L.tbData[k4] == nil then\nL.tbData[k4] = utf8.char(0x79D8,0x5B9D,0x5DF2,0x6536,0x5165,0x884C,0x56CA,0xFF0C,0x53EF,0x5728,0x56FE,0x5F55,0x4E2D,0x67E5,0x770B,0x3002)\nend\nend\n-- 112h：本地化缺键记录器。图录秘宝「增益效果」全显示问号——在 tbData\n-- 上挂 __index 回退（只在查不到时触发，不改任何行为），命中 Talent/\n-- Affix/Jigsaw/Relic/bagrogue 相关键记 lk 日志，定位「？」是缺键还是缺参。\nif type(L) == [[table]] and type(L.tbData) == [[table]] and L.bMikuLkHook ~= true then\nL.bMikuLkHook = true\nif getmetatable(L.tbData) == nil then\nsetmetatable(L.tbData, {\n__index = function(t, k)\nlocal s9 = tostring(k)\nlocal f9 = string.find(s9, [[Talent]], 1, true)\nif f9 == nil then f9 = string.find(s9, [[Affix]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[Jigsaw]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[Relic]], 1, true) end\nif f9 == nil then f9 = string.find(s9, [[bagrogue]], 1, true) end\nif f9 ~= nil then\nlocal f = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif f then\nf:write([[lk:]] .. s9 .. string.char(10))\nf:close()\nend\nend\nreturn nil\nend\n})\nend\nend\n-- 112p：行前整备默认解锁。IsBagGridUnlock 字节码反汇编实锤：\n-- 解锁条件 = GetBagUnlockConfig(id).nUnlockOrder 中 BagRogue:GetBagMaxIndex()。\n-- 运行时遍历 BagRogueConfig 全部子表，凡子元素（或子表自身）含\n-- nUnlockOrder 数字的条目一律置 0——背包功能解锁表全开，\n-- 行前整备（TxtBackPackBuffReady）随之默认解锁。\npcall(function()\nfor kU, vU in pairs(C) do\nif type(vU) == [[table]] then\nif type(vU.nUnlockOrder) == [[number]] then\nvU.nUnlockOrder = 0\nend\nfor kV, vV in pairs(vU) do\nif type(vV) == [[table]] and type(vV.nUnlockOrder) == [[number]] then\nvV.nUnlockOrder = 0\nend\nend\nend\nend\nend)\nend\n-- 112l/112q：SetItemPos 护栏前置安装。第 108 节护栏挂在选卡链\n-- （tryPatchAfterOpen），但 bag_jigsaw 模块在打开背包页时才\n-- 加载——先选卡后开背包时 package.loaded 里还没有它，护栏\n-- 装不上，原生 SetItemPos 炸（invalid parameter A 上报）。\n-- 112q 事故复盘：旧版整段挂在 bMikuJigOpt 守卫块内且只 wrap\n-- 了 UI.OpenInner——本局实锤 OpenInner 从未触发（oi: 零产物），\n-- 背包面板走 UI.Open 打开；且 2.5s 推送时 UI 未就绪则 9s/20s\n-- 重推被外层守卫挡住永不重试。行前整备解锁后局外开背包即裸奔。\n-- 改为独立守卫块（UI 未就绪不置标，重推自动重试），Open 与\n-- OpenInner 双 wrap，装好写安装标记日志。\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and UI.bMikuPosGuardHook ~= true then\nUI.bMikuPosGuardHook = true\nlocal UIg = UI\nlocal function installPosGuard()\n-- 115i：package.loaded[[字面量]] 是调用语法不是索引（GM 段实锤\n-- attempt to call a table value），改临时变量单括号索引。\nlocal nmJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nlocal okJ, J = pcall(function() return package.loaded[nmJ] end)\nif okJ and type(J) == [[table]] and type(J.SetItemPos) == [[function]] and J.bMikuPosGuard ~= true then\nJ.bMikuPosGuard = true\nlocal lq1 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lq1 then\nlq1:write([[pos guard module installed]])\nlq1:write(string.char(10))\nlq1:close()\nend\nlocal origPos = J.SetItemPos\nJ.SetItemPos = function(s2, ...)\nlocal okC, errC = pcall(origPos, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\npcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nend\nend\nend\nlocal lf = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf then\nlf:write([[SetItemPos guard: ]] .. tostring(errC))\nlf:write(string.char(10))\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nlocal okCell = false\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos then okCell = true end\nend\nif not okCell then\nlf:write([[bad u=]] .. tostring(w and w.uId) .. [[ pos=]] .. tostring(w and w.nPos) ..\n[[ dir=]] .. tostring(w and w.nDir) ..\n[[ tp=]] .. tostring(w and w.tpCfg and w.tpCfg.ID))\nlf:write(string.char(10))\nend\nend\nlf:close()\nend\nend\nend\nend\n-- 112r：图录详情抓取前置。112n/112o 的 dump 全挂在 UI.OpenInner\n-- 与 BackpackRiki 名字守卫下，本局 oi: 零产物实锤图录面板同样走\n-- UI.Open 打开——抓取从未触发。挂在 112q 的 UI.Open wrap 里：\n-- 开任何含 ackpack 或 Riki 名面板后，枚举 package.loaded 全部\n-- backpack 或 Riki 模块，dump 模块函数与 metatable 方法（pk_/cx_\n-- 前缀），ItemInfo 详情类必在其中，一次性守标。\n-- 112w：图录详情类补 dump 提为独立函数。uw_backpack_riki_talent\n-- (_list) 要图录 tab 点击后才 require（bf missing 实锤），而 112v\n-- 把补 dump 放在 pk dump 守标之后——首触发即被 return 拦截，\n-- 后续面板事件永不执行。本函数每次 ackpack/Riki 面板事件都尝试\n-- （rt_ 前缀，bMikuBfCnt 上限 10，bf tail done 日志记抓取数）。\nlocal function dumpBfTail()\nlocal bf90 = {\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent_list]],\n}\nlocal bfc91 = tonumber(UIg.bMikuBfCnt) or 0\nif math.min(bfc91, 10) == 10 then return end\nlocal got99 = 0\nfor i92 = 1, #bf90 do\nlocal md93 = package.loaded[bf90[i92]]\nif type(md93) == [[table]] then\ngot99 = got99 - (-1)\nfor k94, v94 in pairs(md93) do\nif type(k94) == [[string]] and type(v94) == [[function]] then\nlocal ok95, d95 = pcall(string.dump, v94)\nif ok95 and type(d95) == [[string]] then\nlocal fn96 = io.open([[D:/DSH/.tools/c112_rt_]] .. tostring(i92) .. [[_]] .. k94 .. [[.bin]], [[wb]])\nif fn96 then\nfn96:write(d95)\nfn96:close()\nend\nend\nend\nend\nend\nend\nUIg.bMikuBfCnt = bfc91 - (-1)\nlocal lf97 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf97 then\nlf97:write([[bf tail done got=]] .. tostring(got99))\nlf97:write(string.char(10))\nlf97:close()\nend\nend\nlocal function dumpAfterOpen(nm)\nif type(nm) ~= [[string]] then return end\n-- 115b：碎石惊沙难度选择面板（名字含 lien 或 efense）独立 dump。\nif string.find(nm, [[lien]], 1, true) ~= nil or string.find(nm, [[efense]], 1, true) ~= nil then\n-- 115c：面板打开时补装 GetDifficultConfig wrap（与 mowstamp 同守标幂等）。\nif DefendLogic and type(DefendLogic.GetDifficultConfig) == [[function]] and DefendLogic.bMikuDiffUnlock ~= true then\nDefendLogic.bMikuDiffUnlock = true\nlocal oldGD2 = DefendLogic.GetDifficultConfig\nDefendLogic.GetDifficultConfig = function(...)\nlocal cfg2 = oldGD2(...)\nif type(cfg2) == [[table]] and cfg2.nUnlockCondition ~= nil then cfg2.nUnlockCondition = nil end\nreturn cfg2\nend\nend\nif UIg.bMikuAdDumped ~= true then\nUIg.bMikuAdDumped = true\nlocal cnta = 0\nfor mka, mva in pairs(package.loaded) do\nif type(mka) == [[string]] and type(mva) == [[table]]\nand (string.find(mka, [[lien]], 1, true) ~= nil or string.find(mka, [[efense]], 1, true) ~= nil) then\nfor mkc, mvc in pairs(mva) do\nif type(mkc) == [[string]] and type(mvc) == [[function]] and math.min(cnta, 200) ~= 200 then\ncnta = cnta - (-1)\nlocal okca, dca = pcall(string.dump, mvc)\nif okca and type(dca) == [[string]] then\nlocal fca = io.open([[D:/DSH/.tools/c115_ad_]] .. string.gsub(mka, [[%.]], [[_]]) .. [[_]] .. mkc .. [[.bin]], [[wb]])\nif fca then\nfca:write(dca)\nfca:close()\nend\nend\nend\nend\nlocal mta = getmetatable(mva)\nif type(mta) == [[table]] and type(mta.__index) == [[table]] then\nfor mkd, mvd in pairs(mta.__index) do\nif type(mkd) == [[string]] and type(mvd) == [[function]] and math.min(cnta, 200) ~= 200 then\ncnta = cnta - (-1)\nlocal okcd, dcd = pcall(string.dump, mvd)\nif okcd and type(dcd) == [[string]] then\nlocal fcd = io.open([[D:/DSH/.tools/c115_ad_]] .. string.gsub(mka, [[%.]], [[_]]) .. [[_]] .. mkd .. [[.bin]], [[wb]])\nif fcd then\nfcd:write(dcd)\nfcd:close()\nend\nend\nend\nend\nend\nend\nend\nlocal lfa = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lfa then\nlfa:write([[ad dump done nm=]] .. nm .. [[ cnt=]] .. tostring(cnta))\nlfa:write(string.char(10))\nlfa:close()\nend\nend\nend\nlocal isB = string.find(nm, [[ackpack]], 1, true) ~= nil\nlocal isR = string.find(nm, [[Riki]], 1, true) ~= nil\nlocal isR2 = string.find(nm, [[riki]], 1, true) ~= nil\nif not isB and not isR and not isR2 then return end\nif UIg.bMikuPkDumped == true then\npcall(dumpBfTail)\nreturn\nend\nUIg.bMikuPkDumped = true\nlocal cnt7 = 0\nfor mk7, mv7 in pairs(package.loaded) do\nif type(mk7) == [[string]] and type(mv7) == [[table]]\nand (string.find(mk7, [[ackpack]], 1, true) ~= nil\nor string.find(mk7, [[iki]], 1, true) ~= nil) then\nfor mk8, mv8 in pairs(mv7) do\nif type(mk8) == [[string]] and type(mv8) == [[function]] and math.min(cnt7, 170) ~= 170 then\ncnt7 = cnt7 - (-1)\nlocal okD7, d7 = pcall(string.dump, mv8)\nif okD7 and type(d7) == [[string]] then\nlocal fn7 = [[D:/DSH/.tools/c112_pk_]] .. string.gsub(mk7, [[%.]], [[_]]) .. [[_]] .. mk8 .. [[.bin]]\nlocal lf7 = io.open(fn7, [[wb]])\nif lf7 then\nlf7:write(d7)\nlf7:close()\nend\nend\nend\nend\nlocal mt7 = getmetatable(mv7)\nif type(mt7) == [[table]] and type(mt7.__index) == [[table]] then\nfor mk9, mv9 in pairs(mt7.__index) do\nif type(mk9) == [[string]] and type(mv9) == [[function]] and math.min(cnt7, 170) ~= 170 then\ncnt7 = cnt7 - (-1)\nlocal okD9, d9 = pcall(string.dump, mv9)\nif okD9 and type(d9) == [[string]] then\nlocal fn9 = [[D:/DSH/.tools/c112_cx_]] .. string.gsub(mk7, [[%.]], [[_]]) .. [[_]] .. mk9 .. [[.bin]]\nlocal lf9 = io.open(fn9, [[wb]])\nif lf9 then\nlf9:write(d9)\nlf9:close()\nend\nend\nend\nend\nend\nend\nend\n-- 112t：loaded 值非 table 的模块名记录（lv:）。uw_backpack_riki_talent\n-- 在 loaded（umin 实锤）却未被 dump，疑值类型非 table 被跳过。\nlocal lv50 = {}\nfor lk51, lv52 in pairs(package.loaded) do\nif type(lk51) == [[string]] and type(lv52) ~= [[table]]\nand string.find(lk51, [[ackpack]], 1, true) ~= nil then\nif math.min(#lv50, 20) ~= 20 then\ntable.insert(lv50, lk51 .. [[=]] .. type(lv52))\nend\nend\nend\nlocal lf69 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf69 then\nlf69:write([[lv:]] .. table.concat(lv50, [[,]]))\nlf69:write(string.char(10))\nlf69:close()\nend\n-- 112t：页面实例抓取。UI.GetUI(nm) 拿面板实例：函数字段直接 dump\n-- （ri2_），table 字段进一层（cf2_，ItemInfo 等子 widget 引用），\n-- 实例元表方法（cr2_）。详情类不在 loaded 模块表时由实例兜底抓到。\npcall(function()\nlocal gi53 = UIg.GetUI\nif type(gi53) ~= [[function]] then return end\nlocal ok54, inst55 = pcall(gi53, nm)\nif not ok54 or type(inst55) ~= [[table]] then return end\nlocal cnt57 = 0\nfor k58, v58 in pairs(inst55) do\nif type(v58) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok59, d59 = pcall(string.dump, v58)\nif ok59 and type(d59) == [[string]] then\nlocal fn60 = io.open([[D:/DSH/.tools/c112_ri2_]] .. tostring(k58) .. [[.bin]], [[wb]])\nif fn60 then\nfn60:write(d59)\nfn60:close()\nend\nend\nelseif type(v58) == [[table]] then\nfor k61, v61 in pairs(v58) do\nif type(v61) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok62, d62 = pcall(string.dump, v61)\nif ok62 and type(d62) == [[string]] then\nlocal fn63 = io.open([[D:/DSH/.tools/c112_cf2_]] .. tostring(k58) .. [[_]] .. tostring(k61) .. [[.bin]], [[wb]])\nif fn63 then\nfn63:write(d62)\nfn63:close()\nend\nend\nend\nend\nend\nend\nlocal rmt64 = getmetatable(inst55)\nif type(rmt64) == [[table]] and type(rmt64.__index) == [[table]] then\nfor k65, v65 in pairs(rmt64.__index) do\nif type(k65) == [[string]] and type(v65) == [[function]] and math.min(cnt57, 300) ~= 300 then\ncnt57 = cnt57 - (-1)\nlocal ok66, d66 = pcall(string.dump, v65)\nif ok66 and type(d66) == [[string]] then\nlocal fn67 = io.open([[D:/DSH/.tools/c112_cr2_]] .. tostring(k65) .. [[.bin]], [[wb]])\nif fn67 then\nfn67:write(d66)\nfn67:close()\nend\nend\nend\nend\nend\nlocal lf68 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf68 then\nlf68:write([[inst dump done cnt=]] .. tostring(cnt57) .. [[ nm=]] .. nm)\nlf68:write(string.char(10))\nlf68:close()\nend\nend)\n-- 112v 旧补 dump 段已提为 dumpBfTail（112w），见 dumpAfterOpen 开头。\nlocal lf8 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf8 then\nlf8:write([[pk dump done cnt=]] .. tostring(cnt7) .. [[ nm=]] .. nm)\nlf8:write(string.char(10))\nlf8:close()\nend\nend\nif type(UIg.OpenInner) == [[function]] and UIg.bMikuPGOI ~= true then\nUIg.bMikuPGOI = true\nlocal oldOI2 = UIg.OpenInner\nUIg.OpenInner = function(nm, ...)\nlocal r2 = table.pack(oldOI2(nm, ...))\npcall(installPosGuard)\npcall(dumpAfterOpen, nm)\nreturn table.unpack(r2, 1, r2.n)\nend\nend\nif type(UIg.Open) == [[function]] and UIg.bMikuPGO ~= true then\nUIg.bMikuPGO = true\nlocal oldOP2 = UIg.Open\nUIg.Open = function(nm, ...)\nlocal r2 = table.pack(oldOP2(nm, ...))\npcall(installPosGuard)\npcall(dumpAfterOpen, nm)\nreturn table.unpack(r2, 1, r2.n)\nend\nend\npcall(installPosGuard)\nlocal lq2 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lq2 then\nlq2:write([[pos guard hook installed]])\nlq2:write(string.char(10))\nlq2:close()\nend\nend\n-- 112r：行前整备解锁结构探针。bagunlock 置 0 与 BagRogueConfig\n-- 遍历都没解开入口锁——解锁判定读的真实表与字段未知。登录时\n-- 打：GetBagMaxIndex 返回值（gmi）、GetBagUnlockConfig 前几条\n-- 结构（guc）、BagRogueConfig 顶层键名（bgk）、全局含 Unlock\n-- 或 Function 关键词的表（gu）。一次拿到真实结构再精准改。\npcall(function()\nlocal fo1 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fo1 == nil then return end\n-- 112s：浅层序列化器（深度 2、每层 40 键，防循环靠深度上限）。\nlocal function serMk(tm, dm)\nif type(tm) ~= [[table]] then return tostring(tm) end\nif math.min(dm, 3) == 3 then return [[T]] end\nlocal ps42 = {}\nfor k43, v43 in pairs(tm) do\nif math.min(#ps42, 40) ~= 40 then\ntable.insert(ps42, tostring(k43) .. [[=]] .. serMk(v43, dm - (-1)))\nend\nend\nreturn [[{]] .. table.concat(ps42, [[,]]) .. [[}]]\nend\nlocal BR = _G.BagRogue\nif type(BR) == [[table]] then\nlocal v1 = nil\npcall(function() v1 = BR.GetBagMaxIndex(BR) end)\nfo1:write([[gmi:]] .. tostring(v1))\nfo1:write(string.char(10))\nfor i2 = 1, 6 do\nlocal c2 = nil\npcall(function() c2 = BR.GetBagUnlockConfig(BR, i2) end)\nlocal ps2 = {}\nif type(c2) == [[table]] then\nfor k3, v3 in pairs(c2) do\nif type(v3) == [[table]] then\ntable.insert(ps2, tostring(k3) .. [[=tbl]])\nelse\ntable.insert(ps2, tostring(k3) .. [[=]] .. tostring(v3))\nend\nend\nend\nfo1:write([[guc:]] .. tostring(i2) .. [[ ]] .. table.concat(ps2, [[ ]]))\nfo1:write(string.char(10))\nend\n-- 112s：解锁表深探。GetBagUnlockConfig(1..6) 返回 nil（guc 空），\n-- 参数不是 1..6；解锁配置实际在 tbBagUnlock / tbBagUnlockOrder /\n-- nMaxBagUnlock。全量序列化拿真实字段名。\nlocal bu16 = BR.tbBagUnlock\nif type(bu16) == [[table]] then\nfo1:write([[tbu:]] .. serMk(bu16, 0))\nfo1:write(string.char(10))\nend\nlocal bo17 = BR.tbBagUnlockOrder\nif type(bo17) == [[table]] then\nfo1:write([[tbo:]] .. serMk(bo17, 0))\nfo1:write(string.char(10))\nend\nfo1:write([[nmx:]] .. tostring(BR.nMaxBagUnlock))\nfo1:write(string.char(10))\nlocal oc18 = nil\npcall(function() oc18 = BR.GetOpenConf(BR) end)\nif oc18 ~= nil then\nfo1:write([[oc:]] .. serMk(oc18, 0))\nfo1:write(string.char(10))\nend\nend\nlocal ps4 = {}\nfor k5, v5 in pairs(C) do\nif type(k5) == [[string]] and math.min(#ps4, 90) ~= 90 then\ntable.insert(ps4, k5)\nend\nend\nfo1:write([[bgk:]] .. table.concat(ps4, [[ ]]))\nfo1:write(string.char(10))\nlocal cnt6 = 0\nfor k6, v6 in pairs(_G) do\nif type(k6) == [[string]] and math.min(cnt6, 40) ~= 40 then\nlocal s6 = string.find(k6, [[nlock]], 1, true) ~= nil\nlocal s7 = string.find(k6, [[unction]], 1, true) ~= nil\nif s6 or s7 then\ncnt6 = cnt6 - (-1)\nfo1:write([[gu:]] .. k6 .. [[=]] .. type(v6))\nfo1:write(string.char(10))\nend\nend\nend\n-- 112s：功能屏蔽系统探查。行前整备入口锁疑走 FunctionShield\n-- 通用屏蔽（gu 实锤全局存在）。dump 其方法字节码（fs_ 前缀）\n-- 并序列化配置表内容（fsc:）。\nlocal fsc20 = rawget(_G, [[FunctionShieldConfig]])\nif type(fsc20) == [[table]] then\nfo1:write([[fsc:]] .. serMk(fsc20, 0))\nfo1:write(string.char(10))\nend\nlocal FS21 = rawget(_G, [[FunctionShield]])\nif type(FS21) == [[table]] then\nfor k22, v22 in pairs(FS21) do\nif type(v22) == [[function]] then\nlocal ok23, d23 = pcall(string.dump, v22)\nif ok23 and type(d23) == [[string]] then\nlocal fn24 = io.open([[D:/DSH/.tools/c112_fs_]] .. tostring(k22) .. [[.bin]], [[wb]])\nif fn24 then\nfn24:write(d23)\nfn24:close()\nend\nend\nend\nend\nend\n-- 112s：图录详情类定位。详情渲染在 Switcher.GetActiveWidget():Show，\n-- 83 个 dump 中无此类。搜索 ItemInfo/Detail 模块并 dump 方法（ii_ 前缀）。\nfor k26, v26 in pairs(package.loaded) do\nif type(k26) == [[string]] then\nlocal kl27 = string.lower(k26)\nlocal f28 = string.find(kl27, [[teminfo]], 1, true) ~= nil\nlocal f29 = string.find(kl27, [[etail]], 1, true) ~= nil\nif f28 or f29 then\nfo1:write([[ii found:]] .. k26)\nfo1:write(string.char(10))\nif type(v26) == [[table]] then\nfor k31, v31 in pairs(v26) do\nif type(v31) == [[function]] then\nlocal ok32, d32 = pcall(string.dump, v31)\nif ok32 and type(d32) == [[string]] then\nlocal fn33 = io.open([[D:/DSH/.tools/c112_ii_]] .. k26 .. [[_]] .. tostring(k31) .. [[.bin]], [[wb]])\nif fn33 then\nfn33:write(d32)\nfn33:close()\nend\nend\nend\nend\nend\nend\nend\nend\n-- 112s：全部 UMG/uw 模块名清单（umin:，700 字符分块，上限 20 块）。\nlocal nms34 = {}\nfor k35, v35 in pairs(package.loaded) do\nif type(k35) == [[string]] then\nlocal kl36 = string.lower(k35)\nlocal f37 = string.find(kl36, [[umg]], 1, true) ~= nil\nlocal f38 = string.find(kl36, [[uw_]], 1, true) ~= nil\nif f37 or f38 then\ntable.insert(nms34, k35)\nend\nend\nend\nlocal all39 = [[umin:]] .. table.concat(nms34, [[,]])\nfor seg40 = 0, 19 do\nlocal s41 = string.sub(all39, seg40 * 700 - (-1), (seg40 - (-1)) * 700)\nif s41 ~= [[]] then\nfo1:write(s41)\nfo1:write(string.char(10))\nend\nend\nfo1:close()\nend)\n-- 112u：解锁探针对象修正。112s 用 _G.BagRogue（运行时单例）调\n-- GetOpenConf/GetBagUnlockConfig 全落空——配置函数在 BagRogueConfig\n-- （bgk 键名实锤）。用正确对象重跑并 dump 判断函数本体；顺带 dump\n-- 行前整备（buffformation 系）与图录详情（uw_backpack_riki_talent）\n-- 类方法（bf_ 前缀），反汇编找入口置灰判断。\npcall(function()\nlocal BC = BagRogueConfig\nif type(BC) ~= [[table]] then return end\nlocal fo70 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif fo70 == nil then return end\nlocal function serMk2(tm, dm)\nif type(tm) ~= [[table]] then return tostring(tm) end\nif math.min(dm, 3) == 3 then return [[T]] end\nlocal ps87 = {}\nfor k88, v88 in pairs(tm) do\nif math.min(#ps87, 40) ~= 40 then\ntable.insert(ps87, tostring(k88) .. [[=]] .. serMk2(v88, dm - (-1)))\nend\nend\nreturn [[{]] .. table.concat(ps87, [[,]]) .. [[}]]\nend\nlocal bu71 = BC.tbBagUnlock\nif type(bu71) == [[table]] then\nfo70:write([[tbu2:]] .. serMk2(bu71, 0))\nfo70:write(string.char(10))\nend\nlocal bo72 = BC.tbBagUnlockOrder\nif type(bo72) == [[table]] then\nfo70:write([[tbo2:]] .. serMk2(bo72, 0))\nfo70:write(string.char(10))\nend\nfo70:write([[nmx2:]] .. tostring(BC.nMaxBagUnlock))\nfo70:write(string.char(10))\nlocal oc73 = nil\npcall(function() oc73 = BC.GetOpenConf(BC) end)\nif oc73 ~= nil then\nfo70:write([[oc2:]] .. serMk2(oc73, 0))\nfo70:write(string.char(10))\nend\nfor i74 = 0, 3 do\nlocal c75 = nil\npcall(function() c75 = BC.GetBagUnlockConfig(BC, i74) end)\nfo70:write([[guc2:]] .. tostring(i74) .. [[ ]] .. serMk2(c75, 0))\nfo70:write(string.char(10))\nend\n-- 判断函数本体 dump（反汇编看参数与分支）\nfor i76 = 1, 4 do\nlocal nm77 = nil\nif i76 == 1 then nm77 = [[GetBagUnlockConfig]] end\nif i76 == 2 then nm77 = [[CheckOpenConfig]] end\nif i76 == 3 then nm77 = [[GetOpenConf]] end\nif i76 == 4 then nm77 = [[LoadBagUnlockConfig]] end\nlocal fv78 = BC[nm77]\nif type(fv78) == [[function]] then\nlocal ok79, d79 = pcall(string.dump, fv78)\nif ok79 and type(d79) == [[string]] then\nlocal fn80 = io.open([[D:/DSH/.tools/c112_bf_]] .. nm77 .. [[.bin]], [[wb]])\nif fn80 then\nfn80:write(d79)\nfn80:close()\nend\nend\nend\nend\n-- buffformation 系与图录详情类方法 dump（登录时已加载，umin 实锤）\nlocal cl81 = {\n[[UMG.Rogue25.umg_rogue25_buffformation]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buffformation_common]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buffformation_role]],\n[[UMG.Rogue25.Widgets.uw_rogue25_buff_formation_check]],\n[[UMG.Rogue25.Widgets.uw_rogue25_formation_buff_item]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent]],\n[[UMG.Backpack.Widgets.uw_backpack_riki_talent_list]],\n}\nfor i82 = 1, #cl81 do\nlocal md83 = package.loaded[cl81[i82]]\nif type(md83) == [[table]] then\nfor k84, v84 in pairs(md83) do\nif type(k84) == [[string]] and type(v84) == [[function]] then\nlocal ok85, d85 = pcall(string.dump, v84)\nif ok85 and type(d85) == [[string]] then\nlocal fn86 = io.open([[D:/DSH/.tools/c112_bf_]] .. tostring(i82) .. [[_]] .. k84 .. [[.bin]], [[wb]])\nif fn86 then\nfn86:write(d85)\nfn86:close()\nend\nend\nend\nend\nelse\nfo70:write([[bf missing:]] .. cl81[i82])\nfo70:write(string.char(10))\nend\nend\nfo70:close()\nend)\n-- 115f：旧 GM 段（原 115d/115e 位置，在探针段之后）已上移到\n-- chunk 顶部并加固（单句柄复用、dump 拼接单文件、xpcall 错误\n-- 回写），见 payload 开头。原位置句柄耗尽导致 io.open 全 nil。\n-- 112t：功能屏蔽总开关关闭。CheckItem/CheckOpen/CheckTest 反汇编实锤\n-- bEnable=false 时全部直接放行（CheckOpen return true、CheckItem 不查\n-- GDPL 表）——行前整备等被屏蔽入口一并解锁。幂等守标与旧值日志。\npcall(function()\nlocal FS66 = rawget(_G, [[FunctionShield]])\nif type(FS66) == [[table]] and FS66.bMikuShieldOff ~= true then\nFS66.bMikuShieldOff = true\nlocal was67 = FS66.bEnable\nFS66.bEnable = false\nlocal lf68 = io.open([[D:/DSH/.tools/shop_patch_err.txt]], [[a]])\nif lf68 then\nlf68:write([[shield off, bEnable was:]] .. tostring(was67))\nlf68:write(string.char(10))\nlf68:close()\nend\nend\nend)",
			"BagRogueEndNode" => "local function mikuApply()\nlocal M = BagRogue\nif type(M) ~= [[table]] then return end\nif M.bMikuEndNodeFix ~= true then\nM.bMikuEndNodeFix = true\nlocal fnOld = M.IsEndNode\nif type(fnOld) == [[function]] then\nM.IsEndNode = function(self)\nlocal okOld, vOld = pcall(fnOld, self)\nif okOld and vOld then return true end\nlocal nMapId = M.GetCurLevelNode(self)\nlocal nNodeId = M.GetCurWalkNode(self)\nlocal tbMap = BagRogueConfig.tbMapNode[nMapId]\nif type(tbMap) ~= [[table]] then return false end\nlocal cfg = tbMap[nNodeId]\nif type(cfg) ~= [[table]] then return false end\nif cfg.nEventType ~= 5 then return false end\nfor _, other in pairs(tbMap) do\nlocal pres = other.tbPreNode\nif type(pres) == [[table]] then\nfor _, p in pairs(pres) do\nif tonumber(p) == nNodeId then return false end\nend\nend\nend\nreturn true\nend\nend\nend\nif M.bMikuSettleFix ~= true and type(s2c) == [[table]] and type(s2c.tbCmds) == [[table]] then\nlocal fnSettle = s2c.tbCmds.BagRogue_LevelSettlement\nif type(fnSettle) == [[function]] then\nM.bMikuSettleFix = true\ns2c.tbCmds.BagRogue_LevelSettlement = function(tbParam)\npcall(fnSettle, tbParam)\nlocal bEnd = false\nlocal nMap = M.GetCurLevelNode(M)\nif nMap == 0 or nMap == nil then bEnd = true end\nif not bEnd then\nlocal okEnd, vEnd = pcall(M.IsEndNode, M)\nif okEnd and vEnd then bEnd = true end\nend\nif bEnd then\nM.bMikuEnded = true\nM.bClickNode = false\nM.bMikuPick = nil\nelse\nlocal kPick = [[tbRelicChoose]]\nif type(tbParam) == [[table]] and type(tbParam[kPick]) == [[table]] and tbParam[kPick][1] ~= nil then\nM.bMikuPick = true\nend\nend\nend\nend\nend\nif M.bMikuPickHook ~= true and type(UI) == [[table]] then\nM.bMikuPickHook = true\nlocal fnCheck = function()\nif BagRogue.bMikuPick ~= true then return end\nlocal bSettled = true\nif type(UI.IsOpen) == [[function]] then\nlocal okIs, vIs = pcall(UI.IsOpen, [[backpacksettlement]])\nif okIs and vIs then bSettled = false end\nend\nif bSettled then\nBagRogue.bMikuPick = nil\npcall(UI.Open, [[backpackchoose]])\nend\nend\nfor _, fnName in ipairs({[[Close]], [[CloseByName]], [[CloseTop]], [[CloseAll]], [[PopUI]]}) do\nlocal fnRaw = UI[fnName]\nif type(fnRaw) == [[function]] then\nUI[fnName] = function(...)\nlocal rs = table.pack(pcall(fnRaw, ...))\npcall(fnCheck)\nif rs[1] then return table.unpack(rs, 2, rs.n) end\nreturn nil\nend\nend\nend\nend\nend\nmikuApply()\nif _G.bMikuOpenHooked ~= true and type(UI) == [[table]] and type(UI.Open) == [[function]] then\n_G.bMikuOpenHooked = true\nlocal fnOpen = UI.Open\nUI.Open = function(sName, ...)\nmikuApply()\nif type(sName) == [[string]] and type(BagRogue) == [[table]] and BagRogue.bMikuEnded == true and string.lower(sName) == [[backpackmap]] then\nBagRogue.bMikuEnded = nil\nreturn fnOpen([[BackpackMain]])\nend\nreturn fnOpen(sName, ...)\nend\nend",
			"BagRogueShopAffix" => "local sUI = [[BackpackShop]]\nlocal sLog = [[D:/DSH/.tools/shop_patch_err.txt]]\nlocal function logErr(s)\nlocal f = io.open(sLog, [[a]])\nif f then\nf:write(s)\nf:write(string.char(10))\nf:close()\nend\nend\nlocal function patchShopInst(p)\nif type(p) ~= [[table]] or p.bMikuInstPatch == true then return false end\nlocal orig = p.RefreshShop\nif type(orig) ~= [[function]] then return false end\np.bMikuInstPatch = true\np.RefreshShop = function(self, ...)\nlocal ok0, err0 = xpcall(function()\nlocal walk = BagRogue.GetCurWalkNode and BagRogue:GetCurWalkNode()\nlocal info = walk and BagRogue.GetNodeInfo and BagRogue:GetNodeInfo(walk)\nif type(info) ~= [[table]] or type(info.D) ~= [[table]] then\nerror([[no info]])\nend\nlocal sid = self.nStoreId or info.D[1] or 1\nlocal sc = BagRogueConfig.GetStoreConfig and BagRogueConfig:GetStoreConfig(sid)\npcall(function()\nif sc and sc.nCashId and Cash and Cash.GetMoneyCfgInfo and WidgetUtils and WidgetUtils.SetTexture and self.ImgMoneyIcon then\nlocal mc = Cash:GetMoneyCfgInfo(sc.nCashId)\nif mc and mc.nIcon then\nWidgetUtils.SetTexture(self.ImgMoneyIcon, mc.nIcon)\nend\nend\nend)\npcall(function()\nif sc and self.TxtMoney and WidgetUtils and WidgetUtils.SetText and sc.refreshCost and Cash and Cash.GetStoreCashNum then\nlocal rc = sc.refreshCost\nlocal have = Cash:GetStoreCashNum(sc.nCashId)\nWidgetUtils.SetText(self.TxtMoney, tostring(rc))\nif self.TxtMoney.SetColorFromHex then\nif math.max(have or 0, rc) == rc and have ~= rc then\nself.TxtMoney:SetColorFromHex([[#FF1010FF]])\nelse\nself.TxtMoney:SetColorFromHex([[#FFFFFFFF]])\nend\nend\nend\nend)\npcall(function()\nif self.DoClearListItems then\nself:DoClearListItems(self.ListBuff)\nend\nend)\nif type(self.ListBuff) ~= [[table]] and type(self.ListBuff) ~= [[userdata]] then return end\nfor i = 2, #info.D do\nlocal d = info.D[i]\nlocal cfg = BagRogueConfig.GetJigsawConfig and BagRogueConfig:GetJigsawConfig(d)\nif cfg then\npcall(function()\nlocal item = {}\nitem.tpCfg = cfg\nitem.nType = 3\nitem.idx = i\nitem.bSelled = false\npcall(function()\nlocal sv = info.S\nif type(sv) ~= [[number]] then sv = 0 end\nif GetBits and GetBits(sv, i, i) == 1 then\nitem.bSelled = true\nend\nend)\nlocal st = BagRogue.ShowItemType and BagRogue.ShowItemType.Shop\nif st then\nitem.ShowItemType = st\nend\nlocal key = BagRogue.GetAffixStrId and BagRogue:GetAffixStrId(d)\nlocal ja = nil\nif key and type(info.JA) == [[table]] then\nja = info.JA[key]\nend\nitem.tbAffix = type(ja) == [[table]] and ja or {}\nlocal w = self.Factory:Create(item)\nif w and self.ListBuff.AddItem then\nself.ListBuff:AddItem(w)\nend\nend)\nend\nend\nend, function(e) return debug.traceback(e) end)\nif not ok0 then\nlogErr([[shop rewrite fail ]] .. tostring(err0))\nreturn orig(self, ...)\nend\nreturn nil\nend\nreturn true\nend\nlocal function patchChooseText()\n-- 第 110 节：探索收获选卡卡片上的占位文本（截图实锤 Text Block）。\n-- 根因：原版 uw_backpack_choose_item2.Show 里 TxtNum:SetText(Text BackPackSoldOut)\n-- 把控件字段名当本地化键，键不存在 → 引擎显示 Text Block 占位，\n-- TxtNum 布局紧跟名字后面（视觉上挤掉名字尾巴）。\n-- 实测（probe5）：卡片实例字段是 advCfg；控件须经 GetSubWidget(name) 取；\n-- TxtPropsName=商品名/TxtStoryName=事件名/TxtStory=描述都正常，\n-- BackPackSoldOut 实际是底部按钮文字（选择）——不能动。\n-- 修复：wrap BackpackChoose 实例 SaveWidget（Show 之后必调），fixCard 清空 TxtNum；\n-- 装补丁时已存在的卡片立即修一遍（guard=bMikuChooseFix）。\nif type(UI) ~= [[table]] or type(UI.GetUI) ~= [[function]] then return end\nlocal okP, p = pcall(UI.GetUI, [[BackpackChoose]])\nif not okP or type(p) ~= [[table]] then return end\nif p.bMikuChooseFix == true then return end\np.bMikuChooseFix = true\nlocal function setSub(w, nm, val)\n-- 111c：取控件统一走 GetSubWidget 优先（卡片与面板控件都挂这上面），\n-- 失败再退回字段索引 w[nm]（兼容旧 setPanelText 的取法）。\nlocal sw = nil\nif type(w.GetSubWidget) == [[function]] then\nlocal okg, s2 = pcall(w.GetSubWidget, w, nm)\nif okg then sw = s2 end\nend\nif sw == nil then\nlocal okg2, s3 = pcall(function() return w[nm] end)\nif okg2 then sw = s3 end\nend\nif sw == nil then return end\nlocal oks, sfn = pcall(function() return sw.SetText end)\nif oks and sfn ~= nil then\npcall(sfn, sw, val)\nend\nend\nlocal function dumpKeys(t)\n-- 111c：诊断用，列一个配置表的全部字符串键（写日志找道具描述源）。\nlocal ks = {}\nif type(t) ~= [[table]] then return [[]] end\nfor k, _ in pairs(t) do\nif type(k) == [[string]] then table.insert(ks, k) end\nend\ntable.sort(ks)\nreturn table.concat(ks, [[,]])\nend\nlocal function dumpCfg(t)\n-- 111d：诊断用，列配置表的键值（string 值截 48 字符，其余只写类型）。\n-- 111f：数字键也列（tbOption 是数组，GETI tbOption[1] 实锤）。\nlocal parts = {}\nif type(t) ~= [[table]] then return tostring(t) end\nfor k, v in pairs(t) do\nif type(k) == [[string]] or type(k) == [[number]] then\nif type(v) == [[string]] then\ntable.insert(parts, tostring(k) .. [[=]] .. string.sub(v, 1, 48))\nelseif type(v) == [[number]] or type(v) == [[boolean]] then\n-- 111h：数字/布尔值直接打值（tbOption[1] 的 Id 之前只见类型不见值）。\ntable.insert(parts, tostring(k) .. [[=]] .. tostring(v))\nelse\ntable.insert(parts, tostring(k) .. [[:(]] .. type(v) .. [[)]])\nend\nend\nend\ntable.sort(parts)\nreturn table.concat(parts, [[ | ]])\nend\nlocal function setTitles(w)\n-- 111c：面板标题栏（顶部横幅）设为探索事件名。111 版用 setPanelText 走\n-- p[nm] 字段索引一直没生效（顶部空白实锤），111c 统一走 setSub（GetSubWidget 优先）。\n-- 111s：标题按卡种类区分（用户实锤）：奇器三选一=迷雾的酬谢，\n-- 秘宝三选一=探索收获，其余保持事件名。种类由 fixCard 写 w.bMikuKind。\nlocal ti = nil\nif w.bMikuKind == [[relic]] then\nti = [[迷雾的酬谢]]\nelseif w.bMikuKind == [[jig]] then\nti = [[探索收获]]\nelse\nlocal se = w.advCfg and w.advCfg.sEventName\nif type(se) ~= [[string]] then return end\nlocal oko, tv = pcall(Text, se)\nif oko and tv ~= nil then ti = tostring(tv) end\nend\nif ti == nil then return end\nsetSub(p, [[TxtTitle]], ti)\nsetSub(p, [[TxtStoryTitle]], ti)\nend\nlocal function fixCard(w)\nif type(w) ~= [[table]] or type(w.advCfg) ~= [[table]] then return end\nif type(w.GetSubWidget) ~= [[function]] then return end\nsetSub(w, [[TxtNum]], [[]])\n-- 第 111 节：探索收获卡对齐奇器卡版式（大字名字横幅、真实词条描述、标题栏事件名）。\n-- 卡片实例 w.idx = 拼图 Id（OnOpen Adventure 分支里 item.idx 与 GetAffixStrId 同源），\n-- 由此反推 jigsaw 配置（sName）与节点表 JA 词条（GetAffixConfig 的 sDes 与 tbDesVal）。\nlocal cfg = nil\nif type(w.idx) == [[number]] and type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetJigsawConfig) == [[function]] then\nlocal okc, c = pcall(BagRogueConfig.GetJigsawConfig, BagRogueConfig, w.idx)\nif okc and type(c) == [[table]] and type(c.sName) == [[string]] and c.nSubId ~= nil then\ncfg = c\nend\nend\nif cfg == nil then\n-- 第 111b 节：道具卡（冠冕/火石/炙热炉火，idx 非拼图 Id）走不到上面的分支，\n-- 原版渲染下 TxtPropsName 小字名字压在图标上。修法不依赖配置表：\n-- 读 TxtPropsName 现成文本挪到 TxtStoryName 大字横幅，再清掉小字；\n-- GetText 拿不到（非 string）就原样不动，无损退回现状。\nif w.bMikuItemFix == true then return end\nw.bMikuItemFix = true\nlocal okg, sw = pcall(w.GetSubWidget, w, [[TxtPropsName]])\nif okg and sw ~= nil then\nlocal okh, hfn = pcall(function() return sw.GetText end)\nif okh and hfn ~= nil then\nlocal okv, cur = pcall(hfn, sw)\nlocal lf = io.open(sLog, [[a]])\nif lf then\nlf:write([[choose item card idx=]] .. tostring(w.idx) ..\n[[ name=]] .. tostring(cur) .. [[ t=]] .. type(cur) ..\n[[ tpCfg=]] .. type(w.tpCfg) ..\n[[ tpKeys=]] .. dumpKeys(w.tpCfg) ..\n[[ tbAffix=]] .. type(w.tbAffix) ..\n[[ brc=]] .. dumpKeys(BagRogueConfig))\nlf:write(string.char(10))\nlf:close()\nend\nif okv and type(cur) == [[string]] and cur ~= [[]] then\nsetSub(w, [[TxtStoryName]], cur)\nsetSub(w, [[TxtPropsName]], [[]])\nend\nend\nend\n-- 111f：widget 实锤不持有 item 数据（wcard 只有 advCfg/idx/Object 等），\n-- 描述从 advCfg（探索事件配置）挖：dump 全字段；tbOption[idx] 子表\n-- （三选一每选项独立配置）候选描述字段命中即写 TxtStory。\n-- Cash 是模块闭包 upvalue 不在 _G（money 段 111e 没打出来的根因），\n-- globals 探查加 Cash 名单。\nlocal tried = {}\nlocal hitDesc = false\nlocal ac = w.advCfg\nif type(ac) == [[table]] then\ntable.insert(tried, [[acfg:]] .. dumpCfg(ac))\n-- 111g：名字键实锤是 RelicNameX（含羞草=RelicName1 等），三选项是秘宝体系；\n-- sOptionDesc 三卡同为 MikuDes10（事件级通用文案，翻译结果恰为模板默认值）。\n-- 专属描述走 tbOption[idx]（秘宝 Id 或选项子表）到 GetRelicConfig 取。\nlocal opt = ac.tbOption\n-- 111t：秘宝三选一卡（第 112 节，战斗格 BattleJigsawReward）。\n-- 112c：秘宝卡条目已改挂 OptionType 10（原版选卡 UI 只认 1..10，type 11\n-- 一张卡都不建，弹空白面板）。识别口径=type 10 且卡 Id 前缀 11\n-- （秘宝卡 11011..12275，原版奇器卡 9501..9530 前缀 9 不撞）。\n-- 拼图 Id 段 1011..1275 与奇器 Id 1001..1030 重叠，必须先于奇器路径按\n-- 拼图配置渲染：名字=GetJigsawConfig.sName、图标=SetTexture(nIcon)、\n-- 描述=基础属性、类别=GetJigsawConfig.nTalentType。\n-- 112i：识别口径修正。秘宝卡选项 Id=12011..12275（12000 与拼图 Id 相加），\n-- 千位前缀是 12——112c 写的 sub(nId,1,2)==[[11]] 永远不命中\n-- （探针实锤：卡面名字是 111b 兜底挪的，描述落到通用文案，111t 从未生效）。\n-- 改用 math.floor(nId/1000)==12（禁字符无法用比较运算，除法取整等价）。\n-- 奇器卡 9501..9530 → floor=9 不撞；原版其他 type 10 卡不在 12 段。\nif ac.nOptionType == 11\nor (ac.nOptionType == 10\nand math.floor((tonumber(ac.nId) or 0) / 1000) == 12) then\nw.bMikuKind = [[jig]]\nlocal oidJ = nil\nif type(opt) == [[table]] and type(opt[1]) == [[number]] then\noidJ = opt[1]\nend\nif oidJ ~= nil\nand type(BagRogueConfig.GetJigsawConfig) == [[function]] then\nlocal okJ2, jc = pcall(BagRogueConfig.GetJigsawConfig, BagRogueConfig, oidJ)\ntable.insert(tried, [[jigpick:]] .. tostring(oidJ) .. [[ ]] .. dumpCfg(jc))\nif okJ2 and type(jc) == [[table]] then\nlocal bannerJ = nil\nlocal okN2, nm2 = pcall(Text, jc.sName)\nif okN2 and nm2 ~= nil then bannerJ = tostring(nm2) end\n-- 112c：类别标签（nTalentType → GetTalentTypeConfig[1].sName），\n-- 拼进名字横幅，与奇器卡「含羞草 · 生机」版式对齐。\nif type(jc.nTalentType) == [[number]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nlocal okT3, tc3 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, jc.nTalentType)\nlocal t13 = nil\nif okT3 and type(tc3) == [[table]] then t13 = tc3[1] end\nif type(t13) == [[table]] and type(t13.sName) == [[string]] then\nlocal okX3, tx3 = pcall(Text, t13.sName)\nif okX3 and tx3 ~= nil and tostring(tx3) ~= [[]] then\nif bannerJ == nil then bannerJ = tostring(tx3)\nelse bannerJ = bannerJ .. [[ · ]] .. tostring(tx3) end\nend\nend\nend\nif type(bannerJ) == [[string]] and bannerJ ~= [[]] then\nsetSub(w, [[TxtStoryName]], bannerJ)\nsetSub(w, [[TxtPropsName]], [[]])\nend\nif type(jc.nIcon) == [[number]] then\nlocal okI2, iw2 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okI2 and iw2 ~= nil then\npcall(function() iw2:SetTexture(jc.nIcon) return true end)\nend\nend\n-- 112j：分段组装，模拟商店卡文本版式：\n-- 属性行 / 察刻经验 / 天赋类别行 / 增益效果 / [n] 词条。\n-- 选卡模板只有 TxtStory 一个文本块，用 string.char(10) 换行分节。\nlocal nl2 = string.char(10)\n-- 112m：属性名与数值的连接符（字符四十三），图录版式为「攻击43号字符203」。\nlocal plus2 = string.char(43)\n-- 112k：tbAttribute/tbTalentNum 客户端实为数组子表结构（tal 探针\n-- 实锤 tbTalentNum={1:(table),2:(table)}），兼容两种形态：number\n-- 映射（键=类别Id）或子表（候选键名取类别/数值），同时深一层\n-- dump 子表键值（tale:/atte: 探针），键名猜错时下轮日志可定位。\nlocal partsA = {}\nlocal partsJ = {}\nlocal at2 = jc.tbAttribute or jc.Attribute\nif type(at2) == [[table]] then\nlocal lab2 = {}\nlab2.AttackNum = [[攻击]]\nlab2.DefenceNum = [[防御]]\nlab2.HealthNum = [[最大生命]]\nlab2.nAttackNum = [[攻击]]\nlab2.nDefenceNum = [[防御]]\nlab2.nHealthNum = [[最大生命]]\nlab2.nAttack = [[攻击]]\nlab2.nDefence = [[防御]]\nlab2.nHealth = [[生命]]\nfor k2, v2 in pairs(at2) do\nif type(v2) == [[number]] and lab2[k2] then\ntable.insert(partsA, lab2[k2] .. [[ ]] .. tostring(v2))\nelseif type(v2) == [[table]] then\nlocal dv2 = {}\nfor k5, v5 in pairs(v2) do\ntable.insert(dv2, tostring(k5) .. [[=]] .. tostring(v5))\nend\ntable.insert(tried, [[atte:]] .. tostring(k2) .. [[ ]] .. table.concat(dv2, [[,]]))\n-- 112m：atte 探针实锤子表为数组形态 v2[1]=属性枚举名 v2[2]=数值\n-- （例 1=AttackNum,2=203）。枚举名经 lab2 映射中文，仅三种封闭集合。\nlocal vv2 = v2.nNum or v2.Num or v2.nValue or v2.nCount or v2.nAttributeVal\nlocal nm2v = nil\nif type(v2[1]) == [[string]] and lab2[v2[1]] ~= nil then\nnm2v = lab2[v2[1]]\nif type(v2[2]) == [[number]] then vv2 = v2[2] end\nelse\nlocal tp2 = v2.nType or v2.Type or v2.nAttributeType or v2.nAttributeNum\nif type(v2[1]) == [[number]] then tp2 = v2[1] end\nif type(v2[2]) == [[number]] then vv2 = v2[2] end\ntp2 = tp2 or tonumber(k2)\nif tp2 == 1 then nm2v = [[攻击]] end\nif tp2 == 2 then nm2v = [[防御]] end\nif tp2 == 3 then nm2v = [[最大生命]] end\nend\nif nm2v ~= nil and type(vv2) == [[number]] then\ntable.insert(partsA, nm2v .. plus2 .. tostring(vv2))\nend\nend\nend\nend\n-- 112j：察刻经验区——类别名走 GetTalentTypeConfig(类别Id)[1].sName\n-- （111j 奇器类别同源）。\nlocal tal2 = {}\nlocal ttn2 = jc.tbTalentNum\nif type(ttn2) == [[table]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nfor k4, v4 in pairs(ttn2) do\nlocal tid4 = nil\nlocal val4 = nil\nif type(v4) == [[number]] then\ntid4 = tonumber(k4)\nval4 = v4\nelseif type(v4) == [[table]] then\nlocal dv4 = {}\nfor k5, v5 in pairs(v4) do\ntable.insert(dv4, tostring(k5) .. [[=]] .. tostring(v5))\nend\ntable.insert(tried, [[tale:]] .. tostring(k4) .. [[ ]] .. table.concat(dv4, [[,]]))\n-- 112m：tale 探针实锤子表为数组形态 v4[1]=篆痕类别Id v4[2]=累计层数\n-- （例 1=11,2=3）。类别名走 GetTalentTypeConfig（111c 横幅同源已实锤）。\ntid4 = v4.nTalentType or v4.TalentType or v4.nId or v4.Id\nval4 = v4.nNum or v4.Num or v4.nTalentNum or v4.TalentNum or v4.nValue or v4.nCount\nif type(v4[1]) == [[number]] then tid4 = v4[1] end\nif type(v4[2]) == [[number]] then val4 = v4[2] end\nend\nif tid4 ~= nil and val4 ~= nil then\nlocal nm4 = tostring(tid4)\nlocal ok4, tc4 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, tid4)\nif ok4 and type(tc4) == [[table]] and type(tc4[1]) == [[table]]\nand type(tc4[1].sName) == [[string]] then\nlocal okn4, tn4 = pcall(Text, tc4[1].sName)\nif okn4 and tn4 ~= nil and tostring(tn4) ~= [[]] then\nnm4 = tostring(tn4)\nend\nend\ntable.insert(tal2, nm4 .. [[ ]] .. tostring(val4))\nend\nend\nend\n-- 112i：节点 JA 词条渲染（与商店货架同源同键）。卡面 w.idx 是选项序号\n-- （探针实锤 idx=3），拼图 Id 在 tbOption[1]；JA 键=GetAffixStrId(拼图Id)。\n-- 词条行 [n] xxx 追加在属性后，与商店/图录版式对齐；选定后服务端发放\n-- 同一批 JA 词条进背包，所见即所得。\nlocal jaJ = nil\nif type(BagRogue) == [[table]] and type(BagRogue.GetCurWalkNode) == [[function]]\nand type(BagRogue.GetNodeInfo) == [[function]]\nand type(BagRogue.GetAffixStrId) == [[function]]\nand type(oidJ) == [[number]] then\nlocal okw2, walk2 = pcall(BagRogue.GetCurWalkNode, BagRogue)\nlocal info2 = nil\nif okw2 and walk2 ~= nil then\nlocal oki2, inf2 = pcall(BagRogue.GetNodeInfo, BagRogue, walk2)\nif oki2 then info2 = inf2 end\nend\nif type(info2) == [[table]] and type(info2.JA) == [[table]] then\nlocal okk2, key2b = pcall(BagRogue.GetAffixStrId, BagRogue, oidJ)\nif okk2 and type(key2b) == [[string]] then jaJ = info2.JA[key2b] end\nend\nend\ntable.insert(tried, [[jaJ:]] .. type(jaJ) .. [[/]] .. (jaJ and #jaJ or 0))\nif type(jaJ) == [[table]] then\nfor n2 = 1, #jaJ do\nif type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal oka2, ac3 = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, jaJ[n2])\nif oka2 and type(ac3) == [[table]] and type(ac3.sDes) == [[string]] then\nlocal okt2, tx2 = pcall(Text, ac3.sDes, table.unpack(ac3.tbDesVal or {}))\nif okt2 and tx2 ~= nil then\ntable.insert(partsJ, string.format([[[%d] ]], n2) .. tostring(tx2))\nend\nend\nend\nend\nend\n-- 112j：商店卡同款文本版式——属性 / 篆痕行 / 增益效果 / 词条。\n-- 112m：tbTalentNum 实锤为篆痕类别与层数（talent.json TalentType），\n-- 标题由「察刻经验」改为「篆痕」（图录 tab 名 Other_ManualTab1 同词）。\nlocal ekao2 = nil\nlocal zeng2 = nil\nif type(utf8) == [[table]] and type(utf8.char) == [[function]] then\nekao2 = utf8.char(0x7BC6, 0x75D5)\nzeng2 = utf8.char(0x589E, 0x76CA, 0x6548, 0x679C)\nend\nlocal out2 = {}\nfor _, s7 in ipairs(partsA) do\ntable.insert(out2, s7)\nend\nif #tal2 ~= 0 and ekao2 ~= nil then\ntable.insert(out2, ekao2)\nfor _, s7 in ipairs(tal2) do\ntable.insert(out2, s7)\nend\nend\nif #partsJ ~= 0 and zeng2 ~= nil then\ntable.insert(out2, zeng2)\nfor _, s7 in ipairs(partsJ) do\ntable.insert(out2, s7)\nend\nend\nif #out2 ~= 0 then\nsetSub(w, [[TxtStory]], table.concat(out2, nl2))\nhitDesc = true\nend\n-- 112m：dump 商店货架 item widget（全局 rogue25 系列）的原版显示函数\n-- 字节码，供属性行与篆痕行版式精确比对。函数名字拼接不含禁字符。\npcall(function()\nfor gk, gv in pairs(_G) do\nif type(gk) == [[string]] and string.find(gk, [[rogue25]], 1, true) ~= nil\nand type(gv) == [[table]] then\nfor mk, mv in pairs(gv) do\nif type(mv) == [[function]] then\nlocal okd2, d2 = pcall(string.dump, mv)\nif okd2 and type(d2) == [[string]] and math.min(#d2, 201) == 201 then\nlocal f9 = io.open([[D:/DSH/.tools/rg25dump_]] .. gk .. [[_]] .. tostring(mk) .. [[.bin]], [[wb]])\nif f9 ~= nil then\nf9:write(d2)\nf9:close()\nend\nend\nend\nend\nend\nend\nend)\nend\nend\nelseif type(opt) == [[table]] and type(w.idx) == [[number]] then\ntable.insert(tried, [[topt:]] .. dumpCfg(opt))\n-- 111h：tbOption 实锤只有 1 个元素（topt:1:(number)），原版名字分支\n-- GETI C=1 取 tbOption[1]——Id 在 [1]，不在 [w.idx]。\nlocal oid = nil\nif type(opt[1]) == [[number]] then\noid = opt[1]\nelseif type(opt[w.idx]) == [[number]] then\noid = opt[w.idx]\nend\nif oid ~= nil\nand type(BagRogueConfig.GetRelicConfig) == [[function]] then\nlocal okR, rc = pcall(BagRogueConfig.GetRelicConfig, BagRogueConfig, oid)\ntable.insert(tried, [[relic]] .. tostring(oid) .. [[:]] .. dumpCfg(rc))\nif type(rc) == [[table]] then\n-- 111s：奇器卡种类标记（标题=迷雾的酬谢）与显式 SetTexture(rc.nIcon)\n-- 覆盖原版图标源（首卡炙热炉火图标显示错误，配置 Id 1001444 与\n-- advCfg.nOptionIcon 一致但画面是别的东西——原版 type 10 分支可能\n-- 从别的表取了图标；111p 实证 ImgIcon:SetTexture 可用）。\nw.bMikuKind = [[relic]]\nif type(rc.nIcon) == [[number]] then\nlocal okI9, iw9 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okI9 and iw9 ~= nil then\nlocal okS9 = pcall(function() iw9:SetTexture(rc.nIcon) return true end)\nif okS9 ~= true then\nokS9 = pcall(function() return _G.SetTexture(iw9, rc.nIcon) end)\nend\ntable.insert(tried, [[seticon:]] .. tostring(rc.nIcon) ..\n[[/]] .. tostring(okS9))\nend\nend\nlocal names = {[[sDes]], [[sDesc]], [[sIntroduce]], [[sText]],\n[[sStory]], [[sTip]], [[sOptionDesc]]}\nfor _, nn in ipairs(names) do\nif type(rc[nn]) == [[string]] and rc[nn] ~= [[]] then\nlocal okT, tv = pcall(Text, rc[nn], table.unpack(rc.tbDesVal or {}))\nif okT and tv ~= nil and tostring(tv) ~= [[]] then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:relic.]] .. nn ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nbreak\nend\nend\nend\n-- 111i：relic 配置实锤没有描述字段（只有 sName/nAffix/nIcon 等），\n-- 专属效果描述在词条表：rc.nAffix（实锤 620）→ GetAffixConfig 的\n-- sDes 与 tbDesVal（111 秘宝卡同源）。\nif hitDesc ~= true and type(rc.nAffix) == [[number]]\nand type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal okA, ac2 = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, rc.nAffix)\ntable.insert(tried, [[affix:]] .. dumpCfg(ac2))\nif okA and type(ac2) == [[table]] and type(ac2.sDes) == [[string]]\nand ac2.sDes ~= [[]] then\nlocal okT, tv = pcall(Text, ac2.sDes, table.unpack(ac2.tbDesVal or {}))\nif okT and tv ~= nil and tostring(tv) ~= [[]] then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:affix.]] .. tostring(rc.nAffix) ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nend\nend\nend\nend\nelseif type(oid) == [[table]] then\ntable.insert(tried, [[opt:]] .. dumpCfg(oid))\nlocal names = {[[sDes]], [[sDesc]], [[sOptionDesc]], [[sText]],\n[[sStory]], [[sIntroduce]], [[sTip]]}\nfor _, nn in ipairs(names) do\nif type(oid[nn]) == [[string]] and oid[nn] ~= [[]] then\nlocal okT, tv = pcall(Text, oid[nn], table.unpack(oid.tbDesVal or {}))\nif okT and tv ~= nil then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:opt.]] .. nn ..\n[[ val:]] .. string.sub(tostring(tv), 1, 48))\nhitDesc = true\nbreak\nend\nend\nend\nend\nend\nif hitDesc ~= true and type(ac.sOptionDesc) == [[string]]\nand ac.sOptionDesc ~= [[]] then\nlocal okT, tv = pcall(Text, ac.sOptionDesc, table.unpack(ac.tbOption or {}))\nif okT and tv ~= nil then\nsetSub(w, [[TxtStory]], tostring(tv))\ntable.insert(tried, [[hit:acfg.sOptionDesc]])\nhitDesc = true\nend\nend\nend\nif type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetCurTimeConfig) == [[function]]\nand type(Cash) == [[table]] and type(Cash.GetMoneyCfgInfo) == [[function]] then\nlocal okC, tc = pcall(BagRogueConfig.GetCurTimeConfig, BagRogueConfig)\nif okC and type(tc) == [[table]] and tc.nKeyItem ~= nil then\nlocal okM, mc = pcall(Cash.GetMoneyCfgInfo, Cash, tc.nKeyItem)\ntable.insert(tried, [[money:]] .. dumpCfg(mc))\nend\nelse\ntable.insert(tried, [[cashnil:]] .. type(Cash))\nend\ntable.insert(tried, [[wcard:]] .. dumpCfg(w))\nlocal globals = {}\nfor k, v in pairs(_G) do\nif type(k) == [[string]] and type(v) == [[table]] then\nif string.find(k, [[Item]]) ~= nil or string.find(k, [[Prop]]) ~= nil\nor string.find(k, [[Cash]]) ~= nil then\ntable.insert(globals, k)\nend\nend\nend\ntable.sort(globals)\n-- 111k：白底板探查。选卡 nOptionType=10 不匹配 Show 的任何图标分支，\n-- 金色图标另有机制；白色底板是模板静态控件。dump ImgIcon 自身字段，\n-- 按候选名探测底板控件（下一轮按探测结果隐藏/换图）。\nlocal okG5, si = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5 and si ~= nil then\ntable.insert(tried, [[icon:]] .. dumpCfg(si))\nend\ndo\nlocal bgnames = {[[ImgIconBg]], [[ImgBg]], [[ImgDi]], [[ImgKuang]],\n[[ImgQuality]], [[PanelIcon]], [[ImgBase]], [[ImgItemBg]],\n[[ImgIconBg1]], [[ImgIconBg2]], [[ImgBox]], [[ImgWhite]], [[ImgIconDi]]}\nfor _, bn in ipairs(bgnames) do\nlocal okg6, s6 = pcall(w.GetSubWidget, w, bn)\nif okg6 and s6 ~= nil then\ntable.insert(tried, [[bgfound:]] .. bn .. [[:]] .. dumpCfg(s6))\n-- 111m：反汇编实锤原版隐藏控件用 WidgetUtils.Collapsed(w)。\nif bn == [[ImgBg]] then\nlocal okw7, wu7 = pcall(function() return WidgetUtils end)\nlocal okc7 = false\nif okw7 and type(wu7) == [[table]]\nand type(wu7.Collapsed) == [[function]] then\nokc7 = pcall(wu7.Collapsed, s6)\nend\ntable.insert(tried, [[bghide:]] .. tostring(okc7))\nend\nend\nend\nend\n-- 111n：白块不是 ImgBg（collapsed:true 但画面无变化）。父链探查与\n-- 候选名 v2，找到即 Collapsed（bg 名单隐藏无损），日志记父名。\ndo\nlocal function parentName(sw)\nlocal okp, pf = pcall(function() return sw.GetParent end)\nif okp and pf ~= nil then\nlocal okp2, pw = pcall(pf, sw)\nif okp2 and pw ~= nil then\nlocal okn2, nf = pcall(function() return pw.GetName end)\nif okn2 and nf ~= nil then\nlocal okn3, nm2 = pcall(nf, pw)\nreturn tostring(nm2)\nend\nend\nend\nreturn [[-]]\nend\nlocal okG5b, si5 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5b and si5 ~= nil then\ntable.insert(tried, [[iparent:]] .. parentName(si5))\nend\nlocal bgn2 = {[[ImageBg]], [[ImgBG]], [[Bg]], [[BgIcon]], [[IconBg]],\n[[ImgIconBack]], [[ImgBack]], [[ImgDiBan]], [[ImgSlot]], [[ImgGrid]],\n[[ImgCell]], [[ImgSquare]], [[ImgRect]], [[ImgPaper]], [[ImgCard]],\n[[WhiteBg]], [[ImgWhiteBg]], [[BgWhite]], [[ImgBaseBg]],\n[[ImgZheZhao]], [[ImgLight]], [[ImgGlow]], [[ImgUnder]]}\nfor _, bn2 in ipairs(bgn2) do\nlocal okg8, s8 = pcall(w.GetSubWidget, w, bn2)\nif okg8 and s8 ~= nil then\nlocal hid = false\nlocal okw8, wu8 = pcall(function() return WidgetUtils end)\nif okw8 and type(wu8) == [[table]]\nand type(wu8.Collapsed) == [[function]] then\nhid = pcall(wu8.Collapsed, s8)\nend\ntable.insert(tried, [[bg2:]] .. bn2 .. [[/]] .. tostring(hid) ..\n[[/p=]] .. parentName(s8))\nend\nend\nend\n-- 111o：iparent 实锤 ImgIcon 的父容器是 PanelStory，白块是其兄弟\n-- 控件。枚举 PanelStory 子控件（GetChildrenCount/GetChildAt），\n-- 名字含 Img/Image 且非 ImgIcon 的 Collapsed，全部落日志。\ndo\nlocal okG5c, si6 = pcall(w.GetSubWidget, w, [[ImgIcon]])\nif okG5c and si6 ~= nil then\nlocal okp9, pf9 = pcall(function() return si6.GetParent end)\nif okp9 and pf9 ~= nil then\nlocal okp10, pw9 = pcall(pf9, si6)\nif okp10 and pw9 ~= nil then\nlocal okcc, cf9 = pcall(function() return pw9.GetChildrenCount end)\nlocal okca, caf9 = pcall(function() return pw9.GetChildAt end)\nif okcc and cf9 ~= nil and okca and caf9 ~= nil then\nlocal okn4, cnt = pcall(cf9, pw9)\ncnt = tonumber(cnt) or 0\ntable.insert(tried, [[pstorycnt:]] .. tostring(cnt))\nfor i = 0, cnt - 1 do\nlocal okch, chw = pcall(caf9, pw9, i)\nif okch and chw ~= nil then\nlocal nmx = [[-]]\nlocal oknf2, nf2 = pcall(function() return chw.GetName end)\nif oknf2 and nf2 ~= nil then\nlocal okn5, nm3 = pcall(nf2, chw)\nnmx = tostring(nm3)\nend\ntable.insert(tried, [[child]] .. tostring(i) .. [[:]] .. nmx)\nif nmx ~= [[ImgIcon]]\nand (string.find(nmx, [[Img]]) ~= nil\nor string.find(nmx, [[Image]]) ~= nil) then\nlocal okh9, wu9 = pcall(function() return WidgetUtils end)\nlocal hid9 = false\nif okh9 and type(wu9) == [[table]]\nand type(wu9.Collapsed) == [[function]] then\nhid9 = pcall(wu9.Collapsed, chw)\nend\ntable.insert(tried, [[hid]] .. tostring(i) .. [[:]] .. tostring(hid9))\nend\nend\nend\nend\nend\nend\nend\nend\n-- 111q：白块根因修正。type 10 不走 Show 的任何面板折叠分支，而奇器卡\n-- （type 2）会 Collapsed PanelPath 与 PanelRandom——白块在这两个面板里\n-- （bg2 探到的 Bg 父容器正是 PanelPath）。补齐同款折叠，PanelStory\n-- （描述区）保持可见。\ndo\nlocal okwq, wuq = pcall(function() return WidgetUtils end)\nif okwq and type(wuq) == [[table]]\nand type(wuq.Collapsed) == [[function]] then\nlocal pns = {[[PanelPath]], [[PanelRandom]]}\nfor _, pn in ipairs(pns) do\nlocal okpq, pwq = pcall(w.GetSubWidget, w, pn)\nif okpq and pwq ~= nil then\nlocal okcq = pcall(wuq.Collapsed, pwq)\ntable.insert(tried, [[panel:]] .. pn .. [[/]] .. tostring(okcq))\nend\nend\nend\nend\nlocal lfd = io.open(sLog, [[a]])\nif lfd then\nlfd:write([[choose desc idx=]] .. tostring(w.idx) .. [[ ]] ..\ntable.concat(tried, [[ ]]) .. [[ globals=]] .. table.concat(globals, [[,]]))\nlfd:write(string.char(10))\nlfd:close()\nend\n-- 111j：类别标签。图录奇器实锤有类别（飓风→风雪），数据链\n-- tbOption[1] → GetRelicConfig.nTalentType → GetTalentTypeConfig。\n-- 卡模板没有独立标签控件（Show 反汇编全控件清单无），拼进名字横幅：\n-- 「含羞草 · 生机」。string.find 纯文本比较防重复拼。\n-- 112c：拼图卡（bMikuKind=jig）类别已在 111t 分支按 GetJigsawConfig 处理，\n-- 且拼图 Id 1011..1030 与奇器 Id 撞表（GetRelicConfig 会命中错的奇器），这里跳过。\nif w.bMikuKind ~= [[jig]]\nand type(ac) == [[table]] and type(BagRogueConfig) == [[table]]\nand type(BagRogueConfig.GetRelicConfig) == [[function]]\nand type(BagRogueConfig.GetTalentTypeConfig) == [[function]] then\nlocal o2 = ac.tbOption\nif type(o2) == [[table]] and type(o2[1]) == [[number]] then\nlocal okR2, rc2 = pcall(BagRogueConfig.GetRelicConfig, BagRogueConfig, o2[1])\nif okR2 and type(rc2) == [[table]] and type(rc2.nTalentType) == [[number]] then\nlocal okT2, tc2 = pcall(BagRogueConfig.GetTalentTypeConfig,\nBagRogueConfig, rc2.nTalentType)\nif okT2 and type(tc2) == [[table]] then\n-- 111l：类别配置全字段落日志，确认 sName/sTypeName 取名是否命中。\nlocal lft = io.open(sLog, [[a]])\nif lft then\n-- 111m：GetTalentTypeConfig 返回 {1..5:子表} 索引表，dump 子表找类别名键。\nlocal sub1 = tc2[1]\nlocal sub2 = tc2[2]\nlft:write([[choose talent idx=]] .. tostring(w.idx) .. [[ ]] ..\ndumpCfg(tc2) .. [[ SUB1:]] .. dumpCfg(sub1) ..\n[[ SUB2:]] .. dumpCfg(sub2))\nlft:write(string.char(10))\nlft:close()\nend\nlocal tn = nil\n-- 111n：GetTalentTypeConfig 实锤返回 {1..5:等级子表}，类别名在子表\n-- sName（日志 SUB1 实锤 sName=bagrogue.TalentName10）。\nlocal t1c = tc2[1]\nif type(t1c) == [[table]] then\ntn = t1c.sName\nif type(tn) ~= [[string]] then tn = t1c.sTypeName end\nend\nif type(tn) ~= [[string]] then tn = tc2.sName end\nif type(tn) ~= [[string]] then tn = tc2.sTypeName end\nif type(tn) == [[string]] and tn ~= [[]] then\nlocal okN, tvn = pcall(Text, tn)\nif okN and tvn ~= nil and tostring(tvn) ~= [[]] then\nlocal okG3, sw3 = pcall(w.GetSubWidget, w, [[TxtStoryName]])\nif okG3 and sw3 ~= nil then\nlocal okH3, h3 = pcall(function() return sw3.GetText end)\nif okH3 and h3 ~= nil then\nlocal okV3, cur3 = pcall(h3, sw3)\nif okV3 and type(cur3) == [[string]] and cur3 ~= [[]]\nand string.find(cur3, tostring(tvn), 1, true) == nil then\nsetSub(w, [[TxtStoryName]], cur3 .. [[ · ]] .. tostring(tvn))\nend\nend\nend\nend\nend\nend\nend\nend\nend\nsetTitles(w)\nreturn\nend\nlocal okn, nm = pcall(Text, cfg.sName)\n-- 111s：秘宝卡种类标记（标题=探索）。\nw.bMikuKind = [[jig]]\nif okn and nm ~= nil then\nsetSub(w, [[TxtStoryName]], tostring(nm))\nend\nsetSub(w, [[TxtPropsName]], [[]])\nlocal lines = {}\nlocal ja = nil\nif type(BagRogue) == [[table]] and type(BagRogue.GetCurWalkNode) == [[function]]\nand type(BagRogue.GetNodeInfo) == [[function]]\nand type(BagRogue.GetAffixStrId) == [[function]] then\nlocal okw, walk = pcall(BagRogue.GetCurWalkNode, BagRogue)\nlocal info = nil\nif okw and walk ~= nil then\nlocal oki, inf = pcall(BagRogue.GetNodeInfo, BagRogue, walk)\nif oki then info = inf end\nend\nif type(info) == [[table]] and type(info.JA) == [[table]] then\nlocal okk, key = pcall(BagRogue.GetAffixStrId, BagRogue, w.idx)\nif okk and type(key) == [[string]] then ja = info.JA[key] end\nend\nend\nif type(ja) == [[table]] then\nfor n = 1, #ja do\nif type(BagRogueConfig.GetAffixConfig) == [[function]] then\nlocal oka, ac = pcall(BagRogueConfig.GetAffixConfig, BagRogueConfig, ja[n])\nif oka and type(ac) == [[table]] and type(ac.sDes) == [[string]] then\nlocal okt, tx = pcall(Text, ac.sDes, table.unpack(ac.tbDesVal or {}))\nif okt and tx ~= nil then\ntable.insert(lines, string.format([[[%d] ]], n) .. tostring(tx))\nend\nend\nend\nend\nend\nif #lines ~= 0 then\nsetSub(w, [[TxtStory]], table.concat(lines, string.char(10)))\nend\nsetTitles(w)\nend\nif type(p.SaveWidget) == [[function]] then\nlocal origSave = p.SaveWidget\np.SaveWidget = function(s2, key, w, ...)\nlocal r = origSave(s2, key, w, ...)\nfixCard(w)\nreturn r\nend\nend\nlocal function scanCards(t)\n-- 三层固定展开（面板 tbWidget[3][i] 结构），不用加法（禁字符）。\nif type(t) ~= [[table]] then return end\nfor _, v in pairs(t) do\nif type(v) == [[table]] then\nif type(v.advCfg) == [[table]] then\nfixCard(v)\nelse\nfor _, v2 in pairs(v) do\nif type(v2) == [[table]] then\nif type(v2.advCfg) == [[table]] then\nfixCard(v2)\nelse\nfor _, v3 in pairs(v2) do\nif type(v3) == [[table]] and type(v3.advCfg) == [[table]] then\nfixCard(v3)\nend\nend\nend\nend\nend\nend\nend\nend\nend\nscanCards(p)\n-- 112c：选卡链路函数字节码 dump 与 ShowAdvChoose 参数探针。\n-- GM /lua 通道失效（NtfScript 客户端不执行），改由补丁在客户端落盘：\n-- BackpackChoose 实例/元表、BackpackMap 实例/元表、uw_backpack_choose_item2\n-- 模块的全部函数 dump 到 D:/DSH/.tools/c112_*.bin，供本地 luac54 反汇编；\n-- 并 wrap 地图面板 ShowAdvChoose 记录 D 全量（点节点时触发）。\n-- 每客户端会话只做一次（guard=BagRogue.bMikuChooseDump）。\ndo\nlocal brD = BagRogue\nif type(brD) == [[table]] and brD.bMikuChooseDump ~= true then\nbrD.bMikuChooseDump = true\nlocal function dumpFn(fn, nm)\nif type(fn) ~= [[function]] then return end\nlocal okd, chunk = pcall(string.dump, fn)\nif okd and type(chunk) == [[string]] then\nlocal f = io.open([[D:/DSH/.tools/c112_]] .. nm .. [[.bin]], [[wb]])\nif f then f:write(chunk) f:close() end\nend\nend\ndumpFn(p.Show, [[p_Show]])\ndumpFn(p.OnOpen, [[p_OnOpen]])\nlocal mt = getmetatable(p)\nif type(mt) == [[table]] then\nlocal ix = mt.__index\nif type(ix) == [[table]] then\nfor k5, v5 in pairs(ix) do\nif type(k5) == [[string]] and type(v5) == [[function]] then\ndumpFn(v5, [[cp_]] .. k5)\nend\nend\nelseif type(ix) == [[function]] then\ndumpFn(ix, [[mt_index_fn]])\nend\nend\nlocal okM, mp = pcall(UI.GetUI, [[BackpackMap]])\nif okM and type(mp) == [[table]] then\ndumpFn(mp.ShowAdvChoose, [[map_ShowAdvChoose]])\nlocal mmt = getmetatable(mp)\nif type(mmt) == [[table]] and type(mmt.__index) == [[table]] then\nfor k6, v6 in pairs(mmt.__index) do\nif type(k6) == [[string]] and type(v6) == [[function]] then\ndumpFn(v6, [[cm_]] .. k6)\nend\nend\nend\nif type(mp.ShowAdvChoose) == [[function]] then\nlocal oldSAC = mp.ShowAdvChoose\nmp.ShowAdvChoose = function(s3, d3, ja3, ...)\nlocal lfp = io.open(sLog, [[a]])\nif lfp then\nlfp:write([[sac dType=]] .. type(d3))\nif type(d3) == [[table]] then\nlfp:write([[ dLen=]] .. tostring(#d3))\nfor i5 = 1, 3 do\nlocal e5 = d3[i5]\nif type(e5) == [[table]] then\nlfp:write([[ e]] .. i5 .. [[ v1=]] .. tostring(e5[1])\n.. [[ v2=]] .. tostring(e5[2]))\nend\nend\nend\nlfp:write([[ ja=]] .. type(ja3))\nlfp:write(string.char(10))\nlfp:close()\nend\nreturn oldSAC(s3, d3, ja3, ...)\nend\nend\nend\n-- 112d：Adventure 分支守卫探针。反汇编实锤 OnOpen 建卡前有两道守卫\n-- （查不到配置就静默跳过，0 卡 0 报错），直接探配置查找函数对\n-- 奇器卡 9515 与秘宝卡 12033 的返回，并 wrap OnOpen/GetWidget/\n-- SaveWidget 记录实参与建卡调用链。\nlocal cfgT = BagRogueConfig\nif type(cfgT) == [[table]] then\nlocal ids = {1033, 1034, 1035, 1011, 1275, 12033}\nfor i9 = 1, 6 do\nlocal pid = ids[i9]\nlocal okA, aA = pcall(cfgT.GetAdventureConfig, cfgT, pid)\nif okA and type(aA) == [[table]] then\nlogErr([[gac:]] .. tostring(pid) .. [[ nId=]] .. tostring(aA.nId)\n.. [[ name=]] .. tostring(aA.sOptionName) .. [[ icon=]] .. tostring(aA.nOptionIcon))\nelse\nlogErr([[gac:]] .. tostring(pid) .. [[ nil]])\nend\nlocal okJ, aJ = pcall(cfgT.GetJigsawConfig, cfgT, pid)\nif okJ and type(aJ) == [[table]] then\nlogErr([[gjc:]] .. tostring(pid) .. [[ nId=]] .. tostring(aJ.nId)\n.. [[ sub=]] .. tostring(aJ.nSubId) .. [[ name=]] .. tostring(aJ.sName)\n.. [[ icon=]] .. tostring(aJ.nIcon))\nelse\nlogErr([[gjc:]] .. tostring(pid) .. [[ nil]])\nend\nend\nlocal tj = cfgT.tbJigsaw\nif type(tj) == [[table]] then\nlocal allk = {}\nfor k8, v8 in pairs(tj) do\nlocal s8 = tostring(k8) .. [[=]] .. tostring(type(v8))\nif type(v8) == [[table]] then\ns8 = s8 .. [[/nId=]] .. tostring(v8.nId) .. [[/sub=]] .. tostring(v8.nSubId)\nend\ntable.insert(allk, s8)\nend\nlocal head = {}\nfor i9 = 1, 10 do\nhead[i9] = allk[i9] or [[]]\nend\nlogErr([[tbJigsaw cnt=]] .. tostring(#allk) .. [[ ]] .. table.concat(head, [[ ]]))\nelse\nlogErr([[tbJigsaw nil]])\nend\nend\nif type(p) == [[table]] and type(p.OnOpen) == [[function]] then\nlocal ooO = p.OnOpen\np.OnOpen = function(pp, a1, a2, a3, a4)\nlogErr([[oo:1=]] .. dumpCfg(a1) .. [[ |2=]] .. dumpCfg(a2)\n.. [[ |3=]] .. dumpCfg(a3) .. [[ |4=]] .. dumpCfg(a4))\nreturn ooO(pp, a1, a2, a3, a4)\nend\nif type(p.GetWidget) == [[function]] then\nlocal gwO = p.GetWidget\np.GetWidget = function(pp, t9)\nlogErr([[gw:]] .. dumpCfg(t9))\nreturn gwO(pp, t9)\nend\nend\nif type(p.SaveWidget) == [[function]] then\nlocal swO = p.SaveWidget\np.SaveWidget = function(pp, t9, w9)\nlogErr([[sw:]] .. dumpCfg(t9) .. [[ w=]] .. tostring(w9))\nreturn swO(pp, t9, w9)\nend\nend\nend\nlocal key2 = [[UMG.Backpack.Widgets.uw_backpack_choose_item2]]\nlocal mod2 = package.loaded[key2]\nif type(mod2) == [[table]] then\nfor k7, v7 in pairs(mod2) do\nif type(k7) == [[string]] and type(v7) == [[function]] then\ndumpFn(v7, [[ci_]] .. k7)\nend\nend\nend\n-- 112h：图录面板（BackpackRiki）探针。图录里秘宝「增益效果」全显示问号——\n-- dump 图录模块与实例函数拿字节码；wrap UI.OpenInner 在图录打开时抓实例。\nlocal key3 = [[UMG.Backpack.Widgets.uw_backpack_riki]]\nlocal mod3 = package.loaded[key3]\nif type(mod3) == [[table]] then\nfor kA, vA in pairs(mod3) do\nif type(kA) == [[string]] and type(vA) == [[function]] then\ndumpFn(vA, [[rk_]] .. kA)\nend\nend\nend\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump ~= true then\nUI.bMikuRikiDump = true\nlocal oldOI = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r1 = table.pack(oldOI(nm, ...))\nif nm == [[BackpackRiki]] and UI.bMikuRikiDumped ~= true then\nUI.bMikuRikiDumped = true\nlocal okR, rp = pcall(UI.GetUI, [[BackpackRiki]])\nif okR and type(rp) == [[table]] then\ndumpFn(rp.OnOpen, [[ri_OnOpen]])\ndumpFn(rp.Show, [[ri_Show]])\nlocal rmt = getmetatable(rp)\nif type(rmt) == [[table]] and type(rmt.__index) == [[table]] then\nfor kR, vR in pairs(rmt.__index) do\nif type(kR) == [[string]] and type(vR) == [[function]] then\ndumpFn(vR, [[cr_]] .. kR)\nend\nend\nend\nend\nend\nreturn table.unpack(r1, 1, r1.n)\nend\nend\n-- 112n：图录「？」详情函数补抓。box OnSelectChange 实锤详情渲染走\n-- self.ItemInfo:Show({tpCfg=item.info, nType=ShowItemType.Riki})，\n-- ItemInfo 类不在 uw_backpack_riki 模块表。两路补抓：一 wrap OpenInner\n-- 在图录打开时遍历页实例子 widget（含 ItemInfo）及其元表函数；二同一\n-- 时机枚举 package.loaded 全部 backpack 模块的函数。独立标志防旧\n-- guard（bMikuRikiDump/bMikuRikiDumped 跨重登持续）拦截。\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump3 ~= true then\nUI.bMikuRikiDump3 = true\nlocal oldOI3 = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r3 = table.pack(oldOI3(nm, ...))\nif nm == [[BackpackRiki]] and UI.bMikuRikiDumped2 ~= true then\nUI.bMikuRikiDumped2 = true\nlocal okR3, rp3 = pcall(UI.GetUI, [[BackpackRiki]])\nif okR3 and type(rp3) == [[table]] then\nfor kF, vF in pairs(rp3) do\nif type(kF) == [[string]] and type(vF) == [[table]] then\nfor kR3, vR3 in pairs(vF) do\nif type(kR3) == [[string]] and type(vR3) == [[function]] then\ndumpFn(vR3, [[cf_]] .. kF .. [[_]] .. kR3)\nend\nend\nlocal rmt3 = getmetatable(vF)\nif type(rmt3) == [[table]] and type(rmt3.__index) == [[table]] then\nfor kR4, vR4 in pairs(rmt3.__index) do\nif type(kR4) == [[string]] and type(vR4) == [[function]] then\ndumpFn(vR4, [[cx_]] .. kF .. [[_]] .. kR4)\nend\nend\nend\nend\nend\nend\nfor mk5, mv5 in pairs(package.loaded) do\nif type(mk5) == [[string]] and string.find(mk5, [[backpack]], 1, true) ~= nil\nand type(mv5) == [[table]] then\nfor mk6, mv6 in pairs(mv5) do\nif type(mk6) == [[string]] and type(mv6) == [[function]] then\ndumpFn(mv6, [[pk_]] .. mk5 .. [[_]] .. mk6)\nend\nend\nend\nend\nend\nreturn table.unpack(r3, 1, r3.n)\nend\nend\n-- 112o：nm==BackpackRiki 从未命中（ri_/cr_ 零产物），图录 OpenInner\n-- 注册名未知。放宽触发：面板名记录（oi: 前 30 个）与名字含 ackpack\n-- 的面板打开即 dump（页实例子 widget 与 backpack 模块全集）。\nif type(UI) == [[table]] and type(UI.OpenInner) == [[function]] and UI.bMikuRikiDump4 ~= true then\nUI.bMikuRikiDump4 = true\nlocal oldOI4 = UI.OpenInner\nUI.OpenInner = function(nm, ...)\nlocal r4 = table.pack(oldOI4(nm, ...))\nlocal cnt4 = tonumber(UI.bMikuOICnt) or 0\nif math.min(cnt4, 30) ~= 30 then\nUI.bMikuOICnt = cnt4 - (-1)\nlogErr([[oi:]] .. tostring(nm))\nend\nif type(nm) == [[string]] and string.find(nm, [[ackpack]], 1, true) ~= nil\nand UI.bMikuRikiDumped2 ~= true then\nUI.bMikuRikiDumped2 = true\nlocal rp4 = nil\nlocal okR4, rpA = pcall(UI.GetUI, nm)\nif okR4 and type(rpA) == [[table]] then rp4 = rpA end\nif rp4 == nil then\nlocal okR5, rpB = pcall(UI.GetUI, [[BackpackRiki]])\nif okR5 and type(rpB) == [[table]] then rp4 = rpB end\nend\nif type(rp4) == [[table]] then\nfor kF4, vF4 in pairs(rp4) do\nif type(kF4) == [[string]] and type(vF4) == [[table]] then\nfor kR5, vR5 in pairs(vF4) do\nif type(kR5) == [[string]] and type(vR5) == [[function]] then\ndumpFn(vR5, [[cf_]] .. kF4 .. [[_]] .. kR5)\nend\nend\nlocal rmt4 = getmetatable(vF4)\nif type(rmt4) == [[table]] and type(rmt4.__index) == [[table]] then\nfor kR6, vR6 in pairs(rmt4.__index) do\nif type(kR6) == [[string]] and type(vR6) == [[function]] then\ndumpFn(vR6, [[cx_]] .. kF4 .. [[_]] .. kR6)\nend\nend\nend\nend\nend\nend\nfor mk7, mv7 in pairs(package.loaded) do\nif type(mk7) == [[string]] and string.find(mk7, [[backpack]], 1, true) ~= nil\nand type(mv7) == [[table]] then\nfor mk8, mv8 in pairs(mv7) do\nif type(mk8) == [[string]] and type(mv8) == [[function]] then\ndumpFn(mv8, [[pk_]] .. mk7 .. [[_]] .. mk8)\nend\nend\nend\nend\nend\nreturn table.unpack(r4, 1, r4.n)\nend\nend\nend\nend\nlogErr([[choose text fix installed]])\nend\nlocal function tryPatchAfterOpen()\n-- 第 108 节：背包归置 SetItemPos 崩溃护栏（用户报过一次 invalid parameter A，\n-- 复现不稳定）。包 uw_backpack_bag_jigsaw.SetItemPos：先走原函数，崩了再\n-- 手动摆有效项、坏项跳过并记日志（module 级幂等，新实例创建时拷到新副本）。\npcall(function()\nlocal kJ = [[UMG.Backpack.Widgets.uw_backpack_bag_jigsaw]]\nlocal J = package.loaded[kJ]\nif type(J) == [[table]] and type(J.SetItemPos) == [[function]] and J.bMikuPosGuard ~= true then\nJ.bMikuPosGuard = true\nlocal origPos = J.SetItemPos\nJ.SetItemPos = function(s2, ...)\nlocal okC, errC = pcall(origPos, s2, ...)\nif okC then return end\nlocal g = s2.tbGrid\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos and w.anchorOffset and w.RenderTransform then\npcall(function()\nw.Slot:SetPosition(cell.Data.pos - w.anchorOffset * w.RenderTransform.Scale)\nend)\nend\nend\nend\nlocal lf = io.open(sLog, [[a]])\nif lf then\nlf:write([[SetItemPos guard: ]] .. tostring(errC))\nlf:write(string.char(10))\nfor _, w in pairs(s2.tbOnGridItem or {}) do\nlocal okCell = false\nif type(w) == [[table]] and w.nPos ~= nil and type(g) == [[table]] then\nlocal cell = g[w.nPos]\nif cell and cell.Data and cell.Data.pos then okCell = true end\nend\nif not okCell then\nlf:write([[bad u=]] .. tostring(w and w.uId) .. [[ pos=]] .. tostring(w and w.nPos) ..\n[[ dir=]] .. tostring(w and w.nDir) ..\n[[ tp=]] .. tostring(w and w.tpCfg and w.tpCfg.ID))\nlf:write(string.char(10))\nend\nend\nlf:close()\nend\nend\nend\nend)\nlocal okI, pi = pcall(UI.GetUI, sUI)\nif okI and type(pi) == [[table]] then\nlocal did = patchShopInst(pi)\nif did and pi.__bOpen == true then\npcall(function() pi:RefreshShop() end)\nend\nend\npcall(patchChooseText)\n-- 111r：队伍 buff 被动探针。组合效果查看页读谁、返回什么结构，开页即记录。\n-- BagRogue 模块可写（bMiku* 旗标先例），幂等守标防重包。\ndo\nlocal BR = _G.BagRogue\nif type(BR) == [[table]] then\nlocal function dumpRow(t)\nif type(t) ~= [[table]] then return tostring(t) end\nlocal ps = {}\nfor k, v in pairs(t) do\nif type(k) == [[string]] or type(k) == [[number]] then\nif type(v) == [[string]] then\ntable.insert(ps, tostring(k) .. [[=]] .. string.sub(v, 1, 40))\nelseif type(v) == [[number]] or type(v) == [[boolean]] then\ntable.insert(ps, tostring(k) .. [[=]] .. tostring(v))\nelse\ntable.insert(ps, tostring(k) .. [[:(]] .. type(v) .. [[)]])\nend\nend\nend\ntable.sort(ps)\nreturn table.concat(ps, [[ | ]])\nend\nlocal function logRet(tag, rs)\nlocal lfp = io.open(sLog, [[a]])\nif lfp then\nlfp:write(tag .. [[ nret=]] .. tostring(#rs))\nfor ri = 1, #rs do\nlocal rv = rs[ri]\nif type(rv) == [[table]] then\nlfp:write([[ ret]] .. ri .. [[#=]] .. tostring(#rv))\nif #rv ~= 0 then\nfor ei = 1, math.min(#rv, 4) do\nlfp:write([[ e]] .. ei .. [[:]] .. dumpRow(rv[ei]))\nend\nelse\nlocal ci = 0\nfor k, v in pairs(rv) do\nci = ci - (-1)\nif ci == 1 or ci == 2 or ci == 3 or ci == 4 then\nlfp:write([[ k]] .. tostring(k) .. [[:]] .. dumpRow(v))\nend\nend\nend\nelse\nlfp:write([[ ret]] .. ri .. [[=]] .. type(rv))\nend\nend\nlfp:write(string.char(10))\nlfp:close()\nend\nend\nif type(BR.GetAllTeamBuff) == [[function]] and BR.bMikuTbLog ~= true then\nBR.bMikuTbLog = true\nlocal oldTb = BR.GetAllTeamBuff\nBR.GetAllTeamBuff = function(...)\nlocal rs = {oldTb(...)}\npcall(logRet, [[teambuff]], rs)\nreturn table.unpack(rs)\nend\nend\nif type(BR.GetRelicItemAll) == [[function]] and BR.bMikuRaLog ~= true then\nBR.bMikuRaLog = true\nlocal oldRa = BR.GetRelicItemAll\nBR.GetRelicItemAll = function(...)\nlocal rs = {oldRa(...)}\npcall(logRet, [[relicall]], rs)\nreturn table.unpack(rs)\nend\nend\nend\nend\nend\nif type(UI) == [[table]] and type(UI.Open) == [[function]] and UI.bMikuShopInst ~= true then\nUI.bMikuShopInst = true\nlocal fnOpen = UI.Open\nUI.Open = function(...)\nlocal r = table.pack(fnOpen(...))\npcall(tryPatchAfterOpen)\nreturn table.unpack(r, 1, r.n)\nend\nend",
			"PalBuildSeqFallback" => "if MikuPalFixVer ~= 7 then\nMikuPalFixVer = 7\nif _G.MikuPalSeqWrapped ~= true and type(PalManage) == [[table]] and type(PalManage.PlayBuildingSeq) == [[function]] then\n_G.MikuPalSeqWrapped = true\nlocal mMikuSeqOrig = PalManage.PlayBuildingSeq\nlocal function mMikuSeqRestore()\npcall(function() if type(PalClient) == [[table]] then PalClient.CanOpenMap = true end end)\npcall(function()\nlocal pc = UE4.UGameplayStatics.GetPlayerController(GetGameIns(), 0)\nif pc ~= nil then pc.bIgnoreEsc = false end\nend)\npcall(function()\nlocal top = UI.GetTop()\nif top ~= nil then WidgetUtils.SelfHitTestInvisible(top) end\nend)\nend\nPalManage.PlayBuildingSeq = function(name, cb)\nlocal done = false\nlocal function finish()\nif done == true then return end\ndone = true\nmMikuSeqRestore()\nif type(cb) == [[function]] then pcall(cb) end\nend\nlocal has = false\npcall(function()\nlocal a = UE4.ULevelLibrary.GetActorByName(GetGameIns(), [[PalBuildingLevelSeq]] .. tostring(name), UE4.ALevelSequenceActor.StaticClass())\nhas = a ~= nil and a.SequencePlayer ~= nil\nend)\nif has ~= true then\nfinish()\nreturn\nend\nlocal ok = pcall(mMikuSeqOrig, name, finish)\nif ok ~= true then finish() end\nUE4.Timer.Add(8, function() pcall(finish) end)\nend\nend\nif _G.MikuUpRspWrapped ~= true and type(PalManage) == [[table]] and type(PalManage.UpgradeBuildingLevelRsp) == [[function]] then\n_G.MikuUpRspWrapped = true\nif type(PalManage.TryBuildLevelUp) == [[function]] then\nlocal mMikuTryUp = PalManage.TryBuildLevelUp\nPalManage.TryBuildLevelUp = function(nId)\n_G.__mikuPlayerUp = nId\n_G.__mikuPlayerUpAt = os.time()\nreturn mMikuTryUp(nId)\nend\nend\nlocal mMikuUpRsp = PalManage.UpgradeBuildingLevelRsp\nPalManage.UpgradeBuildingLevelRsp = function(tb)\nlocal r = mMikuUpRsp(tb)\npcall(function()\nif type(tb) ~= [[table]] or tb.nId == nil then return end\nlocal want = _G.__mikuPlayerUp\n_G.__mikuPlayerUp = nil\nif want == nil or want ~= tb.nId then return end\nlocal lv = VirCaptureBuild:GetBuildLevel(tb.nId)\nif lv == nil or lv == 0 then return end\nUE4.Timer.Add(0.6, function()\npcall(function()\nPalManage.PlayBuildingSeq(tb.nId, function()\npcall(function() UI.Open([[builtsuccess]], tb.nId) end)\nend)\nend)\nend)\nend)\nreturn r\nend\nend\nMikuPalBusy = 0\nlocal function palFix()\npcall(function()\nlocal ui = UI.GetUI([[buildpop]])\nif ui ~= nil and ui:IsOpen() then\nui.pCallBack = nil\nui.pCancel = nil\nui.bConfirm = nil\nlocal panel = nil\nif type(ui.PopPanel) == [[table]] then panel = ui.PopPanel[15] end\nif panel ~= nil then pcall(function() WidgetUtils.Collapsed(panel) end) end\npcall(function() ui:TryClose() end)\nif ui:IsOpen() then pcall(function() UI.Close(ui) end) end\nend\nmMikuSeqRestore()\nend)\nend\nlocal function palTick()\nlocal busy = false\npcall(function()\nlocal ui = UI.GetUI([[buildpop]])\nif ui ~= nil and ui:IsOpen() and ui.bConfirm == true then busy = true end\nend)\nif busy then\nMikuPalBusy = MikuPalBusy - (-1)\nif MikuPalBusy == 10 then\npalFix()\nMikuPalBusy = 0\nend\nelse\nMikuPalBusy = 0\nend\nUE4.Timer.Add(3, palTick)\nend\nUE4.Timer.Add(3, palTick)\n-- 506：岛上开箱后没有任何反馈，服务端只发奖励、客户端蓝图不弹提示。\nif _G.MikuOpenBoxTipWrapped ~= true and type(VirCapture) == [[table]] and type(VirCapture.DoSaveCaptureMonster) == [[function]] then\n_G.MikuOpenBoxTipWrapped = true\nlocal mMikuSaveCapture = VirCapture.DoSaveCaptureMonster\nVirCapture.DoSaveCaptureMonster = function(a, b)\nlocal r1, r2 = mMikuSaveCapture(a, b)\npcall(function()\nUE4.Timer.Add(1.8, function()\npcall(function() UI.ShowTip([[获得：新星开拓经验 ×200、万能鱼饵 ×2]]) end)\nend)\nend)\nreturn r1, r2\nend\nend\nend",
			"BossPvpFix" => "local tbF = { [[D:/DSH/.tools/bppvp.lua]], [[D:/DSH/.tools/bpweap.lua]], [[D:/DSH/.tools/bpbossinfo.lua]], [[D:/DSH/.tools/bpnum.lua]], [[D:/DSH/.tools/bpdlc.lua]] }\nif _G.bMikuBossKit ~= true then\n_G.bMikuBossKit = true\nfor _, f in ipairs(tbF) do\npcall(dofile, f)\nend\nend",
			"PrologueSkip" => "local LOGF = [[D:/DSH/.tools/prologue.log]]\nlocal MOD = [[UMG.Dialogue.DialogueMgr]]\nlocal function L(s)\nlocal f = io.open(LOGF, [[a]])\nif f then\nf:write(os.date([[%H:%M:%S]]) .. [[ ]] .. tostring(s))\nf:write(string.char(10))\nf:close()\nend\nend\nlocal function findDlg()\nif type(_G.UI) ~= [[table]] or type(UI.tbWidget) ~= [[table]] then return nil end\nfor _, r in pairs(UI.tbWidget) do\nif type(r) == [[table]] and r.sName == [[dialogue]] and r.IsGuideing == true then return r end\nend\nreturn nil\nend\nlocal function isOld()\nlocal G = _G.GuideLogic\nif type(G) ~= [[table]] or type(G.IsGuideComplete) ~= [[function]] then return true end\nlocal ok, r = pcall(G.IsGuideComplete, G, G.BeginnerGId or 187)\nif not ok then return true end\nreturn r == true\nend\nlocal function doSkip(tag)\nif _G.bMikuPrologueSkip == true then return end\nif isOld() ~= true then return end\nlocal dlg = findDlg()\nif dlg == nil then return end\nlocal pl = package.loaded\nlocal M = pl[MOD]\nlocal G = _G.GuideLogic\nif type(M) ~= [[table]] or type(G) ~= [[table]] then return end\n_G.bMikuPrologueSkip = true\npcall(M.ForceCloseSequence, M)\npcall(M.Stop, M)\npcall(G.SkipAllGuide, G)\npcall(G.EndGuide, G)\nL(tag .. [[ skipped prologue guide dialogue]])\nend\ndoSkip([[load]])\nif type(_G.EventSystem) == [[table]] and type(_G.EventSystem.Trigger) == [[function]] and _G.bMikuPrologueHook ~= true then\n_G.bMikuPrologueHook = true\nlocal old = _G.EventSystem.Trigger\nlocal n = 0\n_G.EventSystem.Trigger = function(e, ...)\nn = n - (-1)\nif (n % 40) == 0 then pcall(doSkip, [[tick]]) end\nreturn old(e, ...)\nend\nend",
			"NoFormerShop" => NoFormerShop,
			"Dlc24Unlock" => Dlc24Unlock,
			"PaintFix" => PaintFix,
			"DormAvatar" => DormAvatar,
			"LoginGuard" => LoginGuard,
			"ThrowChallengeContinue" => ThrowChallengeContinue,
			"LogiRebuildUnlock" => LogiRebuildUnlock,
			"LogiRebuildNoConfirm" => LogiRebuildNoConfirm,
			"LogiRebuildKeepIndex" => LogiRebuildKeepIndex,
			"Rogue3SeasonOpen" => Rogue3SeasonOpen,
			"Rogue3BlessFix" => Rogue3BlessFix,
			"Rogue3WarWindowFix" => Rogue3WarWindowFix,
			"LogiCardLock" => LogiCardLock,
			"FarmWarSlotFix" => FarmWarSlotFix,
			"FarmWarGrowFix" => FarmWarGrowFix,
			"FarmWarItemFix" => FarmWarItemFix,
			"FarmWarLevelFix" => FarmWarLevelFix,
			"FarmWarPlantFix" => FarmWarPlantFix,
			"AlienPalUnlockFix" => AlienPalUnlockFix,
			"Repression4NodeFix" => Repression4NodeFix,
			"SeasonBossZoneHide" => SeasonBossZoneHide,
			"DlcPuzzleOpen" => DlcPuzzleOpen,
			"S2CSilentNoise" => S2CSilentNoise,
			"SignShortPopOff" => SignShortPopOff,
			"GachaTryOldHide" => GachaTryOldHide,
			"BirthdayWishYears" => BirthdayWishYears,
			"SeasonBossYmirScene" => SeasonBossYmirScene,
			"RmolPvpLoadingFix" => RmolPvpLoadingFix,
			"DonoDuelInteractName" => DonoDuelInteractName,
			"DonoDuelStarterFightBag" => DonoDuelStarterFightBag,
			"DonoDuelGuide64107" => DonoDuelGuide64107,
			"DonoDuelAcceptStoryRetry" => DonoDuelAcceptStoryRetry,
			"DonoDuelGuideRetry" => DonoDuelGuideRetry,
			"DonoDuelAvatar" => DonoDuelAvatar,
			"DonoDuelCardPop" => DonoDuelCardPop,
			_ => null
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static async Task<bool> Send(Connection connection, string name, string lua)
	{
		char[] forbiddenPatchChars = ForbiddenPatchChars;
		foreach (char ch in forbiddenPatchChars)
		{
			if (lua.IndexOf(ch) >= 0)
			{
				Log($"补丁 {name} 含禁用字符 {ch}，丢弃（客户端 Json 解不了这种转义）");
				return false;
			}
		}
		string payload = new JsonObject { ["code"] = lua }.ToJsonString(PatchJsonOpts);
		await CallGSRouter.SendScript(connection, "GM", payload);
		return true;
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "dlcpatches.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
