using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseRequestScript
{
	internal static readonly Dictionary<string, string> FuncNameMap = new Dictionary<string, string>
	{
		["ChangeGirlBeachSuitId"] = "ChangeGirlBeachSuitIdSuccess",
		["ChangeNpcSuitByAreaId"] = "ChangeNpcSuitByAreaIdRsp",
		["SetGroupFurIndex"] = "SetGroupFurSuccess",
		["RouletteEnd"] = "RouletteEndRsp",
		["UnLockWaterGunGameItem"] = "UnLockWaterGunGameItemRsp",
		["ChangeNpcSuit"] = "ChangeNpcSuitSuccess",
		["GiveGiftToArea"] = "GiveGiftToAreaSuccess",
		["BuyRefreshMapTimes"] = "BuyRefreshMapTimesSuccess",
		["CompleteHousePuzzleTask"] = "CompleteHousePuzzleTaskSuccess",
		["CompleteStarWishTask"] = "CompleteStarWishTaskSuccess",
		["SetPlayerRingInfo"] = "SetPlayerRingInfoSuccess",
		["ReadGirlLoveStory"] = "ReadGirlLoveStorySuccess",
		["GirlRoomChange"] = "GirlRoomChangeSuccess",
		["GirlRegister"] = "GirlRegisterSuccess",
		["ThrowGameGetLevelReward"] = "ThrowGameGetReward",
		["ThrowGameGetAchReward"] = "ThrowGameGetReward",
		// 2026-11-12 追加（修复记录第 265 节）：冰趣蜜弹（ThrowGame）教程打完卡加载。
		// 运行时探针读到客户端回调表 HouseMessageHandle 里只有 ThrowTutorialFinish，
		// **没有** ThrowGameTutorialFinish；请求名带 Game、回调名不带，回显原名就匹配不上，
		// 回调不执行 → Connection 遮罩不关。这条映射把回包名改回 ThrowTutorialFinish。
		["ThrowGameTutorialFinish"] = "ThrowTutorialFinish",
		// 2026-11-13 追加（修复记录第 286 节）：客户端回调表里领基地等级奖励的回包叫
		// GetHouseLevelRewardSuccess（探针导出 HouseMessageHandle 的 key 确认），
		// 作用是 UI.GetUI("DormLoveInfo"):UpdateRewardState(rsp)。原来回显请求名
		// GetHouseLevelReward → 客户端找不到回调 → 点了没有即时反馈，只能等属性同步慢慢变灰。
		["GetHouseLevelReward"] = "GetHouseLevelRewardSuccess"
	};

	internal static string Synthesize(JsonObject request)
	{
		string text = request["FuncName"]?.GetValue<string>();
		JsonObject jsonObject = CreateSuccessObject();
		foreach (var (text3, jsonNode2) in request)
		{
			if ((!(text3 == "FuncName") && !(text3 == "tblog")) || 1 == 0)
			{
				jsonObject[text3] = jsonNode2?.DeepClone();
			}
		}
		if (!string.IsNullOrEmpty(text))
		{
			jsonObject["FuncName"] = (FuncNameMap.TryGetValue(text, out string value) ? value : text);
		}
		return jsonObject.ToJsonString();
	}

	internal static string Success(JsonObject? extra = null)
	{
		return CreateSuccessObject(extra).ToJsonString();
	}

	internal static JsonObject CreateSuccessObject(JsonObject? extra = null)
	{
		JsonObject jsonObject = new JsonObject
		{
			["bSuccess"] = true,
			["nResult"] = 0
		};
		if (extra != null)
		{
			foreach (var (propertyName, jsonNode2) in extra)
			{
				jsonObject[propertyName] = jsonNode2?.DeepClone();
			}
		}
		return jsonObject;
	}
}
