using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher）等级奖励领取（2026-11-13 重写，修复记录第 267 节）。
///
/// 客户端链路（反汇编 UMG.House.HouseArcade.umg_arcade_level / HouseArcadeToSeverMsg 得到）：
///   单级「领取」→ GetReward(level) → 发 ArcadeGameGetLevelReward { FuncName, nLevel = level }
///   一键领取    → GetRewardAll()   → 同一个请求，nLevel = -1
/// 回包由 HouseArcadeToSeverMsg:GetRewardRsp 处理：先看 sErr，再把 tbReward 交给 Item.Gain
/// 弹「获得道具」，最后 Call2(HouseArcadeReward, GetRewardRsp) 重画奖励页。
///
/// 旧实现的两个毛病：
///   1. 读 Resources/house/coinpusher_rewards.json —— 文件其实在 Resources/house/coinpusher/ 下，
///      永远拿到 null，于是一个道具都不发；
///   2. nLevel = -1（一键领取）被 Math.Max(1, …) 掰成 1，而且没有「全部领取」的分支。
/// 现在按「累计经验 → 当前等级」换算（与客户端 GetLevel 同口径），nLevel &lt; 0 时把
/// 1~当前等级里没领过的全发一遍。
/// </summary>
[HouseFunc("ArcadeGameGetLevelReward")]
public class ArcadeGameGetLevelReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int requested = HouseJson.NumField(root, "nLevel");
		if (requested == 0)
		{
			requested = HouseJson.NumField(root, "Level");
		}
		int exp = ArcadeGameHelper.ReadExp(player);
		int curLevel = ArcadeGameHelper.LevelFromExp(exp);
		List<int> targets = new List<int>();
		if (requested < 0)
		{
			for (int level = 1; level <= curLevel; level++)
			{
				targets.Add(level);
			}
		}
		else if (requested > 0 && requested <= curLevel)
		{
			targets.Add(requested);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		uint gotValue = HouseAttr.Read(player, ArcadeGameHelper.LevelRewardGotSid);
		foreach (int level in targets)
		{
			if (((gotValue >> level) & 1u) != 0)
			{
				continue;
			}
			List<uint> reward = ArcadeGameHelper.LoadReward(level);
			if (reward == null || reward.Count < 5 || reward[0] == 0)
			{
				continue;
			}
			gotValue |= (uint)(1 << level);
			await HouseAttr.SetAsync(connection, ArcadeGameHelper.LevelRewardGotSid, gotValue, sync);
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			granted.Add(ArcadeGameHelper.ToJson(reward));
		}
		if (granted.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		ArcadeGameHelper.SyncGot(player, sync, gotValue);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ArcadeGameGetLevelReward",
			["nLevel"] = requested,
			["Level"] = requested,
			["bReceived"] = granted.Count > 0,
			["nCurLevel"] = curLevel,
			["nCurExp"] = exp,
			["tbReward"] = granted.DeepClone(),
			["tbRewards"] = granted.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
