using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Repression_Chapter_STAR_AWARD")]
public class Repression_Chapter_STAR_AWARD : ICallGSHandler
{
	private const uint GroupId = 31u;

	private const uint ClaimedMaskSid = 2u;

	private const uint TotalScoreSid = 3u;

	private const uint CurrentActSid = 6u;

	private static JsonArray? _awards;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<int> requested = ParseIndexes(param);
		int actId = (int)(player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 31 && x.Sid == 6)?.Val ?? 0);
		if (actId <= 0)
		{
			actId = RepressionState.ResolveActId(player);
		}
		List<JsonObject> rows = (from JsonObject jsonObject in from node in LoadAwards()
				where node is JsonObject jsonObject && ReadUInt(jsonObject["Ver"]) == (uint)actId
				select node
			orderby ReadUInt(jsonObject["Score"])
			select jsonObject).ToList();
		if (rows.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Repression_Chapter_STAR_AWARD", "{\"tbAward\":[]}");
			return;
		}
		uint score = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 31 && x.Sid == 3)?.Val ?? 0;
		PlayerAttr maskAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 31 && x.Sid == 2);
		if (maskAttr == null)
		{
			maskAttr = new PlayerAttr
			{
				Gid = 31u,
				Sid = 2u,
				Val = 0u
			};
			player.Data.Attrs.Add(maskAttr);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		bool changed = false;
		for (int i = 0; i < rows.Count; i++)
		{
			int index = i + 1;
			if (requested.Count > 0 && !requested.Contains(index))
			{
				continue;
			}
			uint threshold = ReadUInt(rows[i]["Score"]);
			if (score < threshold)
			{
				continue;
			}
			uint bit = (uint)(1 << index);
			if ((maskAttr.Val & bit) != 0)
			{
				continue;
			}
			maskAttr.Val |= bit;
			changed = true;
			JsonNode jsonNode = rows[i]["Award"];
			if (!(jsonNode is JsonArray awardRows))
			{
				continue;
			}
			foreach (JsonNode rowNode in awardRows)
			{
				if (!(rowNode is JsonArray row))
				{
					continue;
				}
				List<uint> list = new List<uint>();
				foreach (JsonNode value in row)
				{
					list.Add(ReadUInt(value));
				}
				if (list.Count <= 0)
				{
					continue;
				}
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, list);
				JsonArray echo = new JsonArray();
				foreach (uint value2 in list)
				{
					echo.Add(value2);
				}
				rewards.Add(echo);
			}
		}
		if (changed)
		{
			sync.Custom[player.ToPackedAttrKey(31u, 2u)] = maskAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(31u, 2u)] = maskAttr.Val;
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		await CallGSRouter.SendScript(connection, "Repression_Chapter_STAR_AWARD", new JsonObject { ["tbAward"] = rewards }.ToJsonString(), sync);
	}

	private static List<int> ParseIndexes(string param)
	{
		List<int> list = new List<int>();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "[]" : param);
			if (jsonNode is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					list.Add((int)ReadUInt(item));
				}
			}
			else if (jsonNode is JsonObject jsonObject)
			{
				foreach (KeyValuePair<string, JsonNode> item2 in jsonObject)
				{
					list.Add((int)ReadUInt(item2.Value));
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static JsonArray LoadAwards()
	{
		if (_awards != null)
		{
			return _awards;
		}
		_awards = new JsonArray();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "Repression";
			buffer[4] = "award.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						_awards.Add(jsonObject.DeepClone());
					}
				}
			}
		}
		catch
		{
		}
		return _awards;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return (uint)Math.Max(0.0, value);
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (uint)Math.Max(0L, value3);
			}
		}
		return 0u;
	}
}
