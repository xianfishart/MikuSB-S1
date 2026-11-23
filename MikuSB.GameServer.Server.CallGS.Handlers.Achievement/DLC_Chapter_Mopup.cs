using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Chapter;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("DLC_Chapter_Mopup")]
public class DLC_Chapter_Mopup : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int levelId = ParseLevelId(param);
		int multiple = Math.Clamp(ParseMultiple(param), 1, 9);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray awards = new JsonArray();
		if (levelId > 0)
		{
			if (GameData.DlcChapterLevelData.TryGetValue((uint)levelId, out DlcChapterLevelExcel config))
			{
				// DLC 关卡速战按次扣体力（修复记录第 182 节）：不足则中止，不发奖励。
				uint vigorCost = config.ConsumeVigor() * (uint)multiple;
				if (!await ChapterVigor.ConsumeCost(connection, player, vigorCost, sync, "DLC_Chapter_Mopup"))
				{
					return;
				}
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
			}
			foreach (JsonObject row in DlcMissionStore.ActiveRows())
			{
				if (DlcMissionStore.ReadInt(row, "Type") == 2 && ParamContains(row, levelId))
				{
					int missionId = DlcMissionStore.ReadInt(row, "MissionID");
					if (missionId > 0)
					{
						MarkCompleted(player, sync, missionId, DlcMissionStore.ReadInt(row, "CheckValue"));
					}
				}
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		await CallGSRouter.SendScript(connection, "DLC_Chapter_Mopup", new JsonObject
		{
			["nID"] = levelId,
			["nMultiple"] = multiple,
			["tbAward"] = awards
		}.ToJsonString(), sync);
	}

	private static int ParseMultiple(string param)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			JsonElement value;
			return (!jsonDocument.RootElement.TryGetProperty("nMultiple", out value)) ? 1 : value.GetInt32();
		}
		catch
		{
			return 1;
		}
	}

	private static void MarkCompleted(PlayerInstance player, NtfSyncPlayer sync, int missionId, int checkValue)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 14 && x.Sid == (uint)missionId);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 14u,
				Sid = (uint)missionId,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = (playerAttr.Val & 1) | (uint)(Math.Clamp(checkValue, 0, int.MaxValue) << 1);
		sync.Custom[player.ToPackedAttrKey(14u, (uint)missionId)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(14u, (uint)missionId)] = playerAttr.Val;
	}

	private static bool ParamContains(JsonObject row, int levelId)
	{
		if (!row.TryGetPropertyValue("Param", out JsonNode jsonNode) || !(jsonNode is JsonArray source))
		{
			return false;
		}
		return source.Any((JsonNode x) => DlcMissionStore.ReadIntNode(x) == levelId);
	}

	private static int ParseLevelId(string param)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("nID", out value) ? value.GetInt32() : 0;
		}
		catch
		{
			return 0;
		}
	}
}
