using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_ReceiveProLevelAward")]
public class GirlCard_ReceiveProLevelAward : ICallGSHandler
{
	private sealed class ProLevelEntry
	{
		public uint Particular { get; init; }

		public int Ns { get; set; }

		public List<int> Tbn { get; set; }

		public List<int> Tbr { get; set; }

		public ProLevelEntry()
		{
			int num = 2;
			List<int> list = new List<int>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<int> span = CollectionsMarshal.AsSpan(list);
			span[0] = 0;
			span[1] = 0;
			Tbn = list;
			Tbr = new List<int>();
		}
	}

	private const uint SpineStrAttrGid = 30u;

	private const int MaxProLevel = 3;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int cardId = 0;
		int proLevel = 0;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			cardId = (req?["nID"]?.GetValue<int>()).GetValueOrDefault();
			proLevel = (req?["nProLevel"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (cardId <= 0 || proLevel < 0 || proLevel > 3)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_ReceiveProLevelAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)cardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_ReceiveProLevelAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint detail = (uint)((card.TemplateId >> 16) & 0xFFFF);
		uint particular = (uint)((card.TemplateId >> 32) & 0xFFFF);
		List<ProLevelEntry> list = ParseRoot(player, detail);
		ProLevelEntry entry = list.FirstOrDefault((ProLevelEntry x) => x.Particular == particular);
		if (entry == null)
		{
			entry = new ProLevelEntry
			{
				Particular = particular
			};
			list.Add(entry);
		}
		while (entry.Tbr.Count < 3)
		{
			entry.Tbr.Add(0);
		}
		List<List<uint>> rows = new List<List<uint>>();
		for (int stage = 1; stage <= 3; stage++)
		{
			if ((proLevel != 0 && stage != proLevel) || card.ProLevel < stage || entry.Tbr[stage - 1] > 0)
			{
				continue;
			}
			foreach (List<uint> award in ReadAwards(detail, particular, stage))
			{
				rows.Add(award);
			}
			entry.Tbr[stage - 1] = 1;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (List<uint> reward in rows)
		{
			if (reward.Count < 5 || reward[4] == 0)
			{
				continue;
			}
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			JsonArray arr = new JsonArray();
			foreach (uint v in reward)
			{
				arr.Add(v);
			}
			granted.Add(arr);
		}
		PlayerStrAttr strAttr = player.Attributes.SetString(30u, detail, SerializeList(list));
		player.Attributes.SyncTo(sync, strAttr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer strSync = new NtfSyncPlayer();
		player.Attributes.SyncTo(strSync, strAttr);
		await player.SendPacket(1004, strSync);
		await player.SendPacket(1012, new NtfSetStrAttr
		{
			Gid = 30u,
			Sid = detail,
			Val = strAttr.Val
		});
		JsonObject rsp = new JsonObject { ["tbAward"] = granted };
		await CallGSRouter.SendScript(connection, "GirlCard_ReceiveProLevelAward", rsp.ToJsonString(), sync);
	}

	private static List<ProLevelEntry> ParseRoot(PlayerInstance player, uint detail)
	{
		List<ProLevelEntry> list = new List<ProLevelEntry>();
		string stringValue = player.Attributes.GetStringValue(30u, detail);
		if (string.IsNullOrWhiteSpace(stringValue))
		{
			return list;
		}
		try
		{
			JsonNode jsonNode = JsonNode.Parse(stringValue);
			if (jsonNode is JsonArray jsonArray)
			{
				for (int i = 0; i < jsonArray.Count; i++)
				{
					if (jsonArray[i] is JsonObject obj)
					{
						list.Add(ReadEntry(obj, (uint)(i + 1)));
					}
				}
			}
			else if (jsonNode is JsonObject jsonObject)
			{
				foreach (var (s, jsonNode3) in jsonObject)
				{
					if (jsonNode3 is JsonObject obj2 && uint.TryParse(s, out var result))
					{
						list.Add(ReadEntry(obj2, result));
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static ProLevelEntry ReadEntry(JsonObject obj, uint particular)
	{
		ProLevelEntry proLevelEntry = new ProLevelEntry
		{
			Particular = particular
		};
		if (obj["ns"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
		{
			proLevelEntry.Ns = value;
		}
		if (obj["tbn"] is JsonArray jsonArray)
		{
			proLevelEntry.Tbn = new List<int>();
			foreach (JsonNode item in jsonArray)
			{
				proLevelEntry.Tbn.Add(int.TryParse(item?.ToString(), out var result) ? result : 0);
			}
		}
		if (obj["tbr"] is JsonArray jsonArray2)
		{
			proLevelEntry.Tbr = new List<int>();
			foreach (JsonNode item2 in jsonArray2)
			{
				proLevelEntry.Tbr.Add(int.TryParse(item2?.ToString(), out var result2) ? result2 : 0);
			}
		}
		return proLevelEntry;
	}

	private static string SerializeList(List<ProLevelEntry> list)
	{
		int num = (int)((list.Count != 0) ? list.Max((ProLevelEntry x) => x.Particular) : 0);
		JsonArray jsonArray = new JsonArray();
		int i;
		for (i = 1; i <= num; i++)
		{
			ProLevelEntry proLevelEntry = list.FirstOrDefault((ProLevelEntry x) => x.Particular == i);
			if (proLevelEntry == null)
			{
				jsonArray.Add(null);
				continue;
			}
			JsonArray jsonArray2 = new JsonArray();
			foreach (int item in proLevelEntry.Tbn)
			{
				jsonArray2.Add(item);
			}
			JsonArray jsonArray3 = new JsonArray();
			foreach (int item2 in proLevelEntry.Tbr)
			{
				jsonArray3.Add(item2);
			}
			jsonArray.Add(new JsonObject
			{
				["ns"] = proLevelEntry.Ns,
				["tbn"] = jsonArray2,
				["tbr"] = jsonArray3
			});
		}
		return jsonArray.ToJsonString();
	}

	private static IEnumerable<List<uint>> ReadAwards(uint detail, uint particular, int stage)
	{
		List<List<uint>> list = new List<List<uint>>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "item";
			buffer[3] = "card";
			buffer[4] = "prolevel.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || !(jsonObject["GDPL"] is JsonArray { Count: >=3 } jsonArray2))
				{
					continue;
				}
				int num = (int.TryParse(jsonArray2[1]?.ToString(), out var result) ? result : 0);
				int num2 = (int.TryParse(jsonArray2[2]?.ToString(), out var result2) ? result2 : 0);
				if (num != (int)detail || num2 != (int)particular)
				{
					continue;
				}
				if (!(jsonObject[$"Award{stage}"] is JsonArray jsonArray3))
				{
					return list;
				}
				foreach (JsonNode item2 in jsonArray3)
				{
					if (!(item2 is JsonArray { Count: >=5 } jsonArray4))
					{
						continue;
					}
					List<uint> list2 = new List<uint>();
					foreach (JsonNode item3 in jsonArray4)
					{
						list2.Add(uint.TryParse(item3?.ToString(), out var result3) ? result3 : 0u);
					}
					if (list2.Count >= 5)
					{
						list.Add(list2);
					}
				}
				return list;
			}
		}
		catch
		{
		}
		return list;
	}
}
