# -*- coding: utf-8 -*-
import io, sys
p = r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs'
src = io.open(p, encoding='utf-8').read()
lines = src.split('\n')
BS = chr(92)

# 1) UI.Open wrapper: run fix before and after
done1 = False
for i, l in enumerate(lines):
    if 'UI.Open = function(...) local r = table.pack(pzPrevO(...)) pcall(mikuPzFix)' in l:
        lines[i] = ('\t\t+ "UI.Open = function(...) pcall(mikuPzFix) local r = table.pack(pzPrevO(...)) '
                    'pcall(mikuPzFix) return table.unpack(r, 1, r.n) end' + BS + 'n"')
        done1 = True
        break

# 2) insert mikuPzEnter after mikuSwGate
block = [
    'local function mikuPzEnter()',
    'local J = _G.JigsawPuzzle',
    'if type(J) ~= [[table]] or type(J.EnterByActivityId) ~= [[function]] then return end',
    'if J.EnterByActivityId == _G.fnMikuPzEnter then return end',
    'local prev = J.EnterByActivityId',
    '_G.fnMikuPzEnter = function(actId)',
    'local A = _G.Activity',
    'local set = _G.tbMikuPuzzleForce',
    'local id = tonumber(actId) or 0',
    'local cfg = nil',
    'if type(A) == [[table]] and type(A.GetActivityConfig) == [[function]] then cfg = A.GetActivityConfig(actId) end',
    'if type(set) == [[table]] and set[id] and type(cfg) == [[table]] then',
    'local cd1 = cfg.tbCustomData1',
    'local sch = 0',
    'if type(cd1) == [[table]] then sch = tonumber(cd1[1]) or 0 end',
    'if sch ~= 0 then',
    'UI.Open([[JigsawPuzzlePlay]], actId, sch)',
    'return',
    'end',
    'end',
    'return prev(actId)',
    'end',
    'end',
]
new = ['\t\t+ "' + ln + BS + 'n"' for ln in block]
idx = None
for i, l in enumerate(lines):
    if 'R.IsOpenById = _G.fnMikuSwGate' in l:
        idx = i + 2
        break
if idx is None:
    print('anchor missing'); sys.exit(1)
if '"local function mikuPzEnter()' not in src:
    lines[idx:idx] = new

# 3) update fix list
for i, l in enumerate(lines):
    if 'local t = { mikuPzSet, mikuPzHook' in l:
        lines[i] = ('\t\t+ "local t = { mikuPzSet, mikuPzHook, mikuPzCard, mikuFwFix, mikuSwGate, '
                    'mikuPzEnter, mikuPzRedraw }' + BS + 'n"')
        lines[i + 1] = '\t\t+ "for i = 1, 7 do pcall(t[i]) end' + BS + 'n"'
        break

io.open(p, 'w', encoding='utf-8').write('\n'.join(lines))
print('done1', done1)
