using System;
using System.Linq;
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

namespace MikuSB.GameServer.Server.CallGS.Handlers.Role;

[CallGSApi("Role_LevelMoppingUp")]
public class Role_LevelMoppingUp : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject request = (JsonNode.Parse(param) as JsonObject) ?? new JsonObject();
		int levelId = ReadInt(request["nID"]);
		int roleId = ReadInt(request["nRoleID"]);
		int multiple = Math.Clamp(ReadInt(request["nMultiple"]), 1, 9);
		if (!GameData.RoleLevelData.TryGetValue((uint)levelId, out RoleLevelExcel levelConfig))
		{
			await CallGSRouter.SendScript(connection, "Role_LevelMoppingUp", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 扫荡按次扣体力（修复记录第 182 节）：不足则直接中止，不发奖励。
		uint vigorCost = levelConfig.ConsumeVigor() * (uint)multiple;
		if (!await ChapterVigor.ConsumeCost(connection, player, vigorCost, sync, "Role_LevelMoppingUp"))
		{
			return;
		}
		JsonArray awards = new JsonArray();
		JsonArray group = new JsonArray();
		uint seedBase = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		for (int i = 0; i < multiple; i++)
		{
			foreach (JsonNode item in await player.RewardManager.GrantLevelRewardsAsync(levelConfig, isFirstClear: false, seedBase + (uint)i, sync))
			{
				group.Add(item?.DeepClone());
			}
		}
		if (group.Count > 0)
		{
			awards.Add(group);
		}
		PlayerAttr pass = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == (uint)levelId);
		if (pass == null)
		{
			pass = new PlayerAttr
			{
				Gid = 22u,
				Sid = (uint)levelId,
				Val = 1u
			};
			player.Data.Attrs.Add(pass);
		}
		else
		{
			pass.Val++;
		}
		sync.Custom[player.ToPackedAttrKey(22u, (uint)levelId)] = pass.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, (uint)levelId)] = pass.Val;
		// 第 415 节：个人故事速战也算日常/成就的「通关个人故事 N 次」和消耗感知。
		TaskEventService.OnLevelClearMany(player, (uint)levelId, multiple);
		TaskEventService.AddByType(player, 4, (int)vigorCost);
		TaskEventService.Flush(player);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject response = new JsonObject
		{
			["nLevelID"] = levelId,
			["nTeamID"] = roleId,
			["nMultiple"] = multiple,
			["tbAward"] = awards
		};
		await CallGSRouter.SendScript(connection, "Role_LevelMoppingUp", response.ToJsonString(), sync);
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
