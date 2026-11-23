# -*- coding: utf-8 -*-
import io

p = r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs'
s = io.open(p, encoding='utf-8').read()
NL = chr(92) + 'n'
n = 0

old1 = '.. type(era)' + NL + '-- 主线故事'
new1 = '.. type(era)' + NL + 'local bMikuHideEntry = MIKU_HIDE_ENTRY' + NL + '-- 主线故事'
assert s.count(old1) == 1, ('1', s.count(old1))
s = s.replace(old1, new1)
n += 1

old2 = ('-- 主界面「战斗」左上角那块版本活动入口（BtnDLC 按钮与 BlankDLC 大图）在 pin 任意期' + NL
        + '-- 都整块取消掉：老期海报贴图在 3.2.0 客户端已经缺失，留着只能靠兜底图撑着。' + NL)
new2 = ('-- 主界面「战斗」左上角那块版本活动入口（BtnDLC 按钮与 BlankDLC 大图）。' + NL
        + '-- 以前是「pin 任意期都整块取消」，结果切到 5 期及以后入口也不出来（第 231 节）。' + NL
        + '-- 现在按服务端下发的开关走：MIKU_HIDE_ENTRY=true 才收（游戏默认 / 1~4 期 / EraLite），' + NL
        + '-- 其余期交给游戏原生 ShowBtnDlc 显示，服务端只负责把进入窗口推到现在。' + NL)
assert s.count(old2) == 1, ('2', s.count(old2))
s = s.replace(old2, new2)
n += 1

old3 = 'pcall(dmHideEntry)' + NL + '-- 活动列表（左栏）重复条目修复'
new3 = ('local function dmShowEntry()' + NL
        + 'local main' + NL
        + 'for _, x in pairs(UI.tbWidget) do' + NL
        + 'if type(x) == [[table]] and tostring(x.sName) == [[main]] and x.BtnDLC then main = x break end' + NL
        + 'end' + NL
        + 'if not main then return end' + NL
        + '-- 之前可能被 dmHideEntry 包过 ShowBtnDlc（包完还会自己再收一次），这里拆掉再显式调一次。' + NL
        + 'if main.bMikuDlcHidden == true and type(main.bMikuDlcShow) == [[function]] then' + NL
        + 'main.ShowBtnDlc = main.bMikuDlcShow' + NL
        + 'main.bMikuDlcHidden = nil' + NL
        + 'main.bMikuDlcShow = nil' + NL
        + 'end' + NL
        + 'if type(main.ShowBtnDlc) == [[function]] then pcall(function() main:ShowBtnDlc() end) end' + NL
        + 'end' + NL
        + 'if bMikuHideEntry then pcall(dmHideEntry) else pcall(dmShowEntry) end' + NL
        + '-- 活动列表（左栏）重复条目修复')
assert s.count(old3) == 1, ('3', s.count(old3))
s = s.replace(old3, new3)
n += 1

old4 = 'local r = oldOpen(...) pcall(dmHideEntry) pcall(dmListFix) return r end'
new4 = ('local r = oldOpen(...) if bMikuHideEntry then pcall(dmHideEntry) else pcall(dmShowEntry) end '
        'pcall(dmListFix) return r end')
assert s.count(old4) == 1, ('4', s.count(old4))
s = s.replace(old4, new4)
n += 1

# 5) 通用替换链末尾追加 MIKU_HIDE_ENTRY（按位置插入，避免空白差异）
anchor = 'lua = lua.Replace("MIKU_ACTIVATION_FLOOR"'
i = s.find(anchor)
assert i > 0, 'anchor not found'
tail = '.Replace("MIKU_TRY_IDS", tryIds, StringComparison.Ordinal);'
j = s.find(tail, i)
assert j > 0, 'chain tail not found'
j += len(tail)
add = NL + '\t\t\t\t\t.Replace("MIKU_HIDE_ENTRY", DlcMainEntry.EntryHiddenFor(eraId) ? "true" : "false", StringComparison.Ordinal)'
s = s[:j] + add + ';' + s[j:]
n += 1

io.open(p, 'w', encoding='utf-8').write(s)
print('patched', n)
