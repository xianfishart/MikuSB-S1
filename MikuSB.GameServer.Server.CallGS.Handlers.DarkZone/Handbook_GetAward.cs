using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("Handbook_GetAward")]
public class Handbook_GetAward : ICallGSHandler
{
	private const uint Gid = 108u;

	private static List<JsonObject>? _rows;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int index = 0;
		try
		{
			index = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nIndex"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		JsonArray rewards = new JsonArray();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject row = Rows().FirstOrDefault((JsonObject x) => ReadInt(x["Index"]) == index);
		if (row != null && index > 0 && HasAllNeedItems(player, row))
		{
			uint sid = (uint)(index / 30 + 1);
			uint bit = (uint)index % 30u;
			PlayerAttr attr = player.Attributes.GetOrCreate(108u, sid);
			if ((attr.Val & (uint)(1 << (int)bit)) == 0)
			{
				JsonNode jsonNode = row["AwardItems"];
				if (jsonNode is JsonArray awardRows)
				{
					List<IReadOnlyList<uint>> parsed = new List<IReadOnlyList<uint>>();
					foreach (JsonNode award in awardRows)
					{
						JsonArray cells = award as JsonArray;
						if (cells == null || cells.Count < 5)
						{
							continue;
						}
						List<uint> values = new List<uint>(5);
						foreach (JsonNode item in cells.Take(5))
						{
							values.Add(uint.TryParse(item?.ToString(), out var v) ? v : 0u);
						}
						if (values.Count == 5 && values[0] != 0 && values[4] != 0)
						{
							parsed.Add(values);
						}
					}
					if (parsed.Count > 0)
					{
						foreach (JsonNode item2 in await player.RewardManager.GrantConfiguredRewardsAsync(parsed, sync))
						{
							rewards.Add(item2?.DeepClone());
						}
					}
				}
				attr.Val |= (uint)(1 << (int)bit);
				player.Attributes.SyncTo(sync, attr);
				DatabaseHelper.SaveDatabaseType(player.Data);
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
		}
		await CallGSRouter.SendScript(connection, "Handbook_GetAward", new JsonObject
		{
			["nIndex"] = index,
			["tbRewards"] = rewards
		}.ToJsonString(), sync);
	}

	private static bool HasAllNeedItems(PlayerInstance player, JsonObject row)
	{
		if (!(row["NeedItems"] is JsonArray jsonArray))
		{
			return true;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonArray { Count: >=4 } jsonArray2)
			{
				uint num = ((jsonArray2.Count < 5 || !uint.TryParse(jsonArray2[4]?.ToString(), out var result)) ? 1u : result);
				ulong templateId = GameResourceTemplateId.FromGdpl(ParseUint(jsonArray2[0]), ParseUint(jsonArray2[1]), ParseUint(jsonArray2[2]), ParseUint(jsonArray2[3]));
				long num2 = player.InventoryManager.InventoryData.Items.Values.Where((BaseGameItemInfo x) => x.TemplateId == templateId).Sum((BaseGameItemInfo x) => x.ItemCount);
				if (num2 < num)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static uint ParseUint(JsonNode? node)
	{
		uint result;
		return (node != null && uint.TryParse(node.ToString(), out result)) ? result : 0u;
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return (node != null && int.TryParse(node.ToString(), out result)) ? result : 0;
	}

	private static List<JsonObject> Rows()
	{
		if (_rows != null)
		{
			return _rows;
		}
		_rows = new List<JsonObject>();
		try
		{
			InlineArray6<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "darkzone";
			buffer[4] = "activity";
			buffer[5] = "handbook.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JsonNode item2 in JsonNode.Parse(File.ReadAllText(path)).AsArray())
				{
					if (item2 is JsonObject item)
					{
						_rows.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		return _rows;
	}
}
