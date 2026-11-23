using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal static class Rogue3DProgress
{
	private const uint Gid = 124u;

	private const uint ExpSid = 2u;

	private const uint LevelSid = 3u;

	private const uint LevelPassStartSid = 20u;

	private const uint LevelPassEndSid = 30u;

	private const uint LevelAwardClaimBaseSid = 4000u;

	private const uint QuestGid = 7u;

	private static Dictionary<uint, uint>? _needExp;

	public static Dictionary<uint, uint> NeedExp
	{
		get
		{
			if (_needExp != null)
			{
				return _needExp;
			}
			_needExp = new Dictionary<uint, uint>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "dlc";
				buffer[3] = "rogue3d";
				buffer[4] = "server_05_levelaward_list.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
					{
						if (item is JsonObject jsonObject)
						{
							uint num = jsonObject["Level"]?.GetValue<uint>() ?? 0;
							uint num2 = jsonObject["NeedExp"]?.GetValue<uint>() ?? 0;
							if (num != 0)
							{
								_needExp[num] = ((num2 == 0) ? 1000u : num2);
							}
						}
					}
				}
			}
			catch
			{
			}
			return _needExp;
		}
	}

	public static uint MaxLevel => (NeedExp.Count == 0) ? 120u : NeedExp.Keys.Max();

	public static uint CurrentLevel(PlayerInstance player)
	{
		uint value = player.Attributes.GetValue(124u, 3u);
		return (value == 0) ? 1u : value;
	}

	public static uint CurrentExp(PlayerInstance player)
	{
		return player.Attributes.GetValue(124u, 2u);
	}

	public static void EnsureInitialized(PlayerInstance player, NtfSyncPlayer sync)
	{
		if (player.Attributes.GetValue(124u, 3u) == 0)
		{
			SetAttr(player, 3u, 1u, sync);
		}
		if (player.Attributes.GetValue(124u, 2u) == 0 && !HasAttr(player, 2u))
		{
			SetAttr(player, 2u, 0u, sync);
		}
	}

	private static bool HasAttr(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.Any((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
	}

	public static async Task<(JsonObject Payload, NtfSyncPlayer Sync)> HandleSettlementAsync(PlayerInstance player, JsonNode? tbParam, bool season)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject payload = (tbParam?.DeepClone() as JsonObject) ?? new JsonObject();
		payload["tbLevelAward"] = new JsonArray();
		payload["tbMonsterDropRiki"] = new JsonArray();
		uint diff = ReadUint(tbParam, "nDiffId");
		if (!(tbParam is JsonObject source) || !source.TryGetPropertyValue("tbFightData", out JsonNode fight) || fight == null || diff == 0)
		{
			return (Payload: payload, Sync: sync);
		}
		if (diff >= 20 && diff <= 30)
		{
			PlayerAttr attr = player.Attributes.Add(124u, diff, 1u);
			player.Attributes.SyncTo(sync, attr);
		}
		List<(uint, uint, uint, uint)> members = ReadMembers(tbParam);
		AddExp(player, 120 + 80 * Math.Min(diff, 20u), sync);
		AdvanceQuests(player, diff, members, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return (Payload: payload, Sync: sync);
	}

	public static void AddExp(PlayerInstance player, uint gain, NtfSyncPlayer sync)
	{
		uint num = CurrentLevel(player);
		uint num2 = CurrentExp(player) + gain;
		uint value;
		for (uint maxLevel = MaxLevel; num < maxLevel && NeedExp.TryGetValue(num, out value); num++)
		{
			if (value == 0)
			{
				break;
			}
			if (num2 < value)
			{
				break;
			}
			num2 -= value;
		}
		SetAttr(player, 2u, num2, sync);
		SetAttr(player, 3u, num, sync);
	}

	public static bool IsLevelAwardClaimed(PlayerInstance player, uint level)
	{
		return (player.Attributes.GetValue(124u, 4000 + level) & 1) != 0;
	}

	public static void MarkLevelAwardClaimed(PlayerInstance player, uint level, NtfSyncPlayer sync)
	{
		SetAttr(player, 4000 + level, 1u, sync);
	}

	private static void AdvanceQuests(PlayerInstance player, uint diff, List<(uint G, uint D, uint P, uint L)> members, NtfSyncPlayer sync)
	{
		HashSet<uint> hashSet = new HashSet<uint>();
		foreach (var member in members)
		{
			uint g = member.G;
			uint d = member.D;
			uint p = member.P;
			uint l = member.L;
			List<uint> list = GameData.CardData.Values.FirstOrDefault((CardExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == GameResourceTemplateId.FromGdpl(g, d, p, l))?.DefaultWeaponGPDL;
			if (list != null && list.Count >= 2)
			{
				hashSet.Add(list[1]);
			}
		}
		foreach (ActivityQuestExcel value in GameData.ActivityQuestData.Values)
		{
			uint type = value.Type;
			bool flag = ((type - 159 <= 1 || type - 163 <= 2) ? true : false);
			if (!flag || value.CheckValue == 0)
			{
				continue;
			}
			if (1 == 0)
			{
			}
			flag = type switch
			{
				164u => MatchPair(value, members.Count, diff), 
				165u => MatchWeapon(value, hashSet, diff), 
				_ => true, 
			};
			if (1 == 0)
			{
			}
			if (!flag)
			{
				continue;
			}
			PlayerAttr orCreate = player.Attributes.GetOrCreate(7u, value.Id);
			if ((orCreate.Val & 1) == 0)
			{
				uint num = orCreate.Val >> 1;
				if (num < value.CheckValue)
				{
					flag = ((type - 159 <= 1 || type == 163) ? true : false);
					uint num2 = (flag ? Math.Min(value.CheckValue, num + 1) : value.CheckValue);
					orCreate.Val = (orCreate.Val & 1) | (num2 << 1);
					player.Attributes.SyncTo(sync, orCreate);
				}
			}
		}
	}

	private static bool MatchPair(ActivityQuestExcel quest, int memberCount, uint diff)
	{
		if (!(quest.Param is JArray { Count: >=2 } jArray))
		{
			return false;
		}
		return memberCount == ReadJInt(jArray[0]) && diff >= (uint)ReadJInt(jArray[1]);
	}

	private static bool MatchWeapon(ActivityQuestExcel quest, HashSet<uint> weapons, uint diff)
	{
		if (!(quest.Param is JArray { Count: >=2 } jArray))
		{
			return false;
		}
		return diff >= (uint)ReadJInt(jArray[1]) && weapons.Contains((uint)ReadJInt(jArray[0]));
	}

	private static List<(uint, uint, uint, uint)> ReadMembers(JsonNode? node)
	{
		List<(uint, uint, uint, uint)> list = new List<(uint, uint, uint, uint)>();
		if (!(node is JsonObject jsonObject) || !(jsonObject["tbMember"] is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonArray { Count: >=4 } jsonArray2)
			{
				list.Add(((uint)ReadJInt(jsonArray2[0]), (uint)ReadJInt(jsonArray2[1]), (uint)ReadJInt(jsonArray2[2]), (uint)ReadJInt(jsonArray2[3])));
			}
		}
		return list;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(124u, sid);
		orCreate.Val = value;
		player.Attributes.SyncTo(sync, orCreate);
	}

	private static uint ReadUint(JsonNode? node, string key)
	{
		int result2;
		if (node is JsonObject jsonObject)
		{
			JsonNode jsonNode = jsonObject[key];
			if (jsonNode != null && uint.TryParse(jsonNode.ToString(), out var result))
			{
				result2 = (int)result;
				goto IL_0028;
			}
		}
		result2 = 0;
		goto IL_0028;
		IL_0028:
		return (uint)result2;
	}

	private static int ReadJInt(JsonNode? node)
	{
		int result;
		return (node != null && int.TryParse(node.ToString(), out result)) ? result : 0;
	}

	private static int ReadJInt(JToken? token)
	{
		int result;
		return (token != null && int.TryParse(token.ToString(), out result)) ? result : 0;
	}
}
