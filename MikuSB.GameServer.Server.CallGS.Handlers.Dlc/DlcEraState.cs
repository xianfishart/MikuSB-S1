using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

internal static class DlcEraState
{
	public const uint Gid = 15u;

	public const uint ActIdSid = 1u;

	private const uint DailyMissionSid = 10u;

	private const uint WeeklyMissionSid = 100u;

	private const int Slots = 20;

	private static List<JsonObject>? _missions;

	public static uint CurrentEraId(PlayerInstance player)
	{
		return ReadAttr(player, 1u);
	}

	public static void SetEra(PlayerInstance player, uint eraId, NtfSyncPlayer? sync)
	{
		SetAttr(player, 1u, eraId, sync);
		// 第 455 节：多诺-巅峰对决的赛季号（属性 185/1）。本期没有 Donoduel 时它会自动跳过。
		DonoDuelState.Ensure(player, eraId, sync);
		RollMissionSlots(player, eraId, sync);
	}

	public static uint PickDefaultEraId(PlayerInstance player, DateTime now)
	{
		uint num = DlcPin.Override();
		if (!DlcPin.IsDefault(num))
		{
			// 轻量复刻期（1~4）的版本内容在这套客户端里不存在，期号回落到
			// EraLite 配置：非 0 就是明确钉的期，0 表示「按游戏默认」——
			// 那就继续往下走默认算法，与 GM 里选「游戏默认」完全同一条路径（第 527 节）。
			uint lite = EraLite.DlcEra(num);
			if (lite != 0)
			{
				return lite;
			}
		}
		uint num2 = CurrentEraId(player);
		if (num2 != 0 && GameData.DlcActivityData.TryGetValue(num2, out DlcActivityExcel value) && IsOpen(value, now))
		{
			return num2;
		}
		var num3 = (from x in GameData.DlcActivityData.Values
			select new
			{
				Config = x,
				Start = ParseConfigTime(x.EnterStartTime)
			} into x
			where x.Start.HasValue && x.Start.Value <= now && IsOpen(x.Config, now)
			orderby x.Start descending
			select x).FirstOrDefault()?.Config.Id;
		if (num3.HasValue)
		{
			return (uint)num3.Value;
		}
		// 一期都选不出来（私服里时间窗普遍已过期）就是「游戏默认」本身的结果：0。
		// 这里不擅自钉到某一期，否则就和 GM 的「游戏默认」不是同一条路径了。
		return 0u;
	}

	private static bool IsOpen(DlcActivityExcel era, DateTime now)
	{
		DateTime? dateTime = ParseConfigTime(era.CloseEndTime);
		return !dateTime.HasValue || dateTime.Value > now;
	}

	public static void RollMissionSlots(PlayerInstance player, uint eraId, NtfSyncPlayer? sync)
	{
		List<uint> list = new List<uint>();
		List<uint> list2 = new List<uint>();
		if (eraId != 0 && GameData.DlcActivityData.TryGetValue(eraId, out DlcActivityExcel value) && value.MissionGroups.Count > 0)
		{
			HashSet<uint> hashSet = value.MissionGroups.ToHashSet();
			foreach (JsonObject item in Missions())
			{
				if (TryUInt(item["GroupID"], out var value2) && hashSet.Contains(value2) && TryUInt(item["MissionID"], out var value3) && value3 != 0)
				{
					uint value4;
					switch (TryUInt(item["RefreshRule"], out value4) ? value4 : 0)
					{
					case 1u:
						list.Add(value3);
						break;
					case 2u:
						list2.Add(value3);
						break;
					}
				}
			}
		}
		list.Sort();
		list2.Sort();
		for (uint num = 0u; num < 20; num++)
		{
			SetAttr(player, 11 + num, (num < list.Count) ? list[(int)num] : 0u, sync);
			SetAttr(player, 101 + num, (num < list2.Count) ? list2[(int)num] : 0u, sync);
		}
	}

	public static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}

	private static List<JsonObject> Missions()
	{
		if (_missions != null)
		{
			return _missions;
		}
		_missions = new List<JsonObject>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_mission.json");
			if (!File.Exists(path))
			{
				return _missions;
			}
			if (JsonNode.Parse(File.ReadAllText(path)) is JsonArray source)
			{
				_missions = source.OfType<JsonObject>().ToList();
			}
		}
		catch
		{
			_missions = new List<JsonObject>();
		}
		return _missions;
	}

	private static bool TryUInt(JsonNode? node, out uint value)
	{
		value = 0u;
		if (!(node is JsonValue jsonValue))
		{
			return false;
		}
		if (jsonValue.TryGetValue<uint>(out value))
		{
			return true;
		}
		if (jsonValue.TryGetValue<int>(out var value2))
		{
			value = (uint)Math.Max(0, value2);
			return true;
		}
		if (jsonValue.TryGetValue<long>(out var value3))
		{
			value = (uint)Math.Max(0L, value3);
			return true;
		}
		if (jsonValue.TryGetValue<double>(out var value4))
		{
			value = (uint)Math.Max(0.0, value4);
			return true;
		}
		if (jsonValue.TryGetValue<string>(out string value5) && uint.TryParse(value5, out var result))
		{
			value = result;
			return true;
		}
		return false;
	}

	private static uint ReadAttr(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 15 && x.Sid == sid)?.Val ?? 0;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 15 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 15u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			if (playerAttr.Val == value && sync == null)
			{
				return;
			}
			playerAttr.Val = value;
		}
		if (sync != null)
		{
			sync.Custom[player.ToPackedAttrKey(15u, sid)] = value;
			sync.Custom[player.ToShiftedAttrKey(15u, sid)] = value;
		}
	}
}
