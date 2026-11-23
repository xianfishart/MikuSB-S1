using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameGetReward")]
public class PubGameGetReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint exp = player.GetAttr(101u, PubAttr.Sid(1u));
		uint currentLevel = PubAttr.ResolveLevel(exp);
		int selected = PubAttr.Num(root["nLevel"]);
		List<uint> levels = new List<uint>();
		if (selected <= 0)
		{
			for (uint level = 1u; level <= currentLevel; level++)
			{
				if (!PubAttr.CheckLevelRewardGot(player, level))
				{
					levels.Add(level);
				}
			}
		}
		else if ((uint)selected <= currentLevel && !PubAttr.CheckLevelRewardGot(player, (uint)selected))
		{
			levels.Add((uint)selected);
		}
		JsonArray granted = new JsonArray();
		JsonArray gotLevel = new JsonArray();
		Dictionary<uint, (uint Exp, List<uint> Reward)> ladder = PubAttr.Ladder();
		foreach (uint level2 in levels)
		{
			if (!ladder.TryGetValue(level2, out var conf) || conf.Reward.Count < 5)
			{
				continue;
			}
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, conf.Reward);
			JsonArray row = new JsonArray();
			foreach (uint v in conf.Reward)
			{
				row.Add(v);
			}
			granted.Add(row);
			await PubAttr.SetLevelRewardGotAsync(connection, player, level2, sync);
			gotLevel.Add((int)level2);
			conf = default;
		}
		if (levels.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "PubGameGetReward",
			["tbReward"] = granted,
			["tbGetLevel"] = gotLevel,
			["nLevel"] = (int)currentLevel
		};
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), sync);
	}
}
