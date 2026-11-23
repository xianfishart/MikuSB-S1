import io

p = 'DlcClientPatches.cs'
s = io.open(p, encoding='utf-8-sig', newline='').read()

lua = io.open('_dcchall.lua', encoding='utf-8', newline='').read().strip().replace(chr(13), '').replace(chr(10), '')

# 合并成单行，但仍保留语义：原文件每行之间用换行分隔 -> 用分号不可靠，
# 因此改成用 BS+'n' 的 Lua 转义序列（lua 加载时还原为换行）。
src = io.open('_dcchall.lua', encoding='utf-8', newline='').read().strip().replace(chr(13), '')
BS = chr(92)
src = src.replace(chr(10), BS + 'n')

block = (
    chr(9) + '/// <summary>\n'
    + chr(9) + '/// 第 494 节：纷愿绮梦「纷彩世界」页签点一下就弹「暂未开放」。\n'
    + chr(9) + '///\n'
    + chr(9) + '/// 客户端 luckyreward.OnOpen 的判定：\n'
    + chr(9) + '///   local cur = me:GetAttribute(DreamCard.LevelGID, 0)\n'
    + chr(9) + '///   local period = DreamCardConf.tbChallTimeConf[cur]\n'
    + chr(9) + '///   bChallOpen = (period ~= nil)\n'
    + chr(9) + '/// 本端 chall_time.json 最后一期（ID 4）时间窗是 2026-01-29 ~ 2026-05-14，早已过期；\n'
    + chr(9) + '/// 服务端已把期号写进 152/0（DreamCardAchievementService.EnsureChallPeriod），\n'
    + chr(9) + '/// 但客户端算倒计时、判到期用的是自己包里那份 nEndTime，几天后又会锁上。\n'
    + chr(9) + '/// 这里把最后一期窗口推到「昨天 ~ 2100」，并在 UI.Open 前后各补一次。\n'
    + chr(9) + '/// </summary>\n'
    + chr(9) + 'public const string DreamCardChallFix = "' + src + '";\n'
)

marker = chr(9) + 'public const string Rogue3WarWindowFix = '
i = s.index(marker)
j = s.index(chr(10), i) + 1
s = s[:j] + block + s[j:]

add = chr(9) + chr(9) + 'list.Add(("DreamCardChallFix", DreamCardChallFix));' + chr(10)
k = s.index(chr(9) + chr(9) + 'list.Add(("MikuEval"')
s = s[:k] + add + s[k:]

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print('ok')
