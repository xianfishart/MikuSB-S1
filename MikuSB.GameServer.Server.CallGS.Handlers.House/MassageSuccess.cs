using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("MassageSuccess")]
public class MassageSuccess : IHouseFuncHandler
{
	private static readonly Logger Logger = new Logger("HouseMassage");

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		JsonObject rsp = new JsonObject
		{
			["bSuccess"] = true,
			["nResult"] = 0
		};
		foreach (var (key, value) in root)
		{
			if (!(key == "tbLeftPoint"))
			{
				rsp[key] = value?.DeepClone();
			}
		}
		rsp["FuncName"] = "MassageSuccess";
		bool isSuccess = true;
		JsonNode jsonNode2 = root["IsSuccess"];
		if (jsonNode2 is JsonValue sv && sv.TryGetValue<bool>(out var b))
		{
			isSuccess = b;
		}
		int star = HouseJson.NumField(root, "Star");
		int hardLevel = HouseJson.NumField(root, "HardLevel");
		rsp["IsSuccess"] = isSuccess || star > 0;
		if (rsp["HisTime"] == null)
		{
			rsp["HisTime"] = 0;
		}
		if (rsp["EvaRandId"] == null)
		{
			rsp["EvaRandId"] = 1;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int starClamped = Math.Clamp(star, 0, 5);
		int lvl = ((hardLevel < 1 || hardLevel > 3) ? 1 : hardLevel);
		uint timesAfter = 0u;
		if (player != null)
		{
			int girlId = HouseJson.NumField(root, "GirlId");
			if (girlId > 0)
			{
				int starOffset = HouseMassageAttr.StarsOffset(lvl);
				uint oldStar = HouseMassageAttr.Read(player, girlId, starOffset);
				if ((uint)starClamped > oldStar)
				{
					HouseMassageAttr.Set(player, sync, girlId, starOffset, (uint)starClamped);
				}
				int timesOffset = HouseMassageAttr.TimesOffset(lvl);
				uint oldTimes = HouseMassageAttr.ReadTimes(player, girlId, timesOffset);
				timesAfter = oldTimes + 1;
				HouseMassageAttr.SetTimes(player, sync, girlId, timesOffset, timesAfter);
				int wantUnlock = ((starClamped >= 5) ? Math.Min(3, lvl + 1) : lvl);
				uint oldUnlock = HouseMassageAttr.Read(player, girlId, 39);
				if ((uint)wantUnlock > oldUnlock)
				{
					HouseMassageAttr.Set(player, sync, girlId, 39, (uint)wantUnlock);
				}
				Logger.Info($"[Massage] girl={girlId} lvl={lvl} star={starClamped} unlock {oldUnlock}->{Math.Max((uint)wantUnlock, oldUnlock)} times {oldTimes}->{timesAfter}");
				// 2026-11-13 追加（修复记录第 272 节）：
				// ① 客户端判定「五星达成奖励可领」读的是 24+难度 的第 0~2 位（高位是次数），
				//    旧实现只写了 31+难度（展示用的疗愈评级），这里两处都写。
				// ② 调理经验以前从来没写过 → 等级页永远 0/400。经验加值取
				//    massage_difficulty.json 的 AddExp[星级-1]，并按 massage_level_reward.json
				//    的 Exp 阈值逐级升级。
				HouseMassageHelper.SetStarsInTimesField(player, sync, girlId, lvl, starClamped);
				int addExp = HouseMassageHelper.AddExpFor(lvl, starClamped);
				(int Level, int Exp, int Added) levelInfo = HouseMassageHelper.AddExp(player, sync, addExp);
				rsp["nAddExp"] = addExp;
				rsp["nNowLevel"] = levelInfo.Level;
				rsp["nNowExp"] = levelInfo.Exp;
				HouseMassageHelper.Log($"session uid={connection.Player?.Uid} girl={girlId} lvl={lvl} star={starClamped} +exp {addExp} -> lv{levelInfo.Level} {levelInfo.Exp}");
				// ③ 五星达成奖励：客户端点击链路在本服一直发不出请求，服务端直接补发并置已领位，
				//    避免玩家卡在「看得到领不到」。
				if (starClamped >= 5)
				{
					await HouseMassageHelper.GrantStarRewardAsync(connection, sync, girlId, lvl, force: false);
				}
			}
		}
		bool unlockNext = star >= 5 && hardLevel >= 1 && hardLevel < 3;
		rsp["HasUnLockNewHardLevel"] = unlockNext;
		rsp["MassResStar"] = star;
		rsp["NowMassageTimes"] = timesAfter;
		// 2026-11-13 追加（修复记录第 271 节）：20702「完成一次神经调理」是 Type 335 的预览成就，
		// 存在 activity_quest.json 里，读 Gid7/Sid=20702 的 Val（bit1 起是进度、bit0 是已领）。
		// 服务端以前从来不写这条进度，成就页一直灰着，所以「成就奖励无法领取」。
		// 这里只标完成，奖励仍然由玩家在成就页领取。
		if (connection.Player != null)
		{
			await HouseAchievementClaim.MarkCompleteAsync(connection, 20702, sync);
		}
		Logger.Info($"MassageSuccess uid={connection.Player?.Uid} star={star} hard={hardLevel} unlock={unlockNext} -> {rsp.ToJsonString()}");
		string[] names = new string[5] { "MassageSuccessRsp", "MassageSuccess", "MassageGameOver", "MassageGameOverRsp", "GameOverRsp" };
		for (int i = 0; i < names.Length; i++)
		{
			rsp["FuncName"] = names[i];
			await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), (i == 0) ? sync : null);
			await Task.Delay(120);
		}
	}
}
