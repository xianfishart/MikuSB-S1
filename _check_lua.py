# -*- coding: utf-8 -*-
"""提取 DefaultModeWindows 的 Lua 模板，做占位符替换，并做括号/关键字配平检查。"""
import io
import re
import sys


def extract(path):
    s = io.open(path, encoding='utf-8').read()
    i = s.find('case "DefaultModeWindows":')
    if i < 0:
        raise SystemExit('case not found: ' + path)
    j = s.find('lua = "', i)
    k = j + len('lua = "')
    out = []
    esc = False
    while k < len(s):
        c = s[k]
        if esc:
            if c == 'n':
                out.append('\n')
            elif c == 't':
                out.append('\t')
            elif c == 'r':
                out.append('\r')
            elif c == '"':
                out.append('"')
            elif c == '\\':
                out.append('\\')
            else:
                out.append(c)
            esc = False
            k += 1
            continue
        if c == '\\':
            esc = True
            k += 1
            continue
        if c == '"':
            break
        out.append(c)
        k += 1
    return ''.join(out)


def strip_lua(s):
    """去掉注释与字符串，只留关键字，便于配平。"""
    res = []
    i = 0
    n = len(s)
    while i < n:
        if s.startswith('--[[', i):
            e = s.find(']]', i + 4)
            i = n if e < 0 else e + 2
        elif s.startswith('--', i):
            e = s.find('\n', i)
            i = n if e < 0 else e + 1
        elif s.startswith('[[', i):
            e = s.find(']]', i + 2)
            i = n if e < 0 else e + 2
            res.append(' ')
        elif s[i] in '"\'':
            q = s[i]
            i += 1
            while i < n:
                if s[i] == '\\':
                    i += 2
                    continue
                if s[i] == q:
                    i += 1
                    break
                i += 1
            res.append(' ')
        else:
            res.append(s[i])
            i += 1
    return ''.join(res)


def stats(s):
    t = strip_lua(s)
    words = re.findall(r'[A-Za-z_][A-Za-z_0-9]*', t)
    cnt = {}
    for w in words:
        cnt[w] = cnt.get(w, 0) + 1
    opens = cnt.get('function', 0) + cnt.get('if', 0) + cnt.get('for', 0) + cnt.get('while', 0)
    # do 单独出现（不含 for/while 自带）
    opens += max(0, cnt.get('do', 0) - cnt.get('for', 0) - cnt.get('while', 0) - cnt.get('repeat', 0))
    ends = cnt.get('end', 0) + cnt.get('until', 0)
    return opens, ends, cnt


old = extract(r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs.bak-dlc5entry')
new = extract(r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs')

for tag, s in (('OLD', old), ('NEW', new)):
    o, e, cnt = stats(s)
    print('%s: len=%d opens=%d ends=%d  function=%d if=%d for=%d do=%d end=%d'
          % (tag, len(s), o, e, cnt.get('function', 0), cnt.get('if', 0),
             cnt.get('for', 0), cnt.get('do', 0), cnt.get('end', 0)))

print('delta opens=%d ends=%d' % (stats(new)[0] - stats(old)[0], stats(new)[1] - stats(old)[1]))

# 生成第 5 期的实际下发文本
gen = (new.replace('MIKU_ACT_ERA', '5')
          .replace('MIKU_TRY_IDS', '72,74')
          .replace('MIKU_HIDE_ENTRY', 'false'))
left = [m for m in ('MIKU_',) if m in gen]
print('残留占位符:', left)
io.open(r'D:\DSH\.tools\dm_check.lua', 'w', encoding='utf-8').write(gen)
print('written D:/DSH/.tools/dm_check.lua, len=%d' % len(gen))
print('首行:', gen.split('\n')[0][:60])
idx = gen.find('local function dmShowEntry')
print('dmShowEntry 段:')
print(gen[idx:idx + 420])
