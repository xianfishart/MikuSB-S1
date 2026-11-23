# -*- coding: utf-8 -*-
import io

LUA = """if MikuPalSeqVer ~= 1 then
MikuPalSeqVer = 1
local function seqBusy(nId)
local r = false
pcall(function()
local nm = string.format([[PalBuildingLevelSeq%d]], nId)
local a = UE4.ULevelLibrary.GetActorByName(GetGameIns(), UE4.ALevelSequenceActor.StaticClass(), nm)
local sp = nil
if a ~= nil then sp = a.SequencePlayer end
if sp ~= nil then
local v = sp.IsPlaying
if type(v) == [[function]] then
local ok0, b = pcall(v, sp)
if ok0 then r = (b == true) end
else
r = (v == true)
end
end
end)
return r
end
local function wrapSeq()
local pm = _G.PalManage
if type(pm) ~= [[table]] then return false end
if type(pm.PlayBuildingSeq) ~= [[function]] then return false end
if pm.bMikuSeqWrap == true then return true end
pm.bMikuSeqWrap = true
local old = pm.PlayBuildingSeq
pm.PlayBuildingSeq = function(self, nId, pSeqEnd)
if type(pSeqEnd) ~= [[function]] then return old(self, nId, pSeqEnd) end
local done = false
local function finish()
if done then return end
done = true
pcall(pSeqEnd)
end
local wrapped = function(...)
if done then return end
done = true
return pSeqEnd(...)
end
local ok1 = pcall(old, self, nId, wrapped)
if not ok1 then finish() return end
local function watch(left)
if done then return end
if seqBusy(nId) then
if left ~= 0 then UE4.Timer.Add(1, function() watch(left - 1) end) end
return
end
finish()
end
UE4.Timer.Add(2, function() watch(20) end)
end
return true
end
local function trySeq(left)
if wrapSeq() then return true end
if left ~= 0 then UE4.Timer.Add(1, function() trySeq(left - 1) end) end
return false
end
trySeq(120)
pcall(function()
local ui = nil
if type(UI.GetUI) == [[function]] then ui = UI.GetUI([[BuildPop]]) end
if ui == nil then return end
if not ui:IsOpen() then return end
if type(ui.pCallBack) ~= [[function]] then return end
local top = UI.GetTop()
if top == nil then return end
if top:IsVisible() == true then return end
local cb = ui.pCallBack
ui.pCallBack = nil
pcall(cb)
end)
end"""

assert '\\' not in LUA, "lua has backslash"
assert '"' not in LUA, "lua has double quote"

lit = LUA.replace('\n', '\\n')

p = r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs'
s = io.open(p, encoding='utf-8-sig' if io.open(p,'rb').read(3) == b'\xef\xbb\xbf' else 'utf-8').read()

# 1. Order 数组扩容
assert s.count('new string[66]') == 1
s = s.replace('new string[66]', 'new string[67]')

# 2. Order 列表加名字
old_order = '"DonoDuelCardPop"\r\n\t};'
if old_order not in s:
    old_order = '"DonoDuelCardPop"\n\t};'
    new_order = '"DonoDuelCardPop", "PalBuildSeqFallback"\n\t};'
else:
    new_order = '"DonoDuelCardPop", "PalBuildSeqFallback"\r\n\t};'
assert s.count(old_order) == 1, s.count(old_order)
s = s.replace(old_order, new_order)

# 3. ByName 里加映射
anchor = '\t\t\t"BossPvpFix" => '
assert s.count(anchor) == 1, s.count(anchor)
entry = '\t\t\t"PalBuildSeqFallback" => "' + lit + '",\n'
s = s.replace(anchor, entry + anchor)

io.open(p, 'w', encoding='utf-8').write(s)
print('patched ok, size', len(s))
