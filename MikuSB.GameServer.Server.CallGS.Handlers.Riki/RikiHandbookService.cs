using System;
using System.Collections.Generic;
using System.IO;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Riki;

internal static class RikiHandbookService
{
	private const uint GroupId = 103u;

	private static readonly uint[] ForceUnlockTypes = new uint[6] { 4u, 7u, 9u, 10u, 12u, 13u };

	private static List<(uint Id, ulong Template)>? _map;

	private static List<uint>? _forceIds;

	public static int Ensure(PlayerInstance player, NtfSyncPlayer? sync)
	{
		HashSet<ulong> hashSet = OwnedTemplates(player);
		if (hashSet.Count == 0)
		{
			return 0;
		}
		int num = 0;
		foreach (var (id, item) in Map())
		{
			if (hashSet.Contains(item) && TrySet(player, id, sync))
			{
				num++;
			}
		}
		if (num > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return num;
	}

	public static int UnlockForceTypes(PlayerInstance player, NtfSyncPlayer? sync)
	{
		int num = 0;
		foreach (uint item in ForceIds())
		{
			if (TrySet(player, item, sync))
			{
				num++;
			}
		}
		if (num > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return num;
	}

	private static bool TrySet(PlayerInstance player, uint id, NtfSyncPlayer? sync)
	{
		uint num = id / 1000;
		if (num == 0)
		{
			return false;
		}
		uint num2 = id % 1000;
		uint sid = (num - 1) * 30 + (uint)Math.Ceiling((double)num2 / 30.0);
		uint num3 = num2 % 30;
		PlayerAttr orCreate = player.Attributes.GetOrCreate(103u, sid);
		uint num4 = (uint)(1 << (int)num3);
		if ((orCreate.Val & num4) != 0)
		{
			return false;
		}
		orCreate.Val |= num4;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, orCreate);
		}
		return true;
	}

	private static HashSet<ulong> OwnedTemplates(PlayerInstance player)
	{
		HashSet<ulong> hashSet = new HashSet<ulong>();
		foreach (CharacterInfo character in player.CharacterManager.CharacterData.Characters)
		{
			if (character.TemplateId != 0)
			{
				hashSet.Add(character.TemplateId);
			}
		}
		foreach (BaseGameItemInfo value in player.InventoryManager.InventoryData.Items.Values)
		{
			if (value.TemplateId != 0)
			{
				hashSet.Add(value.TemplateId);
			}
		}
		foreach (GameWeaponInfo value2 in player.InventoryManager.InventoryData.Weapons.Values)
		{
			if (value2.TemplateId != 0)
			{
				hashSet.Add(value2.TemplateId);
			}
		}
		foreach (GameSkinInfo value3 in player.InventoryManager.InventoryData.Skins.Values)
		{
			if (value3.TemplateId != 0)
			{
				hashSet.Add(value3.TemplateId);
			}
		}
		foreach (GameSupportCardInfo value4 in player.InventoryManager.InventoryData.SupportCards.Values)
		{
			if (value4.TemplateId != 0)
			{
				hashSet.Add(value4.TemplateId);
			}
		}
		return hashSet;
	}

	private static List<(uint, ulong)> Map()
	{
		if (_map != null)
		{
			return _map;
		}
		_map = new List<(uint, ulong)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "riki", "riki.json");
			if (!File.Exists(path))
			{
				return _map;
			}
			foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
			{
				if (!(item is JObject jObject) || !uint.TryParse(jObject["Id"]?.ToString(), out var result) || result == 0)
				{
					continue;
				}
				foreach (ulong item2 in ReadConditions(jObject["Condition"]))
				{
					_map.Add((result, item2));
				}
			}
		}
		catch
		{
		}
		return _map;
	}

	private static List<uint> ForceIds()
	{
		if (_forceIds != null)
		{
			return _forceIds;
		}
		_forceIds = new List<uint>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "riki", "riki.json");
			if (!File.Exists(path))
			{
				return _forceIds;
			}
			foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
			{
				if (item is JObject jObject && uint.TryParse(jObject["Type"]?.ToString(), out var result) && Array.IndexOf(ForceUnlockTypes, result) >= 0 && uint.TryParse(jObject["Id"]?.ToString(), out var result2) && result2 != 0)
				{
					_forceIds.Add(result2);
				}
			}
		}
		catch
		{
		}
		return _forceIds;
	}

	private static IEnumerable<ulong> ReadConditions(JToken? condition)
	{
		if (!(condition is JArray array))
		{
			yield break;
		}
		if (array.Count >= 4 && array[0] is JValue)
		{
			ulong gdpl = ReadGdpl(array);
			if (gdpl != 0)
			{
				yield return gdpl;
			}
			yield break;
		}
		foreach (JToken row in array)
		{
			JArray inner = row as JArray;
			if (inner != null && inner.Count >= 4 && inner[0] is JValue)
			{
				ulong gdpl2 = ReadGdpl(inner);
				if (gdpl2 != 0)
				{
					yield return gdpl2;
				}
			}
		}
	}

	private static ulong ReadGdpl(JArray array)
	{
		uint num = Read(array[0]);
		uint detail = Read(array[1]);
		uint particular = Read(array[2]);
		uint level = Read(array[3]);
		return (num == 0) ? 0 : GameResourceTemplateId.FromGdpl(num, detail, particular, level);
		static uint Read(JToken? token)
		{
			uint result;
			return (token != null && uint.TryParse(token.ToString(), out result)) ? result : 0u;
		}
	}
}
