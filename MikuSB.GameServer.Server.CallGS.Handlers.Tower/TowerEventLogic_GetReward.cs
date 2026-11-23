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

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerEventLogic_GetReward")]
public class TowerEventLogic_GetReward : ICallGSHandler
{
	private const uint ClaimGid = 13u;

	private static readonly Dictionary<int, JsonObject> _award = new Dictionary<int, JsonObject>();

	private static bool _awardLoaded;

	private static readonly Dictionary<int, JsonObject> _chapters = new Dictionary<int, JsonObject>();

	private static bool _chaptersLoaded;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		try
		{
			JsonObject req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject;
			int chapterId = ReadInt(req?["nChapterID"]);
			int group = ReadInt(req?["nGroup"]);
			if (chapterId <= 0)
			{
				return;
			}
			JsonObject conf = LoadAward(chapterId);
			int passed = CountPassed(player, chapterId);
			PlayerAttr claimAttr = GetOrCreate(player, 13u, (uint)chapterId);
			for (int tier = 1; tier <= 3; tier++)
			{
				if (group > 0 && group != tier)
				{
					continue;
				}
				int need = ReadInt(conf?[$"LevelCount{tier}"]);
				if (need <= 0)
				{
					need = 1;
				}
				if (passed < need || ((claimAttr.Val >> tier * 4) & 0xF) != 0)
				{
					continue;
				}
				JsonNode jsonNode = conf?[$"Award{tier}"];
				if (jsonNode is JsonArray rows)
				{
					foreach (JsonNode node in rows)
					{
						JsonArray row = node as JsonArray;
						if (row == null || row.Count < 5)
						{
							continue;
						}
						List<uint> values = new List<uint>();
						foreach (JsonNode item in row)
						{
							values.Add(uint.TryParse(item?.ToString(), out var n) ? n : 0u);
						}
						if (values.Count < 5 || values[0] == 0 || values[4] == 0)
						{
							continue;
						}
						await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
						JsonArray copy = new JsonArray();
						foreach (uint v in values)
						{
							copy.Add(v);
						}
						rewards.Add(copy);
					}
				}
				claimAttr.Val |= (uint)(15 << tier * 4);
			}
			sync.Custom[player.ToPackedAttrKey(13u, (uint)chapterId)] = claimAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(13u, (uint)chapterId)] = claimAttr.Val;
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		catch
		{
		}
		JsonObject rsp = new JsonObject { ["tbRewards"] = rewards };
		await CallGSRouter.SendScript(connection, "TowerEventLogic_GetReward", rsp.ToJsonString(), sync);
	}

	private static JsonObject? LoadAward(int chapterId)
	{
		if (!_awardLoaded)
		{
			_awardLoaded = true;
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "tower_event";
				buffer[4] = "award.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject)
							{
								int num = ReadInt(jsonObject["ChapterID"] ?? jsonObject["ID"]);
								if (num > 0)
								{
									_award[num] = jsonObject;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _award.GetValueOrDefault(chapterId);
	}

	private static int CountPassed(PlayerInstance player, int chapterId)
	{
		if (!_chaptersLoaded)
		{
			_chaptersLoaded = true;
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "tower_event";
				buffer[4] = "chapter.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject)
							{
								int num = ReadInt(jsonObject["ID"]);
								if (num > 0)
								{
									_chapters[num] = jsonObject;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		if (!_chapters.TryGetValue(chapterId, out JsonObject value))
		{
			return 0;
		}
		if (!(value["Level"] is JsonArray jsonArray2))
		{
			return 0;
		}
		int num2 = 0;
		foreach (JsonNode item2 in jsonArray2)
		{
			if (uint.TryParse(item2?.ToString(), out var result) && player.GetAttr(22u, result) != 0)
			{
				num2++;
			}
		}
		return num2;
	}

	private static PlayerAttr GetOrCreate(PlayerInstance player, uint gid, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid,
			Val = 0u
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
