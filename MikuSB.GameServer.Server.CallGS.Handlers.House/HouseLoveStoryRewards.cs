using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public static class HouseLoveStoryRewards
{
	private sealed class StoryRow
	{
		public int GirlId { get; set; }

		public int Index { get; set; }

		public int StoryId { get; set; }

		public int Level { get; set; }

		public List<int[]> Rewards { get; set; } = new List<int[]>();
	}

	private const uint RewardFlagGid = 999u;

	private const uint RewardFlagSidBase = 100000u;

	private static Dictionary<(int Girl, int Index), StoryRow>? _byIndex;

	private static Dictionary<int, StoryRow>? _byStory;

	public static async Task<JsonArray> Claim(Connection connection, int girlId, int index, NtfSyncPlayer sync)
	{
		JsonArray rows = new JsonArray();
		StoryRow story = LoadByIndex().GetValueOrDefault((girlId, index));
		if (story == null || IsClaimed(connection.Player, story.StoryId))
		{
			return rows;
		}
		uint favor = HouseAttr.Read(connection.Player, (uint)(girlId * 50));
		int level = (int)(favor & 0x7FF);
		if (level == 0)
		{
			level = 1;
		}
		if (level < story.Level)
		{
			return rows;
		}
		foreach (int[] row in story.Rewards)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(connection.Player, sync, row.Select((int x) => (uint)x).ToList());
			rows.Add(new JsonArray
			{
				(uint)row[0],
				(uint)row[1],
				(uint)row[2],
				(uint)row[3],
				(uint)row[4]
			});
		}
		MarkClaimed(connection.Player, story.StoryId);
		DatabaseHelper.SaveDatabaseType(connection.Player.Data);
		TaskEventService.AddByType(connection.Player, 68, 1);
		return rows;
	}

	public static async Task<JsonArray> ClaimByStory(Connection connection, int storyId, NtfSyncPlayer sync)
	{
		StoryRow story = LoadByStory().GetValueOrDefault(storyId);
		if (story == null)
		{
			return new JsonArray();
		}
		return await Claim(connection, story.GirlId, story.Index, sync);
	}

	private static bool IsClaimed(PlayerInstance player, int storyId)
	{
		return player.Data.Attrs.Any((PlayerAttr x) => x.Gid == 999 && x.Sid == (uint)(100000 + storyId));
	}

	private static void MarkClaimed(PlayerInstance player, int storyId)
	{
		uint sid = (uint)(100000 + storyId);
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 999 && x.Sid == sid);
		if (playerAttr == null)
		{
			player.Data.Attrs.Add(new PlayerAttr
			{
				Gid = 999u,
				Sid = sid,
				Val = 1u
			});
		}
		else
		{
			playerAttr.Val = 1u;
		}
	}

	private static Dictionary<(int Girl, int Index), StoryRow> LoadByIndex()
	{
		if (_byIndex != null)
		{
			return _byIndex;
		}
		_byIndex = new Dictionary<(int, int), StoryRow>();
		foreach (StoryRow item in LoadAll())
		{
			_byIndex[(item.GirlId, item.Index)] = item;
		}
		return _byIndex;
	}

	private static Dictionary<int, StoryRow> LoadByStory()
	{
		if (_byStory != null)
		{
			return _byStory;
		}
		_byStory = new Dictionary<int, StoryRow>();
		foreach (StoryRow item in LoadAll())
		{
			if (item.StoryId > 0)
			{
				_byStory[item.StoryId] = item;
			}
		}
		return _byStory;
	}

	private static List<StoryRow> LoadAll()
	{
		List<StoryRow> list = new List<StoryRow>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "love_story.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				List<int[]> list2 = new List<int[]>();
				if (jsonObject["TbAward"] is JsonArray jsonArray2)
				{
					foreach (JsonNode item2 in jsonArray2)
					{
						if (item2 is JsonArray { Count: >=5 } jsonArray3)
						{
							int[] array = new int[5];
							for (int i = 0; i < 5; i++)
							{
								array[i] = ((jsonArray3[i] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value)) ? value : 0);
							}
							if (array[0] > 0)
							{
								list2.Add(array);
							}
						}
					}
				}
				list.Add(new StoryRow
				{
					GirlId = HouseJson.NumField(jsonObject, "GirlId"),
					Index = HouseJson.NumField(jsonObject, "Index"),
					StoryId = HouseJson.NumField(jsonObject, "StoryId"),
					Level = HouseJson.NumField(jsonObject, "Level"),
					Rewards = list2
				});
			}
		}
		catch
		{
		}
		return list;
	}
}
