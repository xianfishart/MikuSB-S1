using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Lineup;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.BossPvp;

internal static class BossPvpService
{
	private sealed class EnterLevelParam
	{
		[JsonPropertyName("nID")]
		public uint NId { get; set; }

		[JsonPropertyName("nLevelID")]
		public uint NLevelId { get; set; }

		[JsonPropertyName("nTeamID")]
		public uint NTeamId { get; set; }
	}

	private sealed class RecordParam
	{
		[JsonPropertyName("nID")]
		public uint NId { get; set; }

		[JsonPropertyName("nDiff")]
		public int NDiff { get; set; }

		[JsonPropertyName("nTime")]
		public double NTime { get; set; }

		[JsonPropertyName("ResidueTime")]
		public int ResidueTime { get; set; }

		[JsonPropertyName("bRecord")]
		public bool BRecord { get; set; }

		[JsonPropertyName("nTeamID")]
		public uint NTeamId { get; set; }
	}

	private sealed class SettlementParam
	{
		[JsonPropertyName("nID")]
		public uint NId { get; set; }

		[JsonPropertyName("nDiff")]
		public int NDiff { get; set; }

		[JsonPropertyName("nTime")]
		public double NTime { get; set; }

		[JsonPropertyName("ResidueTime")]
		public int ResidueTime { get; set; }

		[JsonPropertyName("nTeamID")]
		public uint NTeamId { get; set; }
	}

	private sealed class FailParam
	{
		[JsonPropertyName("nID")]
		public uint NId { get; set; }
	}

	private sealed class MopupParam
	{
		[JsonPropertyName("nID")]
		public uint NId { get; set; }

		[JsonPropertyName("nDiff")]
		public int NDiff { get; set; }
	}

	private sealed class RewardParam
	{
		[JsonPropertyName("tbTaskID")]
		public List<uint> TaskIds { get; set; } = new List<uint>();
	}

	private sealed class BossPvpRoleSnapshot
	{
		[JsonPropertyName("role")]
		public uint Role { get; set; }

		[JsonPropertyName("weapon")]
		public uint Weapon { get; set; }

		[JsonPropertyName("s1")]
		public uint S1 { get; set; }

		[JsonPropertyName("s2")]
		public uint S2 { get; set; }

		[JsonPropertyName("s3")]
		public uint S3 { get; set; }

		[JsonPropertyName("wgdpl")]
		public List<uint> Wgdpl { get; set; } = new List<uint>();

		[JsonPropertyName("wslot")]
		public Dictionary<uint, ulong> Wslot { get; set; } = new Dictionary<uint, ulong>();

		[JsonPropertyName("sgdpl1")]
		public List<uint> Sgdpl1 { get; set; } = new List<uint>();

		[JsonPropertyName("sgdpl2")]
		public List<uint> Sgdpl2 { get; set; } = new List<uint>();

		[JsonPropertyName("sgdpl3")]
		public List<uint> Sgdpl3 { get; set; } = new List<uint>();
	}

	private static readonly ConcurrentDictionary<int, (uint BossId, uint TeamId, long At)> LastEnterTeam = new ConcurrentDictionary<int, (uint, uint, long)>();

	private const uint GroupId = 51u;

	private const uint ActivitySubId = 0u;

	private const uint ChallengeNumSid = 1u;

	private const uint DiffStartId = 10u;

	private const uint LevelStartSid = 100u;

	private const uint LevelStride = 10u;

	private const uint BossLineup1 = 15u;

	private const uint BossLineup2 = 16u;

	private const uint FullSeasonId = 1u;

	private const int BuildKeepDays = 30;

	private const uint DailyChallengeLimit = 999u;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private static List<JsonObject>? _questRows;

	public static async ValueTask<(object Response, NtfSyncPlayer Sync)> HandleGetOpenIdAsync(PlayerInstance player)
	{
		await EnsureBossLineupsAsync(player);
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		BossPvpBossChallengeExcel cyclicSeason = GetCyclicSeason(out var seasonStart, out var seasonEnd);
		uint num = cyclicSeason?.ID ?? 1;
		int num2 = ReadInt(player, 0u);
		if (num2 > 0 && num2 != (int)num)
		{
			ResetBossPvpProgress(player, ntfSyncPlayer);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		SetStr(player, 0u, num.ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		SetStr(player, 1u, GetDailyChallengeNum().ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		if (cyclicSeason != null)
		{
			for (int i = 0; i < cyclicSeason.BossIds.Count; i++)
			{
				uint bossLevelId = cyclicSeason.BossIds[i];
				EnsureStr(player, (uint)(10 + (i + 1)), "0", ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 1u), EmptySnapshotJson(), ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 2u), EmptySnapshotJson(), ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 3u), EmptySnapshotJson(), ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 4u), "0", ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 5u), "0", ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 6u), "0", ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 7u), "0", ntfSyncPlayer);
				EnsureStr(player, GetBossSid(bossLevelId, 8u), "0", ntfSyncPlayer);
				MarkBossTaskProgress(player, bossLevelId, ReadInt(player, GetBossSid(bossLevelId, 8u)), ntfSyncPlayer);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		var item = new
		{
			nID = num,
			tbTimeCfg = new[]
			{
				new
				{
					nID = num,
					nStartTime = seasonStart.ToUnixTimeSeconds(),
					nEndTime = seasonEnd.ToUnixTimeSeconds()
				}
			}
		};
		return (item, ntfSyncPlayer);
	}

	public static object HandleEnterLevel(PlayerInstance? player, string? param)
	{
		EnterLevelParam enterLevelParam = Deserialize<EnterLevelParam>(param);
		if (player != null && enterLevelParam != null && enterLevelParam.NTeamId != 0)
		{
			LastEnterTeam[player.Uid] = (enterLevelParam.NId, enterLevelParam.NTeamId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		}
		return new
		{
			nSeed = Random.Shared.Next(1, int.MaxValue),
			nID = (enterLevelParam?.NId ?? 0)
		};
	}

	private static uint ResolveLineupId(PlayerInstance player, uint bossLevelId, uint lineupId)
	{
		if (lineupId != 0)
		{
			return lineupId;
		}
		if (player != null && LastEnterTeam.TryGetValue(player.Uid, out (uint, uint, long) value))
		{
			_ = value.Item1;
			return value.Item2;
		}
		return 0u;
	}

	public static (object Response, NtfSyncPlayer Sync) HandleRecord(PlayerInstance player, string? param)
	{
		RecordParam recordParam = Deserialize<RecordParam>(param);
		if (recordParam == null)
		{
			return (Response: new
			{
				bRecord = false
			}, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		if (recordParam.BRecord)
		{
			int num = ReadInt(player, GetBossSid(recordParam.NId, 4u));
			int num2 = ComputeIntegral(recordParam.NId, recordParam.NDiff, recordParam.ResidueTime);
			if (num2 >= num)
			{
				uint lineupId = ResolveLineupId(player, recordParam.NId, recordParam.NTeamId);
				WriteBestRun(player, recordParam.NId, lineupId, recordParam.NTime, num2, ntfSyncPlayer);
			}
		}
		return (Response: new
		{
			bRecord = recordParam.BRecord
		}, Sync: ntfSyncPlayer);
	}

	public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? param)
	{
		SettlementParam settlementParam = ((param != null) ? param.Deserialize<SettlementParam>(JsonOptions) : null);
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		if (settlementParam == null)
		{
			return (Response: new JsonObject(), Sync: ntfSyncPlayer);
		}
		uint bossSid = GetBossSid(settlementParam.NId, 7u);
		uint bossSid2 = GetBossSid(settlementParam.NId, 6u);
		uint bossSid3 = GetBossSid(settlementParam.NId, 8u);
		SetStr(player, bossSid, (ReadInt(player, bossSid) + 1).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		SetStr(player, bossSid2, (ReadInt(player, bossSid2) + 1).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		SetStr(player, bossSid3, Math.Max(ReadInt(player, bossSid3), settlementParam.NDiff).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		MarkBossTaskProgress(player, settlementParam.NId, settlementParam.NDiff, ntfSyncPlayer);
		uint? num = TryGetPositionDiffSid(settlementParam.NId);
		if (num.HasValue)
		{
			int num2 = Math.Max(ReadInt(player, num.Value), settlementParam.NDiff);
			SetStr(player, num.Value, num2.ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		}
		uint num3 = ResolveLineupId(player, settlementParam.NId, settlementParam.NTeamId);
		if (num3 != 0)
		{
			WriteLineupSnapshots(player, settlementParam.NId, num3, ntfSyncPlayer);
		}
		int num4 = ComputeIntegral(settlementParam.NId, settlementParam.NDiff, settlementParam.ResidueTime);
		if (num4 > ReadInt(player, GetBossSid(settlementParam.NId, 4u)))
		{
			WriteBestRun(player, settlementParam.NId, num3, settlementParam.NTime, num4, ntfSyncPlayer);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return (Response: new JsonObject(), Sync: ntfSyncPlayer);
	}

	public static (JsonNode Response, NtfSyncPlayer Sync) HandleFail(PlayerInstance player, JsonNode? param)
	{
		FailParam failParam = ((param != null) ? param.Deserialize<FailParam>(JsonOptions) : null);
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		if (failParam == null)
		{
			return (Response: new JsonObject(), Sync: ntfSyncPlayer);
		}
		uint bossSid = GetBossSid(failParam.NId, 7u);
		SetStr(player, bossSid, (ReadInt(player, bossSid) + 1).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		return (Response: new JsonObject(), Sync: ntfSyncPlayer);
	}

	public static (object Response, NtfSyncPlayer Sync) HandleMopup(PlayerInstance player, string? param)
	{
		MopupParam mopupParam = Deserialize<MopupParam>(param);
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		if (mopupParam == null)
		{
			return (Response: new { }, Sync: ntfSyncPlayer);
		}
		uint bossSid = GetBossSid(mopupParam.NId, 7u);
		uint bossSid2 = GetBossSid(mopupParam.NId, 6u);
		uint bossSid3 = GetBossSid(mopupParam.NId, 8u);
		SetStr(player, bossSid, (ReadInt(player, bossSid) + 1).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		SetStr(player, bossSid2, (ReadInt(player, bossSid2) + 1).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		SetStr(player, bossSid3, Math.Max(ReadInt(player, bossSid3), mopupParam.NDiff).ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		MarkBossTaskProgress(player, mopupParam.NId, mopupParam.NDiff, ntfSyncPlayer);
		uint? num = TryGetPositionDiffSid(mopupParam.NId);
		if (num.HasValue)
		{
			int num2 = Math.Max(ReadInt(player, num.Value), mopupParam.NDiff + 1);
			SetStr(player, num.Value, num2.ToString(CultureInfo.InvariantCulture), ntfSyncPlayer);
		}
		int num3 = ComputeIntegral(mopupParam.NId, mopupParam.NDiff, 0);
		if (num3 > ReadInt(player, GetBossSid(mopupParam.NId, 4u)))
		{
			WriteBestRun(player, mopupParam.NId, 0u, 0.0, num3, ntfSyncPlayer);
		}
		return (Response: new { }, Sync: ntfSyncPlayer);
	}

	public static async Task<(object Response, NtfSyncPlayer Sync)> HandleGetReward(PlayerInstance player, string? param)
	{
		RewardParam rewardParam = Deserialize<RewardParam>(param);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray awards = new JsonArray();
		if (rewardParam?.TaskIds == null || rewardParam.TaskIds.Count == 0)
		{
			return (Response: new
			{
				tbAward = Array.Empty<object>()
			}, Sync: sync);
		}
		BossPvpBossChallengeExcel season = GetOpenSeason();
		if (season == null)
		{
			return (Response: new
			{
				tbAward = Array.Empty<object>()
			}, Sync: sync);
		}
		List<JsonObject> rows = LoadQuestRows();
		JsonArray jsonArray = null;
		foreach (uint taskId in rewardParam.TaskIds.Distinct())
		{
			JsonObject jsonObject = rows.FirstOrDefault((JsonObject x) => ReadIntNode(x["ID"]) == taskId);
			int num;
			if (jsonObject != null)
			{
				jsonArray = jsonObject["Param"] as JsonArray;
				if (jsonArray != null)
				{
					num = ((jsonArray.Count < 2) ? 1 : 0);
					goto IL_0128;
				}
			}
			num = 1;
			goto IL_0128;
			IL_0128:
			if (num != 0)
			{
				continue;
			}
			int num2 = ReadIntNode(jsonArray[0]);
			int num3 = ReadIntNode(jsonArray[1]);
			if (num2 <= 0 || num2 > season.BossIds.Count)
			{
				continue;
			}
			uint bossLevelId = season.BossIds[num2 - 1];
			if (ReadInt(player, GetBossSid(bossLevelId, 8u)) < num3)
			{
				continue;
			}
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == taskId);
			if (playerAttr == null)
			{
				playerAttr = new PlayerAttr
				{
					Gid = 7u,
					Sid = taskId,
					Val = 0u
				};
				player.Data.Attrs.Add(playerAttr);
			}
			if ((playerAttr.Val & 1) != 0)
			{
				continue;
			}
			playerAttr.Val |= 3u;
			sync.Custom[player.ToPackedAttrKey(7u, taskId)] = playerAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(7u, taskId)] = playerAttr.Val;
			if (!(jsonObject["Rewards"] is JsonArray jsonArray2))
			{
				continue;
			}
			foreach (JsonNode item in jsonArray2)
			{
				if (item is JsonArray { Count: >=5 } jsonArray3)
				{
					List<uint> reward = jsonArray3.Select((JsonNode x) => (uint)ReadIntNode(x)).Take(5).ToList();
					if (reward.Count >= 5 && reward[0] != 0)
					{
						await VirCapture_GetLevelAward.GrantRewardAsync(player, sync, reward);
						InlineArray5<JsonNode> buffer = default;
						buffer[0] = (int)reward[0];
						buffer[1] = (int)reward[1];
						buffer[2] = (int)reward[2];
						buffer[3] = (int)reward[3];
						buffer[4] = (int)reward[4];
						awards.Add(new JsonArray(buffer));
					}
				}
			}
			jsonArray = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		return (Response: new
		{
			tbAward = awards
		}, Sync: sync);
	}

	private static List<JsonObject> LoadQuestRows()
	{
		if (_questRows != null)
		{
			return _questRows;
		}
		List<JsonObject> list = new List<JsonObject>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return _questRows = list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item2 in jsonArray)
				{
					if (item2 is JsonObject item)
					{
						list.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		return _questRows = list;
	}

	private static void ResetBossPvpProgress(PlayerInstance player, NtfSyncPlayer sync)
	{
		foreach (PlayerStrAttr item in player.Data.StrAttrs.Where((PlayerStrAttr x) => x.Gid == 51 && x.Sid != 0 && x.Sid != 1).ToList())
		{
			player.Data.StrAttrs.Remove(item);
			sync.Custom[player.ToPackedAttrKey(item.Gid, item.Sid)] = 0u;
			sync.Custom[player.ToShiftedAttrKey(item.Gid, item.Sid)] = 0u;
		}
		foreach (PlayerAttr item2 in player.Data.Attrs.Where((PlayerAttr x) => x.Gid == 7 && x.Sid >= 9601 && x.Sid <= 9615).ToList())
		{
			player.Data.Attrs.Remove(item2);
			sync.Custom[player.ToPackedAttrKey(item2.Gid, item2.Sid)] = 0u;
			sync.Custom[player.ToShiftedAttrKey(item2.Gid, item2.Sid)] = 0u;
		}
	}

	private static void MarkBossTaskProgress(PlayerInstance player, uint bossLevelId, int clearedDifficulty, NtfSyncPlayer sync)
	{
		BossPvpBossChallengeExcel openSeason = GetOpenSeason();
		if (openSeason == null)
		{
			return;
		}
		int num = openSeason.BossIds.IndexOf(bossLevelId);
		if (num < 0)
		{
			return;
		}
		num++;
		foreach (JsonObject item in LoadQuestRows())
		{
			if (!(item["Param"] is JsonArray { Count: >=2 } jsonArray) || ReadIntNode(jsonArray[0]) != num)
			{
				continue;
			}
			int num2 = ReadIntNode(jsonArray[1]);
			if (num2 <= 0 || num2 > clearedDifficulty)
			{
				continue;
			}
			uint taskId = (uint)ReadIntNode(item["ID"]);
			if (taskId != 0)
			{
				PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == taskId);
				if (playerAttr == null)
				{
					playerAttr = new PlayerAttr
					{
						Gid = 7u,
						Sid = taskId,
						Val = 0u
					};
					player.Data.Attrs.Add(playerAttr);
				}
				playerAttr.Val |= 2u;
				sync.Custom[player.ToPackedAttrKey(7u, taskId)] = playerAttr.Val;
				sync.Custom[player.ToShiftedAttrKey(7u, taskId)] = playerAttr.Val;
			}
		}
	}

	private static int ReadIntNode(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return 0;
		}
		if (jsonValue.TryGetValue<int>(out var value))
		{
			return value;
		}
		if (jsonValue.TryGetValue<string>(out string value2) && int.TryParse(value2, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return value;
		}
		return 0;
	}

	private static async ValueTask EnsureBossLineupsAsync(PlayerInstance player)
	{
		Dictionary<int, LineupDataInfo> lineups = player.LineupManager.LineupData.LineupInfo;
		LineupDataInfo baseLineup = lineups.GetValueOrDefault(1) ?? lineups.Values.FirstOrDefault();
		if (baseLineup != null)
		{
			if (!lineups.ContainsKey(15))
			{
				await player.LineupManager.UpdateLineup(15, baseLineup.Member1, baseLineup.Member2, baseLineup.Member3);
			}
			if (!lineups.ContainsKey(16))
			{
				await player.LineupManager.UpdateLineup(16, baseLineup.Member1, baseLineup.Member2, baseLineup.Member3);
			}
		}
	}

	private static void WriteBestRun(PlayerInstance player, uint bossLevelId, uint lineupId, double finishTime, int score, NtfSyncPlayer sync)
	{
		if (lineupId != 0)
		{
			WriteLineupSnapshots(player, bossLevelId, lineupId, sync);
		}
		SetStr(player, GetBossSid(bossLevelId, 4u), score.ToString(CultureInfo.InvariantCulture), sync);
		SetStr(player, GetBossSid(bossLevelId, 5u), Math.Max(0, (int)Math.Floor(finishTime)).ToString(CultureInfo.InvariantCulture), sync);
	}

	private static BossPvpRoleSnapshot[] CaptureLineupSnapshots(PlayerInstance player, uint lineupId)
	{
		Dictionary<int, LineupDataInfo> lineupInfo = player.LineupManager.LineupData.LineupInfo;
		LineupDataInfo lineupDataInfo = lineupInfo.GetValueOrDefault((int)lineupId) ?? lineupInfo.Values.FirstOrDefault((LineupDataInfo x) => x.Member1 != 0) ?? lineupInfo.Values.FirstOrDefault();
		if (lineupDataInfo != null)
		{
			return new BossPvpRoleSnapshot[3]
			{
				CaptureRoleSnapshot(player, lineupDataInfo.Member1),
				CaptureRoleSnapshot(player, lineupDataInfo.Member2),
				CaptureRoleSnapshot(player, lineupDataInfo.Member3)
			};
		}
		return new BossPvpRoleSnapshot[3]
		{
			new BossPvpRoleSnapshot(),
			new BossPvpRoleSnapshot(),
			new BossPvpRoleSnapshot()
		};
	}

	private static BossPvpRoleSnapshot CaptureRoleSnapshot(PlayerInstance player, uint characterGuid)
	{
		if (characterGuid == 0)
		{
			return new BossPvpRoleSnapshot();
		}
		CharacterInfo characterByGUID = player.CharacterManager.GetCharacterByGUID(characterGuid);
		if (characterByGUID == null)
		{
			return new BossPvpRoleSnapshot();
		}
		BossPvpRoleSnapshot bossPvpRoleSnapshot = new BossPvpRoleSnapshot
		{
			Role = characterByGUID.Guid,
			Weapon = characterByGUID.WeaponUniqueId
		};
		GameWeaponInfo weaponItem = player.InventoryManager.GetWeaponItem(characterByGUID.WeaponUniqueId);
		if (weaponItem != null)
		{
			bossPvpRoleSnapshot.Wgdpl = BuildWeaponGdpl(weaponItem);
			bossPvpRoleSnapshot.Wslot = weaponItem.PartSlots;
		}
		uint[] array = (from x in characterByGUID.SupportSlots.OrderBy((KeyValuePair<uint, uint> x) =>
			{
				KeyValuePair<uint, uint> keyValuePair = x;
				return keyValuePair.Key;
			}).Select((KeyValuePair<uint, uint> x) =>
			{
				KeyValuePair<uint, uint> keyValuePair = x;
				return keyValuePair.Value;
			})
			where x != 0
			select x).Take(3).ToArray();
		if (array.Length != 0)
		{
			bossPvpRoleSnapshot.S1 = array[0];
			bossPvpRoleSnapshot.Sgdpl1 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[0]));
		}
		if (array.Length > 1)
		{
			bossPvpRoleSnapshot.S2 = array[1];
			bossPvpRoleSnapshot.Sgdpl2 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[1]));
		}
		if (array.Length > 2)
		{
			bossPvpRoleSnapshot.S3 = array[2];
			bossPvpRoleSnapshot.Sgdpl3 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[2]));
		}
		return bossPvpRoleSnapshot;
	}

	private static List<uint> BuildWeaponGdpl(GameWeaponInfo weapon)
	{
		List<uint> list = DecodeGdpl(weapon.TemplateId);
		list.Add(weapon.Level);
		list.Add(weapon.Evolue);
		return list;
	}

	private static List<uint> BuildSupportGdpl(GameSupportCardInfo? support)
	{
		if (support == null)
		{
			return new List<uint>();
		}
		List<uint> list = DecodeGdpl(support.TemplateId);
		list.Add(support.Level);
		list.Add(0u);
		return list;
	}

	private static List<uint> DecodeGdpl(ulong templateId)
	{
		int num = 4;
		List<uint> list = new List<uint>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<uint> span = CollectionsMarshal.AsSpan(list);
		span[0] = (uint)(templateId & 0xFFFF);
		span[1] = (uint)((templateId >> 16) & 0xFFFF);
		span[2] = (uint)((templateId >> 32) & 0xFFFF);
		span[3] = (uint)((templateId >> 48) & 0xFFFF);
		return list;
	}

	private static int ComputeIntegral(uint bossLevelId, int diff, int residueTime)
	{
		if (!GameData.BossPvpBossData.TryGetValue(bossLevelId, out BossPvpBossExcel value) || diff <= 0 || diff > value.BossLevel.Count)
		{
			return 0;
		}
		List<int> list = value.BossLevel[diff - 1];
		if (list.Count == 0)
		{
			return 0;
		}
		int num = ((list.Count > 2) ? list[2] : 0);
		int num2 = ((list.Count > 3) ? list[3] : 0);
		int num3 = ((list.Count > 4) ? list[4] : 0);
		return (int)Math.Floor((double)((num2 + num3 * Math.Max(0, residueTime)) * num) + 0.5);
	}

	private static uint? TryGetPositionDiffSid(uint bossLevelId)
	{
		BossPvpBossChallengeExcel openSeason = GetOpenSeason();
		if (openSeason == null)
		{
			return null;
		}
		int num = openSeason.BossIds.FindIndex((uint x) => x == bossLevelId);
		if (num < 0)
		{
			return null;
		}
		return (uint)(10 + (num + 1));
	}

	private static BossPvpBossChallengeExcel? GetOpenSeason()
	{
		DateTimeOffset seasonStart;
		DateTimeOffset seasonEnd;
		return GetCyclicSeason(out seasonStart, out seasonEnd);
	}

	private static BossPvpBossChallengeExcel? GetCyclicSeason(out DateTimeOffset seasonStart, out DateTimeOffset seasonEnd)
	{
		DateTimeOffset dateTimeOffset = (seasonStart = DateTimeOffset.Now);
		seasonEnd = dateTimeOffset.AddDays(30.0);
		return GetFullSeason();
	}

	private static BossPvpBossChallengeExcel? GetFullSeason()
	{
		if (GameData.BossPvpBossChallengeData.TryGetValue(1u, out BossPvpBossChallengeExcel value) && value != null && value.BossIds.Count > 0)
		{
			return value;
		}
		BossPvpBossChallengeExcel bossPvpBossChallengeExcel = null;
		foreach (BossPvpBossChallengeExcel value2 in GameData.BossPvpBossChallengeData.Values)
		{
			if (value2.BossIds.Count > 0 && (bossPvpBossChallengeExcel == null || value2.BossIds.Count > bossPvpBossChallengeExcel.BossIds.Count))
			{
				bossPvpBossChallengeExcel = value2;
			}
		}
		return bossPvpBossChallengeExcel;
	}

	private static void WriteLineupSnapshots(PlayerInstance player, uint bossLevelId, uint lineupId, NtfSyncPlayer sync)
	{
		BossPvpRoleSnapshot[] array = CaptureLineupSnapshots(player, lineupId);
		SetStr(player, GetBossSid(bossLevelId, 1u), JsonSerializer.Serialize(array[0], JsonOptions), sync);
		SetStr(player, GetBossSid(bossLevelId, 2u), JsonSerializer.Serialize(array[1], JsonOptions), sync);
		SetStr(player, GetBossSid(bossLevelId, 3u), JsonSerializer.Serialize(array[2], JsonOptions), sync);
	}

	private static uint GetDailyChallengeNum()
	{
		return 999u;
	}

	private static int ReadInt(PlayerInstance player, uint sid)
	{
		if (!int.TryParse(player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 51 && x.Sid == sid)?.Val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return 0;
		}
		return result;
	}

	private static void EnsureStr(PlayerInstance player, uint sid, string value, NtfSyncPlayer sync)
	{
		if (player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 51 && x.Sid == sid) == null)
		{
			SetStr(player, sid, value, sync);
		}
	}

	private static void SetStr(PlayerInstance player, uint sid, string value, NtfSyncPlayer sync)
	{
		player.SetStrAttr(51u, sid, value);
		sync.CustomStr[player.ToShiftedAttrKey(51u, sid)] = value;
	}

	private static uint GetBossSid(uint bossLevelId, uint offset)
	{
		return 10 * bossLevelId + 100 + offset;
	}

	private static string EmptySnapshotJson()
	{
		return JsonSerializer.Serialize(new BossPvpRoleSnapshot(), JsonOptions);
	}

	private static T? Deserialize<T>(string? param)
	{
		if (string.IsNullOrWhiteSpace(param))
		{
			return default;
		}
		return JsonSerializer.Deserialize<T>(param, JsonOptions);
	}

	private static DateTimeOffset? ParseBossTime(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		if (!DateTime.TryParseExact(value.Trim().Trim(new char[2] { '[', ']' }), "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return null;
		}
		return new DateTimeOffset(result);
	}
}
