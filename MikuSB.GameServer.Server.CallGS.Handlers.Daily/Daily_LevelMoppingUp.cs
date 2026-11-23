using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Chapter;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_LevelMoppingUp")]
public class Daily_LevelMoppingUp : ICallGSHandler
{
	private const uint GuaranteeGroupId = 102u;

	private const uint GuaranteeCounterMask = 65535u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null || !(JsonNode.Parse(param) is JsonObject request))
		{
			return;
		}
		int levelId = ReadInt(request["nLevelID"]);
		int multiple = Math.Clamp(ReadInt(request["nMultiple"]), 1, 9);
		if (levelId <= 0 || !GameData.DailyLevelData.TryGetValue((uint)levelId, out DailyLevelExcel config))
		{
			await CallGSRouter.SendScript(connection, "Daily_LevelMoppingUp", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 速战按次扣体力（修复记录第 182 节）：不足则直接中止，不发奖励。
		uint vigorCost = config.ConsumeVigor() * (uint)multiple;
		if (!await ChapterVigor.ConsumeCost(connection, player, vigorCost, sync, "Daily_LevelMoppingUp"))
		{
			return;
		}
		JsonArray awards = new JsonArray();
		JsonArray merged = new JsonArray();
		for (int i = 0; i < multiple; i++)
		{
			foreach (JsonNode item in await player.RewardManager.GrantLevelRewardsAsync(config, isFirstClear: false, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync))
			{
				merged.Add(item?.DeepClone());
			}
		}
		if (merged.Count > 0)
		{
			awards.Add(merged);
		}
		PlayerAttr pass = player.Attributes.GetOrCreate(22u, (uint)levelId);
		pass.Val += (uint)multiple;
		sync.Custom[player.ToPackedAttrKey(22u, (uint)levelId)] = pass.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, (uint)levelId)] = pass.Val;
		if (config.Guarantee != 0)
		{
			PlayerAttr guarantee = player.Attributes.GetOrCreate(102u, (uint)levelId);
			uint counter = guarantee.Val & 0xFFFF;
			if (counter < config.Guarantee)
			{
				uint next = Math.Min(counter + (uint)multiple, config.Guarantee);
				guarantee.Val = (guarantee.Val & 0xFFFF0000u) | next;
				sync.Custom[player.ToPackedAttrKey(102u, (uint)levelId)] = guarantee.Val;
				sync.Custom[player.ToShiftedAttrKey(102u, (uint)levelId)] = guarantee.Val;
			}
		}
		// 第 415 节：速战同样算日常 —— 「消耗感知 N 点」按实际扣的体力累加，
		// 「完成任意关卡 N 次」按速战把数累加；以前这两类任务只有真打一局才动。
		TaskEventService.OnLevelClearMany(player, (uint)levelId, multiple);
		TaskEventService.AddByType(player, 4, (int)vigorCost);
		TaskEventService.Flush(player);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject response = new JsonObject
		{
			["nLevelID"] = levelId,
			["nTeamID"] = ReadInt(request["nTeamID"]),
			["nMultiple"] = multiple,
			["tbAward"] = awards
		};
		await CallGSRouter.SendScript(connection, "Daily_LevelMoppingUp", response.ToJsonString(), sync);
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
