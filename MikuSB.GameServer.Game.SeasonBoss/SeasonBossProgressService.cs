using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Lineup;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.SeasonBoss;

internal static class SeasonBossProgressService
{
	private sealed class SeasonBossRoleSnapshot
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

	public const uint StrGroupId = 26u;

	public const uint LevelStartSid = 100u;

	public const uint LevelStride = 10u;

	private const uint NumGroupId = 18u;

	private const uint LevelIndexSid = 3u;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	};

	public static uint GetStrSid(uint levelId, uint offset)
	{
		return 10 * levelId + 100 + offset;
	}

	public static void RecordLineup(PlayerInstance player, uint levelId, uint lineupId, NtfSyncPlayer sync)
	{
		if (levelId != 0)
		{
			Dictionary<int, LineupDataInfo> lineupInfo = player.LineupManager.LineupData.LineupInfo;
			LineupDataInfo lineupDataInfo = lineupInfo.GetValueOrDefault((int)lineupId) ?? lineupInfo.GetValueOrDefault(1) ?? lineupInfo.Values.FirstOrDefault();
			uint[] array = ((lineupDataInfo == null) ? new uint[3] : new uint[3] { lineupDataInfo.Member1, lineupDataInfo.Member2, lineupDataInfo.Member3 });
			for (uint num = 1u; num <= 3; num++)
			{
				uint characterGuid = array[num - 1];
				SeasonBossRoleSnapshot value = CaptureRoleSnapshot(player, characterGuid);
				string value2 = JsonSerializer.Serialize(value, JsonOptions);
				uint strSid = GetStrSid(levelId, num);
				player.SetStrAttr(26u, strSid, value2);
				sync.CustomStr[player.ToShiftedAttrKey(26u, strSid)] = value2;
			}
		}
	}

	public static void RecordResult(PlayerInstance player, uint levelId, int score, int time, NtfSyncPlayer sync)
	{
		if (levelId != 0)
		{
			uint strSid = GetStrSid(levelId, 6u);
			uint strSid2 = GetStrSid(levelId, 7u);
			uint strSid3 = GetStrSid(levelId, 4u);
			uint strSid4 = GetStrSid(levelId, 5u);
			SetStr(player, sync, strSid, (ReadStr(player, strSid) + 1).ToString());
			SetStr(player, sync, strSid2, (ReadStr(player, strSid2) + 1).ToString());
			if (score > ReadStr(player, strSid3))
			{
				SetStr(player, sync, strSid3, score.ToString());
			}
			int num = ReadStr(player, strSid4);
			if (time > 0 && (num <= 0 || time < num))
			{
				SetStr(player, sync, strSid4, time.ToString());
			}
		}
	}

	public static void BumpLevelIndex(PlayerInstance player, int zoneId, int diff, NtfSyncPlayer sync)
	{
		if (zoneId > 0 && diff > 0)
		{
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 18 && x.Sid == 3);
			uint num = playerAttr?.Val ?? 0;
			int num2 = (int)(num / 100);
			int num3 = (int)(num % 100);
			if (zoneId == 1)
			{
				num2 = Math.Max(num2, diff);
			}
			else
			{
				num3 = Math.Max(num3, diff);
			}
			uint num4 = (uint)(num2 * 100 + num3);
			if (playerAttr == null)
			{
				playerAttr = new PlayerAttr
				{
					Gid = 18u,
					Sid = 3u,
					Val = num4
				};
				player.Data.Attrs.Add(playerAttr);
			}
			else
			{
				playerAttr.Val = num4;
			}
			sync.Custom[player.ToPackedAttrKey(18u, 3u)] = num4;
			sync.Custom[player.ToShiftedAttrKey(18u, 3u)] = num4;
		}
	}

	private static int ReadStr(PlayerInstance player, uint sid)
	{
		string s = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 26 && x.Sid == sid)?.Val;
		int result;
		return int.TryParse(s, out result) ? result : 0;
	}

	private static void SetStr(PlayerInstance player, NtfSyncPlayer sync, uint sid, string value)
	{
		player.SetStrAttr(26u, sid, value);
		sync.CustomStr[player.ToShiftedAttrKey(26u, sid)] = value;
	}

	private static SeasonBossRoleSnapshot CaptureRoleSnapshot(PlayerInstance player, uint characterGuid)
	{
		if (characterGuid == 0)
		{
			return new SeasonBossRoleSnapshot();
		}
		CharacterInfo characterByGUID = player.CharacterManager.GetCharacterByGUID(characterGuid);
		if (characterByGUID == null)
		{
			return new SeasonBossRoleSnapshot();
		}
		SeasonBossRoleSnapshot seasonBossRoleSnapshot = new SeasonBossRoleSnapshot
		{
			Role = characterByGUID.Guid,
			Weapon = characterByGUID.WeaponUniqueId
		};
		GameWeaponInfo weaponItem = player.InventoryManager.GetWeaponItem(characterByGUID.WeaponUniqueId);
		if (weaponItem != null)
		{
			seasonBossRoleSnapshot.Wgdpl = BuildWeaponGdpl(weaponItem);
			seasonBossRoleSnapshot.Wslot = weaponItem.PartSlots;
		}
		uint[] array = (from x in characterByGUID.SupportSlots
			orderby x.Key
			select x.Value into x
			where x != 0
			select x).Take(3).ToArray();
		if (array.Length != 0)
		{
			seasonBossRoleSnapshot.S1 = array[0];
			seasonBossRoleSnapshot.Sgdpl1 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[0]));
		}
		if (array.Length > 1)
		{
			seasonBossRoleSnapshot.S2 = array[1];
			seasonBossRoleSnapshot.Sgdpl2 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[1]));
		}
		if (array.Length > 2)
		{
			seasonBossRoleSnapshot.S3 = array[2];
			seasonBossRoleSnapshot.Sgdpl3 = BuildSupportGdpl(player.InventoryManager.GetSupportCardItem(array[2]));
		}
		return seasonBossRoleSnapshot;
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
}
