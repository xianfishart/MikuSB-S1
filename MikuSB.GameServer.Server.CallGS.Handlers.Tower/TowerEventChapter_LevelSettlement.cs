using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerEventChapter_LevelSettlement")]
public class TowerEventChapter_LevelSettlement : ICallGSHandler
{
	private const uint LevelStateGroupId = 21u;

	private const uint LaunchPassGroupId = 22u;

	private const uint PassedFlagMask = 263u;

	private static readonly Logger Logger = new Logger("TowerEvent");

	private static Dictionary<uint, JsonObject>? _chapters;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonNode, NtfSyncPlayer) tuple = HandleSettlement(connection.Player, JsonNode.Parse(param));
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "TowerEventChapter_LevelSettlement", arg: response.ToJsonString());
	}

	public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
	{
		try
		{
			return HandleSettlementAsync(player, tbParam).GetAwaiter().GetResult();
		}
		catch (Exception value)
		{
			Logger.Error($"TowerEvent settlement crashed: {value}");
			return (Response: new JsonObject { ["sErr"] = "error.Server" }, Sync: new NtfSyncPlayer());
		}
	}

	public static async Task<(JsonNode Response, NtfSyncPlayer Sync)> HandleSettlementAsync(PlayerInstance player, JsonNode? tbParam)
	{
		try
		{
			return await HandleSettlementInnerAsync(player, tbParam);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Logger.Error($"TowerEvent settlement failed: playerNull={player == null} dataNull={player?.Data == null} attrs={player?.Data?.Attrs?.Count} param={tbParam?.ToJsonString()} ex={ex2}");
			return (Response: new JsonObject { ["sErr"] = "error.Server" }, Sync: new NtfSyncPlayer());
		}
	}

	private static async Task<(JsonNode Response, NtfSyncPlayer Sync)> HandleSettlementInnerAsync(PlayerInstance player, JsonNode? tbParam)
	{
		TowerEventSettlementParam req = ((tbParam != null) ? tbParam.Deserialize<TowerEventSettlementParam>() : null);
		if (req == null || req.LevelId == 0 || req.ChapterId == 0)
		{
			Logger.Error("Invalid tower event settlement payload: " + (tbParam?.ToJsonString() ?? "null"));
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr levelStateAttr = GetOrCreateAttr(player.Data, 21u, (uint)req.LevelId);
		levelStateAttr.Val |= 263u;
		SyncAttr(sync, player, levelStateAttr);
		PlayerAttr passAttr = GetOrCreateAttr(player.Data, 22u, (uint)req.LevelId);
		bool wasPassed = passAttr.Val != 0;
		passAttr.Val = Math.Max(1u, passAttr.Val + 1);
		SyncAttr(sync, player, passAttr);
		JsonArray awards = new JsonArray();
		await AutoCompleteSegmentAsync(player, req.ChapterId, req.LevelId, sync, awards);
		if (GameData.TowerEventLevelData.TryGetValue((uint)req.LevelId, out TowerEventLevelExcel levelConfig))
		{
			JsonArray rows = await player.RewardManager.GrantLevelRewardsAsync(levelConfig, !wasPassed, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync);
			JsonArray group = new JsonArray();
			foreach (JsonNode item in rows)
			{
				group.Add(item?.DeepClone());
			}
			if (group.Count > 0)
			{
				awards.Add(group);
			}
		}
		AdvanceType74Tasks(player, req.LevelId, sync);
		Logger.Info($"TowerEvent settlement saved. uid={player.Uid} chapterId={req.ChapterId} levelId={req.LevelId} levelStateVal={levelStateAttr.Val} passVal={passAttr.Val} awards={awards.Count}");
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return (Response: awards, Sync: sync);
	}

	private static void AdvanceType74Tasks(PlayerInstance player, int levelId, NtfSyncPlayer sync)
	{
		foreach (AchievementExcel value in GameData.AchievementData.Values)
		{
			if (value.Type == 74)
			{
				uint num = value.Group;
				bool flag = num - 3 <= 2;
				if (flag && ParamMatchesLevel(value.Param, levelId))
				{
					PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 2u, value.Id);
					uint num2 = Math.Max(1u, value.CheckValue);
					orCreateAttr.Val = (orCreateAttr.Val & 1) | (num2 << 1);
					SyncAttr(sync, player, orCreateAttr);
				}
			}
		}
	}

	private static bool ParamMatchesLevel(JToken? param, int levelId)
	{
		if (!(param is JArray { Count: >=2 } jArray))
		{
			return false;
		}
		int num = (int.TryParse(jArray[1]?.ToString(), out var result) ? result : (-1));
		return num == -1 || num == levelId;
	}

	private static async Task AutoCompleteSegmentAsync(PlayerInstance player, int chapterId, int levelId, NtfSyncPlayer sync, JsonArray awards)
	{
		if (_chapters == null)
		{
			_chapters = new Dictionary<uint, JsonObject>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "tower_event";
				buffer[4] = "chapter.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JsonArray rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (rows != null)
					{
						foreach (JsonNode node in rows)
						{
							JsonObject row = node as JsonObject;
							if (row != null && uint.TryParse(row["ID"]?.ToString(), out var id))
							{
								_chapters[id] = row;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		if (!_chapters.TryGetValue((uint)chapterId, out JsonObject chapter))
		{
			return;
		}
		JsonNode jsonNode = chapter["Level"];
		if (!(jsonNode is JsonArray levels))
		{
			return;
		}
		int index = -1;
		for (int i = 0; i < levels.Count; i++)
		{
			if (int.TryParse(levels[i]?.ToString(), out var id2) && id2 == levelId)
			{
				index = i;
				break;
			}
		}
		if (index < 0)
		{
			return;
		}
		int position = index + 1;
		if (position % 5 != 0)
		{
			return;
		}
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		int i2 = index - 1;
		while (i2 >= 0 && i2 >= index - 4)
		{
			if (uint.TryParse(levels[i2]?.ToString(), out var id3))
			{
				PlayerAttr attr = GetOrCreateAttr(player.Data, 22u, id3);
				if (attr.Val == 0)
				{
					attr.Val = now;
					SyncAttr(sync, player, attr);
					if (GameData.TowerEventLevelData.TryGetValue(id3, out TowerEventLevelExcel skippedConfig))
					{
						JsonArray rows2 = await player.RewardManager.GrantLevelRewardsAsync(skippedConfig, isFirstClear: true, now, sync);
						JsonArray group = new JsonArray();
						foreach (JsonNode item in rows2)
						{
							group.Add(item?.DeepClone());
						}
						if (group.Count > 0)
						{
							awards.Add(group);
						}
					}
					skippedConfig = null;
				}
			}
			i2--;
		}
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
