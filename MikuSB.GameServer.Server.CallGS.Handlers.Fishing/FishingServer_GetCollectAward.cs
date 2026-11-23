using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_GetCollectAward")]
public class FishingServer_GetCollectAward : ICallGSHandler
{
	private const uint Gid = 32u;

	private const uint FishBookBase = 20000u;

	private const uint RikiFlagBase = 20u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		List<uint> ids = ReadIds(request["tbIdList"]);
		if (ids.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_GetCollectAward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		uint stars = CountBookStars(player);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (uint id in ids)
		{
			if (!GameData.FishingRikiAwardData.TryGetValue(id, out FishingRikiAwardExcel row) || stars < row.Point)
			{
				continue;
			}
			uint flagSid = id / 30 + 20;
			int bit = (int)(id % 30);
			PlayerAttr flag = player.Attributes.GetOrCreate(32u, flagSid);
			if (((flag.Val >> bit) & 1) != 0)
			{
				continue;
			}
			List<List<uint>> rows = RewardText.Parse(row.Rewards);
			if (rows.Count == 0)
			{
				continue;
			}
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
			{
				granted.Add(item?.DeepClone());
			}
			flag.Val |= (uint)(1 << bit);
			player.Attributes.SyncTo(sync, flag);
			row = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "FishingServer_GetCollectAward", new JsonObject { ["tbAwardList"] = granted }.ToJsonString(), sync);
	}

	private static uint CountBookStars(PlayerInstance player)
	{
		uint num = 0u;
		foreach (FishingFishExcel value in GameData.FishingFishData.Values)
		{
			uint attr = player.GetAttr(32u, 20000 + value.Id * 10 + 1);
			uint num2 = ((value.RikiMaxStar == 0) ? attr : Math.Min(attr, value.RikiMaxStar));
			num += num2;
		}
		return num;
	}

	private static List<uint> ReadIds(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			uint num = ((item is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value)) ? value : (uint.TryParse(item?.ToString(), out var result) ? result : 0u));
			if (num != 0 && !list.Contains(num))
			{
				list.Add(num);
			}
		}
		return list;
	}
}
