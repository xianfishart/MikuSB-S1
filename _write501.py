# -*- coding: utf-8 -*-
text = """
================================================================================
501. 蓝图岛「建筑建造/升级完成后卡死」—— 施工面板收尾回调不执行
    （2026-11-22 11:15）
================================================================================

  现象
    在蓝图岛点建筑建造/升级，服务端已经成功（161 等级 +1），但客户端卡住：
    HUD 全没了（小地图、左侧任务、血条、技能栏都不显示）、角色不能动、
    鼠标指针还在、屏幕上留一层半透明暗色遮罩，按 ESC 也退不出去。

  排查
    1. Server.log：UpgradeBuild 正常返回（build 1 0->1 / 1->2），
       之后 10 分钟客户端再没发过任何 CallGS —— 不是网络问题，是客户端 UI 卡住。
    2. Game.log 时间线：
         10:35:25.365 OpenInner Connection      <- VirCaptureBuild:ReqUpgradeBuild 的 ShowConnection
         10:35:25.463 Close connection          <- rsp 到达，回调第一句 UI.CloseConnection()
         10:35:25.474 OpenInner BuildPop        <- 回调里 PalManage.TryPop{nType=15} 打开施工面板
       之后就再也没有任何 UI 事件（连 UI.Close 都没有）。
    3. 反汇编 s2_tbCmds_VirCaptureBuild_UpgradeBuild（客户端 rsp 回调）：
         UI.CloseConnection()
         local lv = VirCaptureBuild:GetBuildLevel(nId)
         if lv > 1 then
             EventSystem:Trigger(Event.NotifyBuildingLevelUp, nId, lv)
             if PalManage.CheckCurrentMissionGetAward() then PalClient.TryUpdateCurMission()
             else PalManage.TryPop{ nType = 15, nBuildId = nId, pCallBack = ... } end
         end
       pCallBack 的内容：Trigger(NotifyBuildingLevelUp) -> WidgetUtils.Collapsed(UI.GetTop())
       -> PalManage.PlayBuildingSeq(nId, function() SelfHitTestInvisible(UI.GetTop())
          + UI.SafeOpen("BuiltSuccess", nId) end)
       也就是说：HUD 被折叠、等收尾回调恢复，全看这条链能不能走完。
    4. 反汇编 UMG/VirCapture/umg_vircapture_pop_all.lua：
         UpdateData(data)  ->  nType==15 时调 InitConstructionPanel(nBuildId, panel)
         InitConstructionPanel(584-608)：
             panel:UnbindAllFromAnimationFinished("loop")
             panel:BindToAnimationEVent(..., EWidgetAnimationEvent.Finished, ...)
             panel:PlayAnimation(panel.loop)
             UE4.Timer.Add(2.5, function()
                 Player.bIgnoreEsc = false
                 WidgetUtils.Collapsed(panel)
                 self:TryClose()          <- 关面板、清 bConfirm、触发 pCallBack
             end)
    5. 现场实测（MikuEval 探针）：
         UI.GetUI("BuildPop"):IsOpen() = true，bConfirm = true（一直没被清），
         pCallBack 还是 function，PopPanel[15]（uw_build_construction_pop / "Construction"）
         Visibility = 1（Collapsed），UI.GetTop()（fight）IsVisible = false。
       bConfirm 只有 TryClose 才会清成 nil —— 说明那个 2.5 秒收尾回调没有跑完，
       面板永远停在「已在施工、等收尾」的状态，玩家看着就是黑屏卡死。
       （单独验证过：WidgetUtils.Collapsed/SelfHitTestInvisible/UI.Close、
         UE4.Timer.Add 本身都正常，PlayerController 的 Cast 也成功，
         所以不是某个调用直接崩，而是这条收尾链在实机环境下不可靠。）

  改了什么（客户端补丁 DlcClientPatches.PalBuildSeqFallback，第 501 节）
    · 起一个 3 秒周期的看门狗（UE4.Timer.Add 自递归）：
        如果 UI.GetUI("BuildPop"):IsOpen() 且 BuildPop.bConfirm == true 连续两次（约 6 秒），
        就认为收尾链断了，强制收尾：
          - 清 pCallBack / pCancel / bConfirm
          - WidgetUtils.Collapsed(PopPanel[15])
          - BuildPop:TryClose()，还开着一律 UI.Close
          - HUD 恢复：fight 不可见时 SelfHitTestInvisible(fight)
          - 交互恢复：PalClient.CanOpenMap = true、pc.bIgnoreEsc = false
    · 判据只用 bConfirm == true：这个字段只有进入「施工」流程（InitConstructionPanel）
      才会置 true，正常状态最长 2.5 秒就会清掉，所以不会误关玩家正常打开的建造列表。
    · 只看功能：强关之后不会播建造序列、也不会弹「建造成功」界面
      （序列回调在实机里本来就不可靠，宁可跳过表现也不能锁死）。

  实测（真实界面）
    · 建筑 1 酒店：lv1 -> lv2、lv3 -> lv4 各升一次，
      服务端日志 build 1 -> lv3 / lv4 正常，客户端 HUD 全部保留、角色可操作、
      不再出现暗色遮罩（对比修复前：同一条路径必卡）。
    · 建筑 2 食堂被解锁条件拦住（buildingcondition #3 = 任务 116 未完成），
      服务端回 error.condition_limit，客户端正常弹提示、不卡。

  部署
    MikuGameServer.dll md5 780d2a0284d5e36f2fd126db9910e5ca（3,127,296 字节）
    备份 .bak-501（上一版 fb97e249…，只加了 PalBuildSeqFallback 的 v1 版本，看门狗还没做）

  备忘
    · 客户端补丁靠登录/切期时 NtfScript 下发，改完必须重启 MikuSB.exe
      并让客户端重新登录（dlcpatches.log 里显示「客户端补丁 68/68」才说明带上了新条目）。
    · 想在不重启客户端的前提下修现场：写 D:/DSH/.tools/eval.lua 调
      UI.GetUI("BuildPop") 清 bConfirm + TryClose + SelfHitTestInvisible(fight) 能立刻恢复，
      但治标不治本，下次建造还会卡。
    · 还没验证「首次建造（161 从 0 到 1）」这条路径：现存 10 栋建筑里，
      除 1 号酒店外都要前置任务（后续任务还没推到），暂时造不了第二栋。
      如果玩家实际点第一栋时仍然卡，说明首次建造没走 TryPop（bConfirm 不会为 true），
      那就把看门狗判据放宽成「BuildPop 开 且 fight 不可见」，但要先确认正常打开
      建造列表时 fight 是可见的，避免误关面板。
"""
p = r'D:\软件商店下载位置\PC修复记录\修复更新包\MikuSB-修复记录-2026-11-02.txt'
import io
with io.open(p, 'a', encoding='utf-8') as f:
    f.write(text)
print('appended', len(text))
