using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_LevelSettlement")]
public class VirCaptureLevel_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["nID"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		if (levelId != 0)
		{
			bool wasPassed = player.IsLevelReallyPassed(levelId);
			if (GameData.VirCaptureLevelData.TryGetValue(levelId, out VirCaptureLevelExcel config))
			{
				await player.RewardManager.GrantLevelRewardsAsync(config, !wasPassed, now, sync);
			}
			uint star = ReadUint(request["nStar"]);
			if (star != 0)
			{
				PlayerAttr starAttr = player.Attributes.GetOrCreate(21u, levelId);
				starAttr.Val |= (uint)((star >= 3) ? 7 : ((1 << (int)star) - 1));
				player.Attributes.SyncTo(sync, starAttr);
			}
			PlayerAttr pass = player.Attributes.GetOrCreate(22u, levelId);
			pass.Val = Math.Max(1u, pass.Val + 1);
			player.Attributes.SyncTo(sync, pass);
			// 开拓目标：Type=2「闯关吧指定关卡通关」按 ClearedLevels 判断
			VirCaptureMissionCounterStore.MarkLevelCleared((int)player.Uid, levelId);
			VirCaptureMissionCounterStore.Save((int)player.Uid);
			VirCaptureMissionProgress.SyncAll(player, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_LevelSettlement", "{}", sync);
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
