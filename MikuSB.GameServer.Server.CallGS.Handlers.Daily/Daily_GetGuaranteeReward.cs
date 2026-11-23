using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_GetGuaranteeReward")]
public class Daily_GetGuaranteeReward : ICallGSHandler
{
	private const uint GuaranteeGroupId = 102u;

	private const uint CounterMask = 65535u;

	private const int SuitShift = 16;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["Type"]);
		if (levelId == 0 || !GameData.DailyLevelData.TryGetValue(levelId, out DailyLevelExcel level) || level.Guarantee == 0)
		{
			await CallGSRouter.SendScript(connection, "Daily_GetGuaranteeReward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(102u, levelId);
		uint counter = attr.Val & 0xFFFF;
		uint suit = ReadUint(request["Suit"]);
		if (suit == 0)
		{
			suit = (attr.Val >> 16) & 0xFFFF;
		}
		if (counter < level.Guarantee)
		{
			await CallGSRouter.SendScript(connection, "Daily_GetGuaranteeReward", "{\"sError\":\"tip.guarantee_not_enough\"}");
			return;
		}
		SupportCardExcel card = PickSuitCard(suit);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "Daily_GetGuaranteeReward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		List<uint> rewardRow = new List<uint> { card.Genre, card.Detail, card.Particular, card.Level, 1u };
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(new List<IReadOnlyList<uint>> { rewardRow }, sync);
		attr.Val &= 4294901760u;
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		JsonArray item = new JsonArray();
		foreach (JsonNode item2 in granted)
		{
			item.Add(item2?.DeepClone());
		}
		await CallGSRouter.SendScript(connection, "Daily_GetGuaranteeReward", new JsonObject
		{
			["SuitId"] = suit,
			["tbItem"] = new JsonArray { item }
		}.ToJsonString(), sync);
	}

	private static SupportCardExcel? PickSuitCard(uint suit)
	{
		if (suit == 0)
		{
			return null;
		}
		List<SupportCardExcel> list = GameData.SupportCardData.Where((SupportCardExcel x) => x.Particular == suit).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		return list[Random.Shared.Next(list.Count)];
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
