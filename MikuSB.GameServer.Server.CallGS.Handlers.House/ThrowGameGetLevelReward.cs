using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ThrowGameGetLevelReward")]
public class ThrowGameGetLevelReward : IHouseFuncHandler
{
	private const uint HouseBeachInfoStart = 17000u;

	private const uint CurLevelOffset = 3u;

	private const uint RewardStateOffset = 20u;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int requested = Math.Max(0, HouseJson.NumField(root, "Level"));
		bool all = root != null && root["bAll"]?.GetValue<bool>() == true;
		int curLevel = Math.Max(1, (int)HouseAttr.Read(player, 17003u));
		List<int> levels;
		if (all)
		{
			levels = Enumerable.Range(1, curLevel).ToList();
		}
		else
		{
			levels = ((requested > 0 && requested <= curLevel) ? new List<int> { requested } : new List<int>());
		}
		JsonArray granted = new JsonArray();
		foreach (int level in levels)
		{
			uint stateSid = (uint)(17020 + level);
			if (HouseAttr.Read(player, stateSid) != 0)
			{
				continue;
			}
			List<uint> reward = LoadReward(level);
			if (reward != null)
			{
				await HouseAttr.SetAsync(connection, stateSid, 1u, sync);
				if (reward.Count >= 5)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
				}
				JsonNode[] array = reward.Select((uint x) => JsonValue.Create(x)).ToArray();
				granted.Add(new JsonArray((ReadOnlySpan<JsonNode?>)array));
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ThrowGameGetReward",
			["Level"] = requested,
			["nLevel"] = requested,
			["bReceived"] = true,
			["tbRewards"] = granted,
			["tbReward"] = ((granted.Count != 1) ? new JsonArray() : granted[0]?.DeepClone())
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	private static List<uint>? LoadReward(int level)
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "Throw";
			buffer[4] = "ThrowAward.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return null;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
			{
				if (!(item is JsonObject jsonObject) || HouseJson.NumField(jsonObject, "Level") != level)
				{
					continue;
				}
				if (!(jsonObject["Reward"] is JsonArray source))
				{
					return null;
				}
				return source.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList();
			}
		}
		catch
		{
		}
		return null;
	}
}
