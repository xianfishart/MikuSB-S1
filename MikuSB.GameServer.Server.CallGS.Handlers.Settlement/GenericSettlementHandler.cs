using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Settlement;

public abstract class GenericSettlementHandler : ICallGSHandler
{
	private static readonly Logger Logger = new Logger("Settlement");

	private readonly string _api;

	private static readonly Dictionary<uint, JsonObject> _levels = new Dictionary<uint, JsonObject>();

	protected GenericSettlementHandler(string api)
	{
		_api = api;
	}

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JObject req;
		try
		{
			req = JObject.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
		}
		catch
		{
			req = new JObject();
		}
		uint levelId = FindLevelId(req);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JArray awards = new JArray();
		if (_api == "Repression_Chapter_LevelSettlement" && levelId != 0)
		{
			PlayerAttr taskAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == 10408);
			if (taskAttr == null)
			{
				taskAttr = new PlayerAttr
				{
					Gid = 2u,
					Sid = 10408u,
					Val = 0u
				};
				player.Data.Attrs.Add(taskAttr);
			}
			taskAttr.Val = (taskAttr.Val & 1) | 2;
			sync.Custom[player.ToPackedAttrKey(2u, 10408u)] = taskAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(2u, 10408u)] = taskAttr.Val;
		}
		if (levelId != 0)
		{
			JsonObject level = LoadLevel(levelId);
			ILevelRewardConfig typed = ResolveTypedConfig(levelId);
			if (level == null && typed == null)
			{
				Logger.Warn($"settlement {_api}: level {levelId} not found in any reward table (raw/typed)");
			}
			if (level != null || typed != null)
			{
				bool wasPassed = player.IsLevelReallyPassed(levelId);
				uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				player.SetAttr(22u, levelId, now);
				sync.Custom[player.ToPackedAttrKey(22u, levelId)] = now;
				sync.Custom[player.ToShiftedAttrKey(22u, levelId)] = now;
				int star = ReadInt(req["nStar"] ?? req["nStarCount"] ?? req["nStarNum"]);
				if (star > 0)
				{
					PlayerAttr starAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == levelId);
					if (starAttr == null)
					{
						starAttr = new PlayerAttr
						{
							Gid = 21u,
							Sid = levelId,
							Val = 0u
						};
						player.Data.Attrs.Add(starAttr);
					}
					uint mask = ((star >= 3) ? 7u : ((uint)((1 << star) - 1)));
					starAttr.Val |= mask;
					sync.Custom[player.ToPackedAttrKey(21u, levelId)] = starAttr.Val;
					sync.Custom[player.ToShiftedAttrKey(21u, levelId)] = starAttr.Val;
				}
				if (typed != null)
				{
					JsonArray rows = await player.RewardManager.GrantLevelRewardsAsync(typed, !wasPassed, now, sync);
					JArray group = new JArray();
					foreach (JsonNode row in rows)
					{
						group.Add(JArray.Parse(row.ToJsonString()));
					}
					if (group.Count > 0)
					{
						awards.Add(group);
					}
				}
				else if (level != null)
				{
					JArray rewardGroup = await LevelRewardHelper.GrantAsync(player, level, sync, wasPassed);
					if (rewardGroup.Count > 0)
					{
						awards.Add(rewardGroup);
					}
				}
				AchievementProgress.MarkLevel(player, levelId, sync);
				// 第 415 节：通用结算（危境战幕 / 常规行动 / 角色关 / 塔 …）也要喂任务进度，
				// 否则日常「完成任意关卡 N 次」这类任务只有主线关卡才算数。
				TaskEventService.OnLevelClear(player, levelId);
				DatabaseHelper.SaveDatabaseType(player.Data);
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
		}
		await CallGSRouter.SendScript(arg: new JObject
		{
			["tbAward"] = awards,
			["tbRewards"] = awards.DeepClone()
		}.ToString(), connection: connection, api: _api, extra: sync);
	}

	private static uint FindLevelId(JObject req)
	{
		string[] array = new string[6] { "nLevelID", "nLevelId", "levelID", "levelId", "LevelID", "LevelId" };
		foreach (string key in array)
		{
			long valueOrDefault = req.Value<long?>(key).GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				return (uint)valueOrDefault;
			}
		}
		long valueOrDefault2 = req.Value<long?>("nID").GetValueOrDefault();
		if (valueOrDefault2 > 0)
		{
			return (uint)valueOrDefault2;
		}
		if (req["tbParam"] is JObject jObject)
		{
			string[] array2 = new string[3] { "nLevelID", "nLevelId", "nID" };
			foreach (string key2 in array2)
			{
				long valueOrDefault3 = jObject.Value<long?>(key2).GetValueOrDefault();
				if (valueOrDefault3 > 0)
				{
					return (uint)valueOrDefault3;
				}
			}
		}
		return 0u;
	}

	private static ILevelRewardConfig? ResolveTypedConfig(uint levelId)
	{
		if (GameData.ChapterLevelData.TryGetValue(levelId, out ChapterLevelExcel value))
		{
			return value;
		}
		if (GameData.DailyLevelData.TryGetValue(levelId, out DailyLevelExcel value2))
		{
			return value2;
		}
		if (GameData.RoleLevelData.TryGetValue(levelId, out RoleLevelExcel value3))
		{
			return value3;
		}
		if (GameData.TowerEventLevelData.TryGetValue(levelId, out TowerEventLevelExcel value4))
		{
			return value4;
		}
		if (GameData.TowerLevelData.TryGetValue(levelId, out TowerLevelExcel value5))
		{
			return value5;
		}
		if (GameData.DlcChapterLevelData.TryGetValue(levelId, out DlcChapterLevelExcel value6))
		{
			return value6;
		}
		if (GameData.RepressionLevelData.TryGetValue(levelId, out RepressionLevelExcel value7))
		{
			return value7;
		}
		if (GameData.VirCaptureLevelData.TryGetValue(levelId, out VirCaptureLevelExcel value8))
		{
			return value8;
		}
		if (GameData.ElementLevelData.TryGetValue(levelId, out ElementLevelExcel value9))
		{
			return value9;
		}
		if (GameData.DlcClimbTowerLevelData.TryGetValue(levelId, out DlcClimbTowerLevelExcel value10))
		{
			return value10;
		}
		if (GameData.BagRogueLevelData.TryGetValue(levelId, out BagRogueLevelExcel value11))
		{
			return value11;
		}
		return null;
	}

	private static JsonObject? LoadLevel(uint id)
	{
		if (_levels.TryGetValue(id, out JsonObject value))
		{
			return value;
		}
		try
		{
			string[] array = new string[4] { "chapter/level.json", "dlc/dlc1/chapter/level.json", "dlc/Repression/level.json", "daily/level.json" };
			foreach (string text in array)
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", text.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(path))
				{
					continue;
				}
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray == null)
				{
					continue;
				}
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject && ReadInt(jsonObject["ID"]) == (int)id)
					{
						_levels[id] = jsonObject;
						return jsonObject;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static int ReadInt(JToken? token)
	{
		if (token == null)
		{
			return 0;
		}
		if (token.Type == JTokenType.Integer)
		{
			return token.Value<int>();
		}
		if (token.Type == JTokenType.Float)
		{
			return (int)token.Value<decimal>();
		}
		int result;
		return int.TryParse(token.ToString(), out result) ? result : 0;
	}

	private static int ReadInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<long>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<double>(out var value3))
			{
				return (int)value3;
			}
		}
		return 0;
	}
}
