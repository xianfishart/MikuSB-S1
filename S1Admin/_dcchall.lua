local function mikuDcChall()
if type(_G.DreamCardConf) ~= [[table]] then return end
local tb = DreamCardConf.tbChallTimeConf
if type(tb) ~= [[table]] then return end
local last = tb[#tb]
if type(last) ~= [[table]] then return end
local now = GetTime()
if type(now) ~= [[number]] or now == 0 then return end
if tonumber(last.nEndTime) == 4102444800 then return end
last.nStartTime = now - 86400
last.nEndTime = 4102444800
end
pcall(mikuDcChall)
if type(UI) == [[table]] and type(UI.Open) == [[function]] and _G.bMikuDcChall ~= true then
_G.bMikuDcChall = true
local oldOpen = UI.Open
UI.Open = function(...) pcall(mikuDcChall) local r = table.pack(oldOpen(...)) pcall(mikuDcChall) return table.unpack(r, 1, r.n) end
end
