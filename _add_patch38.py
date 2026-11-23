# -*- coding: utf-8 -*-
import io

BS = chr(92)

lua = """
local function mikuR3Log(s)
local ok, f = pcall(io.open, [[D:/DSH/.tools/r3bless.log]], [[a]])
if ok and f then f:write(tostring(s)) f:write(string.char(10)) f:close() end
end
local function mikuR3Ensure()
local S = _G.Rogue3DSave
if type(S) ~= [[table]] then return end
if type(S.tbSaveData) ~= [[table]] then S.tbSaveData = {} end
if type(S.tbSaveData.cache) ~= [[table]] then S.tbSaveData.cache = {} end
end
local function mikuR3WrapLoad()
local S = _G.Rogue3DSave
if type(S) ~= [[table]] then return end
if type(S.Load) ~= [[function]] or _G.bMikuR3LoadWrap == true then return end
_G.bMikuR3LoadWrap = true
local old = S.Load
S.Load = function(...)
local r = table.pack(pcall(old, ...))
mikuR3Ensure()
if r[1] ~= true then mikuR3Log([[Load err: ]] .. tostring(r[2])) return nil end
return table.unpack(r, 2, r.n)
end
end
local function mikuR3WrapGetters()
local S = _G.Rogue3DSave
if type(S) ~= [[table]] then return end
local names = { [[GetChaosRefreshBuffTotalCount]], [[GetChaosRefreshBuffCurCount]] }
for _, n in ipairs(names) do
local key = [[MikuR3G_]] .. n
if type(S[n]) == [[function]] and _G[key] ~= true then
_G[key] = true
local old = S[n]
S[n] = function(...)
local ok, v = pcall(old, ...)
if not ok then mikuR3Log(n .. [[ err: ]] .. tostring(v)) return nil end
return v
end
end
end
end
local function mikuR3BlessHook()
if type(UI) ~= [[table]] or type(UI.Open) ~= [[function]] or _G.bMikuR3BlessHook == true then return end
_G.bMikuR3BlessHook = true
local oldOpen = UI.Open
UI.Open = function(...)
mikuR3Ensure()
local r = table.pack(oldOpen(...))
pcall(function()
local u = UI.GetUI and UI.GetUI([[FightRogueBless]])
if type(u) ~= [[table]] then return end
if u.TotalCount == nil then
local S = _G.Rogue3DSave
local n = nil
if type(S) == [[table]] and type(S.GetChaosRefreshBuffTotalCount) == [[function]] then
local ok, v = pcall(S.GetChaosRefreshBuffTotalCount, S)
if ok then n = v end
end
u.TotalCount = n or 1
mikuR3Log([[TotalCount fallback ]] .. tostring(u.TotalCount))
end
if type(u.OnBtnClickGO) == [[function]] and u.bMikuGO ~= true then
u.bMikuGO = true
local oldGO = u.OnBtnClickGO
u.OnBtnClickGO = function(self, ...)
local ok, err = pcall(oldGO, self, ...)
if not ok then mikuR3Log([[GO err: ]] .. tostring(err)) end
return nil
end
end
end)
return table.unpack(r, 1, r.n)
end
end
pcall(mikuR3WrapLoad)
pcall(mikuR3WrapGetters)
pcall(mikuR3BlessHook)
mikuR3Ensure()
""".strip("\n")

assert '"' not in lua and "'" not in lua, "quote found"
for ch in "+<>&" + BS:
    assert ch not in lua, ("forbidden char " + ch)

escaped = lua.replace("\n", BS + "n")

p = "DlcClientPatches.cs"
src = io.open(p, encoding="utf-8-sig").read()

anchor = "\tpublic const string Rogue3SeasonOpen = "
idx = src.index(anchor)
eol = src.index("\n", idx)

doc = (
    "\n\n\t/// <summary>\n"
    "\t/// Rogue3BlessFix：增益试炼打完第一波「卡住」的根因修。\n"
    "\t/// 客户端 umg_fight_rogue_bless.OnOpen 第一件事就是调\n"
    "\t/// Rogue3DSave.GetChaosRefreshBuffTotalCount() 取本波可选增益数，\n"
    "\t/// 而它读的是 Rogue3DSave.tbSaveData.cache.c_chaos_buffcount；\n"
    "\t/// 本端「验证战场」进关卡走 Rogue3DScoreModel_EnterBuffLevel，\n"
    "\t/// 全程没有任何一步调用 Rogue3DSave.Load，tbSaveData 恒为 nil，\n"
    "\t/// 于是 OnOpen 在取缓存时就抛 attempt to index a nil value (field tbSaveData)，\n"
    "\t/// TotalCount 留空；界面本身照常显示（三张增益卡都在），可点确认时\n"
    "\t/// OnBtnClickGO 里 TotalCount 与数字比较再抛一次 —— 表现就是\n"
    "\t/// 「打完第一波、增益界面点不动、战斗一直暂停」。\n"
    "\t/// 修法：保证 tbSaveData / cache 是表（Load 之后也兜一次）；\n"
    "\t/// 两个 chaos 计数读取包 pcall；仅在总计缺失时补 TotalCount 兜底值，\n"
    "\t/// 并在 GO 抛错时记一行日志到 D:/DSH/.tools/r3bless.log。\n"
    "\t/// </summary>"
)

const_line = '\tpublic const string Rogue3BlessFix = "' + escaped + '";'

src = src[:eol] + doc + "\n" + const_line + src[eol:]

before = src
src = src.replace("new string[37]", "new string[38]")
assert src != before, "Order size not replaced"

before = src
src = src.replace(
    '"LogiRebuildNoConfirm", "LogiRebuildKeepIndex", "Rogue3SeasonOpen"',
    '"LogiRebuildNoConfirm", "LogiRebuildKeepIndex", "Rogue3SeasonOpen",\n\t\t"Rogue3BlessFix"',
)
assert src != before, "Order list not replaced"

before = src
src = src.replace(
    '\t\t\t"Rogue3SeasonOpen" => Rogue3SeasonOpen,',
    '\t\t\t"Rogue3SeasonOpen" => Rogue3SeasonOpen,\n\t\t\t"Rogue3BlessFix" => Rogue3BlessFix,',
)
assert src != before, "ByName not replaced"

io.open(p, "w", encoding="utf-8-sig", newline="\n").write(src)
print("patched ok", len(src))
