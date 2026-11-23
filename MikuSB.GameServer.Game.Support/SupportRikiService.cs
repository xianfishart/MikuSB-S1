using System.Collections.Generic;
using System.Text.Json;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Support;

public static class SupportRikiService
{
	public static void Sync(PlayerInstance player, NtfSyncPlayer? sync = null)
	{
		Dictionary<string, uint> dictionary = new Dictionary<string, uint>();
		foreach (GameSupportCardInfo value3 in player.InventoryManager.InventoryData.SupportCards.Values)
		{
			uint num = (uint)(value3.TemplateId & 0xFFFF);
			uint num2 = (uint)((value3.TemplateId >> 16) & 0xFFFF);
			uint num3 = (uint)((value3.TemplateId >> 32) & 0xFFFF);
			if (num != 0 && num2 != 0 && num3 != 0)
			{
				string key = $"{num}-{num2}-{num3}";
				if (!dictionary.TryGetValue(key, out var value) || value3.Level > value)
				{
					dictionary[key] = value3.Level;
				}
			}
		}
		string value2 = JsonSerializer.Serialize(dictionary);
		player.SetStrAttr(41u, 1u, value2);
		if (sync != null)
		{
			sync.CustomStr[player.ToShiftedAttrKey(41u, 1u)] = value2;
			sync.CustomStr[player.ToPackedAttrKey(41u, 1u)] = value2;
		}
	}
}
