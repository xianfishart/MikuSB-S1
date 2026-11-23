# -*- coding: utf-8 -*-
import io

p = r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs'
s = io.open(p, encoding='utf-8').read()
NL = chr(92) + 'n'

new1 = NL.join([
 'local function dmLoveMapId()',
 'local R = _G.RichmanLogic',
 'local v = R and tonumber(R.MapID)',
 'if v and v ~= 0 then return v end',
 'local RL = L.MapID',
 'if type(RL) == [[number]] and RL ~= 0 then return RL end',
 'return 1',
 'end',
 'local function dmLoveTrace(nm, err)',
 'local tb = select(2, pcall(debug.traceback, [[ ]], 3))',
 'pcall(dmLoveLog, nm, tostring(err) .. [[ | ]] .. tostring(tb))',
 'end',
 'local function dmLoveWrapMap(nm, fb)',
 'local f = L[nm]',
 'if type(f) ~= [[function]] then return end',
 'local w = function(a, ...)',
 'if type(a) ~= [[number]] or a == 0 then a = dmLoveMapId() end',
 'local r = pack(pcall(f, a, ...))',
 'if r[1] then return unpack(r, 2, r.n) end',
 'pcall(dmLoveTrace, nm, r[2])',
 'if type(fb) == [[function]] then return fb() end',
 'return fb',
 'end',
 'w.bMikuLoveMap = true',
 'L[nm] = w',
 'end',
 'local function dmLoveWrap(nm, fb)',
 'local f = L[nm]',
])

old1 = 'local function dmLoveWrap(nm, fb)' + NL + 'local f = L[nm]'
assert s.count(old1) == 1, ('anchor1', s.count(old1))
s = s.replace(old1, new1)

old2 = NL.join([
 'dmLoveWrap([[GetMapDay]], 1)',
 'dmLoveWrap([[GetMapIsEntered]], false)',
 'dmLoveWrap([[GetMapAccumulatedMoney]], 0)',
])
new2 = NL.join([
 'dmLoveWrapMap([[GetMapDay]], 1)',
 'dmLoveWrapMap([[GetMapIsEntered]], false)',
 'dmLoveWrapMap([[GetMapAccumulatedMoney]], 0)',
])
assert s.count(old2) == 1, ('anchor2', s.count(old2))
s = s.replace(old2, new2)

old3 = 'pcall(dmLoveLog, nm, r[2])' + NL + 'if type(fb) == [[function]] then return fb() end'
new3 = 'pcall(dmLoveTrace, nm, r[2])' + NL + 'if type(fb) == [[function]] then return fb() end'
assert s.count(old3) >= 1, ('anchor3', s.count(old3))
s = s.replace(old3, new3)

deep = NL.join([
 'dmLoveWrap([[IsReceiveEnd]], false)',
 '-- 全模块深度守卫：把恋爱线两个模块的所有函数都包一层，',
 '-- 出错时把调用栈写进 loveguard.log，用来定位白屏崩在哪一步。',
 'local function dmLoveDeep()',
 'local mods = { _G.RichManLoveLogic, _G.DLC_RichManLove }',
 'for mi = 1, 2 do',
 'local mod = mods[mi]',
 'if type(mod) == [[table]] then',
 'local keys = {}',
 'for k, v in pairs(mod) do',
 'if type(v) == [[function]] and type(k) == [[string]] then keys[#keys + 1] = k end',
 'end',
 'for i = 1, #keys do',
 'local nm = keys[i]',
 'local f = mod[nm]',
 'if type(f) == [[function]] and not f.bMikuDeep then',
 'local w = function(...)',
 'local r = pack(pcall(f, ...))',
 'if r[1] then return unpack(r, 2, r.n) end',
 'pcall(dmLoveTrace, nm, r[2])',
 'return nil',
 'end',
 'w.bMikuDeep = true',
 'mod[nm] = w',
 'end',
 'end',
 'end',
 'end',
 'end',
 'pcall(dmLoveDeep)',
 'end',
 '_G.MikuDmLoveGuard = dmLoveGuard',
])
old4 = NL.join([
 'dmLoveWrap([[IsReceiveEnd]], false)',
 'end',
 '_G.MikuDmLoveGuard = dmLoveGuard',
])
assert s.count(old4) == 1, ('anchor4', s.count(old4))
s = s.replace(old4, deep)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched ok')
