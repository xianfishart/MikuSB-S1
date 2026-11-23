using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Settlement;

internal static class LevelRewardHelper
{
	public static Task<JArray> GrantAsync(PlayerInstance player, JsonObject level, NtfSyncPlayer sync, bool wasPassed, uint seed = 0u)
	{
		return GrantCoreAsync(player, ReadUInt(level["ID"]), ReadIds(level["FirstDropID"]), ReadIds(level["BaseDropID"]), ReadIds(level["RandomDropID"]), sync, wasPassed, seed);
	}

	public static Task<JArray> GrantAsync(PlayerInstance player, ILevelRewardConfig level, NtfSyncPlayer sync, bool wasPassed, uint seed = 0u)
	{
		return GrantCoreAsync(player, level.ID, level.FirstDropIds(), level.BaseDropIds(), level.RandomDropIds(), sync, wasPassed, seed);
	}

	private static async Task<JArray> GrantCoreAsync(PlayerInstance player, uint levelId, IReadOnlyList<uint> firstIds, IReadOnlyList<uint> baseIds, IReadOnlyList<uint> randomIds, NtfSyncPlayer sync, bool wasPassed, uint seed)
	{
		JArray rows = new JArray();
		if (seed == 0)
		{
			seed = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() ^ player.Uid);
		}
		Random random = new Random((int)(seed ^ levelId));
		if (wasPassed)
		{
			await GrantDropIds(player, baseIds, random, sync, rows);
		}
		else
		{
			await GrantDropIds(player, firstIds, random, sync, rows);
		}
		await GrantDropIds(player, randomIds, random, sync, rows);
		return rows;
	}

	private static async Task GrantDropIds(PlayerInstance player, IReadOnlyList<uint> dropIds, Random random, NtfSyncPlayer sync, JArray rows)
	{
		foreach (uint dropId in dropIds)
		{
			if (!GameData.DropData.TryGetValue(dropId, out DropExcel dropConfig))
			{
				continue;
			}
			foreach (List<uint> drop in dropConfig.Drop)
			{
				if (drop.Count >= 3 && drop[0] != 0 && drop[2] != 0 && GameData.DropGroupData.TryGetValue(drop[0], out DropGroupExcel group))
				{
					await ResolveGroup(multiplier: drop[2], player: player, group: group, random: random, sync: sync, rows: rows);
					group = null;
				}
			}
			dropConfig = null;
		}
	}

	private static async Task ResolveGroup(PlayerInstance player, DropGroupExcel group, uint multiplier, Random random, NtfSyncPlayer sync, JArray rows)
	{
		JToken groupRaw = group.GroupRaw;
		if (!(groupRaw is JArray groups))
		{
			return;
		}
		if (group.RandType == 1)
		{
			for (uint i = 0u; i < multiplier; i++)
			{
				JArray selected = SelectWeighted(groups, random);
				if (selected != null)
				{
					await AddReward(player, selected, 1u, sync, rows);
				}
			}
			return;
		}
		if (group.RandType == 2)
		{
			JArray selected2 = SelectWeighted(groups, random);
			JArray nested = default;
			int num;
			if (selected2 != null && selected2.Count >= 3)
			{
				groupRaw = selected2[2];
				nested = groupRaw as JArray;
				num = ((nested == null) ? 1 : 0);
			}
			else
			{
				num = 1;
			}
			if (num != 0)
			{
				return;
			}
			uint drawCount = ReadUInt(selected2[0]);
			for (uint i2 = 0u; i2 < multiplier * drawCount; i2++)
			{
				JArray nestedEntry = SelectWeighted(nested, random);
				if (nestedEntry != null)
				{
					await AddReward(player, nestedEntry, 1u, sync, rows);
				}
			}
			return;
		}
		foreach (JToken entry in groups)
		{
			if (entry is JArray rewardRow)
			{
				await AddReward(player, rewardRow, multiplier, sync, rows);
			}
		}
	}

	private static async Task AddReward(PlayerInstance player, JArray entry, uint multiplier, NtfSyncPlayer sync, JArray rows)
	{
		JArray gdpl = default;
		int num;
		if (entry.Count >= 3)
		{
			JToken jToken = entry[0];
			gdpl = jToken as JArray;
			if (gdpl != null)
			{
				num = ((gdpl.Count < 4) ? 1 : 0);
				goto IL_0052;
			}
		}
		num = 1;
		goto IL_0052;
		IL_0052:
		if (num == 0)
		{
			uint g = ReadUInt(gdpl[0]);
			uint d = ReadUInt(gdpl[1]);
			uint p = ReadUInt(gdpl[2]);
			uint l = ReadUInt(gdpl[3]);
			uint count = ReadUInt(entry[2]);
			if (g != 0 && d != 0 && p != 0 && l != 0 && count != 0)
			{
				count = checked(count * Math.Max(1u, multiplier));
				await player.RewardManager.GrantRewardAsync(new _003C_003Ez__ReadOnlyArray<uint>(new uint[5] { g, d, p, l, count }), sync);
				rows.Add(new JArray { g, d, p, l, count });
			}
		}
	}

	private static JArray? SelectWeighted(JArray entries, Random random)
	{
		JArray[] array = (from entry in entries.OfType<JArray>()
			where entry.Count >= 2 && ReadUInt(entry[1]) != 0
			select entry).ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		ulong num = array.Aggregate(0uL, (ulong sum, JArray entry) => sum + ReadUInt(entry[1]));
		if (num == 0)
		{
			return array[0];
		}
		ulong num2 = (ulong)(random.NextDouble() * (double)num);
		JArray[] array2 = array;
		foreach (JArray jArray in array2)
		{
			uint num4 = ReadUInt(jArray[1]);
			if (num2 < num4)
			{
				return jArray;
			}
			num2 -= num4;
		}
		return array[^1];
	}

	private static List<uint> ReadIds(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (node is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				uint num = ReadUInt(item);
				if (num != 0)
				{
					list.Add(num);
				}
			}
		}
		return list;
	}

	private static uint ReadUInt(JToken? token)
	{
		if (token == null)
		{
			return 0u;
		}
		uint result;
		return uint.TryParse(token.ToString(), out result) ? result : 0u;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<uint>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (uint)Math.Max(0L, value3);
			}
			if (jsonValue.TryGetValue<double>(out var value4))
			{
				return (uint)Math.Max(0.0, value4);
			}
		}
		return 0u;
	}
}
