using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_GetLevelAward")]
public class Rogue3D_GetLevelAward : ICallGSHandler
{
	private static Dictionary<uint, List<uint>>? _awards;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<uint> requested = new List<uint>();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["tbIdList"];
			if (jsonNode is JsonArray arr)
			{
				foreach (JsonNode item in arr)
				{
					if (uint.TryParse(item?.ToString(), out var level) && level != 0)
					{
						requested.Add(level);
					}
				}
			}
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		Rogue3DProgress.EnsureInitialized(player, sync);
		JsonArray granted = new JsonArray();
		uint currentLevel = Rogue3DProgress.CurrentLevel(player);
		Dictionary<uint, List<uint>> awards = Awards();
		foreach (uint level2 in requested.Distinct())
		{
			if (level2 > currentLevel || Rogue3DProgress.IsLevelAwardClaimed(player, level2) || !awards.TryGetValue(level2, out List<uint> row) || row.Count < 5)
			{
				continue;
			}
			List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>> { row };
			foreach (JsonNode item2 in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
			{
				granted.Add(item2?.DeepClone());
			}
			Rogue3DProgress.MarkLevelAwardClaimed(player, level2, sync);
			row = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "Rogue3D_GetLevelAward", new JsonObject { ["tbAwardList"] = granted }.ToJsonString(), sync);
	}

	private static Dictionary<uint, List<uint>> Awards()
	{
		if (_awards != null)
		{
			return _awards;
		}
		_awards = new Dictionary<uint, List<uint>>();
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
					if (!(item is JsonObject jsonObject))
					{
						continue;
					}
					JsonNode jsonNode = jsonObject["Level"];
					if (jsonNode == null || !uint.TryParse(jsonNode.ToString(), out var result) || !(jsonObject["AwardList"] is JsonArray { Count: >=5 } jsonArray))
					{
						continue;
					}
					List<uint> list = new List<uint>(5);
					foreach (JsonNode item2 in jsonArray.Take(5))
					{
						list.Add(uint.TryParse(item2?.ToString(), out var result2) ? result2 : 0u);
					}
					if (list[0] != 0 && list[4] != 0)
					{
						_awards[result] = list;
					}
				}
			}
		}
		catch
		{
		}
		return _awards;
	}
}
