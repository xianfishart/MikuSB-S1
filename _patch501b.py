# -*- coding: utf-8 -*-
import io, re

LUA = """if MikuPalFixVer ~= 2 then
MikuPalFixVer = 2
MikuPalBusy = 0
local function palFix()
pcall(function()
local ui = nil
if type(UI.GetUI) == [[function]] then ui = UI.GetUI([[BuildPop]]) end
if ui ~= nil and ui:IsOpen() then
ui.pCallBack = nil
ui.pCancel = nil
ui.bConfirm = nil
local panel = nil
if type(ui.PopPanel) == [[table]] then panel = ui.PopPanel[15] end
if panel ~= nil then pcall(function() WidgetUtils.Collapsed(panel) end) end
pcall(function() ui:TryClose() end)
if ui:IsOpen() then pcall(function() UI.Close(ui) end) end
end
local top = nil
if type(UI.GetTop) == [[function]] then top = UI.GetTop() end
if top ~= nil and top:IsVisible() ~= true then
pcall(function() WidgetUtils.SelfHitTestInvisible(top) end)
end
if type(PalClient) == [[table]] then PalClient.CanOpenMap = true end
pcall(function()
local pc = UE4.UGameplayStatics.GetPlayerController(GetGameIns(), 0)
if pc ~= nil then pc.bIgnoreEsc = false end
end)
end)
end
local function palTick()
local busy = false
pcall(function()
local ui = UI.GetUI([[BuildPop]])
if ui ~= nil and ui:IsOpen() and ui.bConfirm == true then busy = true end
end)
if busy then
MikuPalBusy = MikuPalBusy - (-1)
if MikuPalBusy == 2 then
palFix()
MikuPalBusy = 0
end
else
MikuPalBusy = 0
end
UE4.Timer.Add(3, palTick)
end
UE4.Timer.Add(3, palTick)
end"""

for ch in ['\\', '"', '<', '>', '&', '+']:
    assert ch not in LUA, "bad char " + ch

lit = LUA.replace('\n', '\\n')

p = r'D:\DSH\s1build\S1Admin\DlcClientPatches.cs'
s = io.open(p, encoding='utf-8').read()

pat = re.compile(r'\t\t\t"PalBuildSeqFallback" => "[^"]*",\n')
m = pat.search(s)
assert m, 'entry not found'
entry = '\t\t\t"PalBuildSeqFallback" => "' + lit + '",\n'
s = s[:m.start()] + entry + s[m.end():]
io.open(p, 'w', encoding='utf-8').write(s)
print('rewritten, size', len(s))
