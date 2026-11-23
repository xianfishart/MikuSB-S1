using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GetMassageLevelReward")]
public class GetMassageLevelReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		// 2026-11-13 改（修复记录第 272 节）：等级上限 15（MassageLevelPoint 只有 15 档），
		// 奖励配置在 Resources/house/massage/，一个等级可能给多件物品。
		int level = Math.Clamp(HouseJson.NumField(root, "Level"), 1, HouseMassageHelper.MaxLevel);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GetMassageLevelReward",
			["tbReward"] = new JsonArray()
		};
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<List<uint>> items = HouseMassageHelper.LevelRewardItems(level);
		PlayerInstance? player = connection.Player;
		if (player != null && items.Count != 0)
		{
			// 客户端 IsMassageLevelRewardGot：10000 + HouseMassageLevelRewardStart(3) + floor(level/30)，第 level%30 位
			uint sid = HouseMassageHelper.LevelRewardStartSid(level);
			int bit = level % 30;
			uint value = player.Attributes.GetValue(101u, sid);
			if (((value >> bit) & 1) == 0)
			{
				value |= (uint)(1 << bit);
				player.Attributes.Set(101u, sid, value);
				player.Attributes.SyncTo(sync, 101u, sid, value);
				sync.Custom[player.ToPackedAttrKey(101u, sid)] = value;
				sync.Custom[player.ToShiftedAttrKey(101u, sid)] = value;
				JsonArray granted = new JsonArray();
				foreach (List<uint> item in items)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, item);
					JsonNode[] row = new JsonNode[item.Count];
					for (int i = 0; i < item.Count; i++)
					{
						row[i] = JsonValue.Create(item[i]);
					}
					granted.Add(new JsonArray(row));
				}
				rsp["tbReward"] = granted;
				DatabaseHelper.SaveDatabaseType(player.Data);
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
				HouseMassageHelper.Log($"claim level reward uid={player.Uid} level={level} items={granted.Count}");
			}
		}
		// 2026-11-13 追加（修复记录第 271 节）：顺带补齐 20702「完成一次神经调理」的进度，
		// 老存档调理过但成就没标记的，点一次领取就会补上。
		await HouseMassageAttr.MarkAchievementIfNeededAsync(connection, sync);
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), sync);
	}
}
