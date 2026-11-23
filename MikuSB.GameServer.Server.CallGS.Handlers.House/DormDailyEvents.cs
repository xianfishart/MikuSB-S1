using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public static class DormDailyEvents
{
	private sealed class DailyTalkRow
	{
		public int GirlId { get; set; }

		public int StoryId { get; set; }

		public int CheckIn { get; set; }

		public int PreLevel { get; set; }

		public int PreStoryGirl { get; set; }

		public int PreStoryId { get; set; }

		public int CheckLoveLevel { get; set; }

		public int HigherOrder { get; set; }

		public List<List<int>> NeedItems { get; set; } = new List<List<int>>();

		public List<int> Furnitures { get; set; } = new List<int>();

		public DateTime? BeginTime { get; set; }

		public DateTime? EndTime { get; set; }
	}

	private const uint HouseGroupId = 101u;

	private const uint DailyEventOffset = 3u;

	private const uint ServerDailyEventDateGid = 999u;

	private static List<DailyTalkRow>? _talks;

	private static Dictionary<int, (int AreaId, int Index)>? _furniture;

	public static void Ensure(PlayerInstance player)
	{
		try
		{
			DateTime now = DateTime.Now;
			int num = int.Parse(((now.Hour < 4) ? now.Date.AddDays(-1.0) : now.Date).ToString("yyyyMMdd"));
			if (player.GetAttr(999u, 0u) == (uint)num)
			{
				return;
			}
			List<DailyTalkRow> list = LoadTalks();
			if (list.Count == 0)
			{
				return;
			}
			foreach (CharacterInfo character in player.CharacterManager.CharacterData.Characters)
			{
				int girlId = (int)((character.TemplateId >> 16) & 0xFFFF);
				if (girlId > 0)
				{
					player.SetAttr(101u, (uint)(girlId * 50 + 3), 0u);
					List<DailyTalkRow> list2 = (from x in list
						where x.GirlId == girlId && x.StoryId > 0 && IsEligible(player, girlId, x)
						orderby x.HigherOrder descending, x.StoryId
						select x).ToList();
					if (list2.Count != 0)
					{
						DailyTalkRow dailyTalkRow = list2[(num + girlId) % list2.Count];
						player.SetAttr(101u, (uint)(girlId * 50 + 3), (uint)dailyTalkRow.StoryId);
					}
				}
			}
			player.SetAttr(999u, 0u, (uint)num);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		catch (Exception value)
		{
			Console.Error.WriteLine($"DormDailyEvents.Ensure failed: {value}");
		}
	}

	public static (int GirlId, int EventId) Take(PlayerInstance player, int girlId)
	{
		if (girlId <= 0)
		{
			return (GirlId: 0, EventId: 0);
		}
		int attr = (int)player.GetAttr(101u, (uint)(girlId * 50 + 3));
		if (attr > 0)
		{
			player.SetAttr(101u, (uint)(girlId * 50 + 3), 0u);
		}
		return (GirlId: girlId, EventId: attr);
	}

	private static bool IsEligible(PlayerInstance player, int girlId, DailyTalkRow row)
	{
		if (row.CheckIn > 0)
		{
			uint attr = player.GetAttr(101u, (uint)(girlId * 50 + 1));
			if (attr == 0 || attr >= 100)
			{
				return false;
			}
		}
		if (row.PreLevel > 0 && player.GetAttr(22u, (uint)row.PreLevel) == 0)
		{
			return false;
		}
		if (row.PreStoryGirl > 0 && row.PreStoryId > 0)
		{
			uint attr2 = player.GetAttr(101u, (uint)(row.PreStoryGirl * 50 + 2));
			if ((attr2 & (uint)(1 << row.PreStoryId)) == 0)
			{
				return false;
			}
		}
		if (row.CheckLoveLevel > 0)
		{
			uint attr3 = player.GetAttr(101u, (uint)(girlId * 50));
			int num = (int)(attr3 & 0x7FF);
			if (num == 0)
			{
				num = 1;
			}
			if (num < row.CheckLoveLevel)
			{
				return false;
			}
		}
		foreach (List<int> needItem in row.NeedItems)
		{
			if (needItem.Count >= 4)
			{
				ulong id = GameResourceTemplateId.FromGdpl((uint)needItem[0], (uint)needItem[1], (uint)needItem[2], (uint)needItem[3]);
				BaseGameItemInfo baseGameItemInfo = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == id);
				if (baseGameItemInfo == null || baseGameItemInfo.ItemCount < 1)
				{
					return false;
				}
			}
		}
		foreach (int furniture in row.Furnitures)
		{
			if (!HasFurniture(player, furniture))
			{
				return false;
			}
		}
		if (row.BeginTime.HasValue && row.EndTime.HasValue)
		{
			DateTime now = DateTime.Now;
			if (now < row.BeginTime.Value || now > row.EndTime.Value)
			{
				return false;
			}
		}
		return true;
	}

	private static bool HasFurniture(PlayerInstance player, int tmpId)
	{
		if (_furniture == null)
		{
			_furniture = new Dictionary<int, (int, int)>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "support_furniture.json");
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item5 in jsonArray)
						{
							if (item5 is JsonObject row)
							{
								int num = Num(row, "FurnitureTmpId");
								int item = Num(row, "AreaId");
								int item2 = Num(row, "Index");
								if (num > 0)
								{
									_furniture[num] = (item, item2);
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
		if (!_furniture.TryGetValue(tmpId, out (int, int) value))
		{
			return false;
		}
		(int, int) tuple = value;
		int item3 = tuple.Item1;
		int item4 = tuple.Item2;
		uint attr = player.GetAttr(101u, item3 switch
		{
			51 => (uint)(10010 + item4 / 10), 
			60 => (uint)(15001 + item4 / 10), 
			_ => (uint)(Math.Max(0, item3) * 50 + 10 + item4 / 10), 
		});
		return (attr & (uint)(1 << item4 % 10 * 3)) != 0;
	}

	private static List<DailyTalkRow> LoadTalks()
	{
		if (_talks != null)
		{
			return _talks;
		}
		_talks = new List<DailyTalkRow>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "daily_talk.json");
			if (!File.Exists(path))
			{
				return _talks;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return _talks;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					_talks.Add(new DailyTalkRow
					{
						GirlId = Num(jsonObject, "GirlId"),
						StoryId = Num(jsonObject, "StoryId"),
						CheckIn = Num(jsonObject, "CheckIn"),
						PreLevel = Num(jsonObject, "PreLevel"),
						CheckLoveLevel = Num(jsonObject, "CheckLoveLevel"),
						HigherOrder = Num(jsonObject, "HigherOrder"),
						NeedItems = ReadRows(jsonObject["NeedItem"]),
						Furnitures = ReadInts(jsonObject["Furnitures"]),
						PreStoryGirl = ReadIntAt(jsonObject["PreStory"], 0),
						PreStoryId = ReadIntAt(jsonObject["PreStory"], 1),
						BeginTime = ParseTime(jsonObject["BeginTime"]),
						EndTime = ParseTime(jsonObject["EndTime"])
					});
				}
			}
		}
		catch
		{
		}
		return _talks;
	}

	public static int Num(JsonObject row, string key)
	{
		JsonNode jsonNode = row[key];
		if (!(jsonNode is JsonValue jsonValue))
		{
			return 0;
		}
		if (jsonValue.TryGetValue<int>(out var value))
		{
			return value;
		}
		if (jsonValue.TryGetValue<long>(out var value2))
		{
			return (int)value2;
		}
		if (jsonValue.TryGetValue<double>(out var value3))
		{
			return (int)value3;
		}
		int result;
		return int.TryParse(jsonValue.ToString(), out result) ? result : 0;
	}

	private static List<List<int>> ReadRows(JsonNode? node)
	{
		List<List<int>> list = new List<List<int>>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		if (jsonArray.Count > 0 && !(jsonArray[0] is JsonArray))
		{
			List<int> list2 = ReadInts(jsonArray);
			if (list2.Count >= 4)
			{
				list.Add(list2);
			}
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonArray node2)
			{
				List<int> list3 = ReadInts(node2);
				if (list3.Count >= 4)
				{
					list.Add(list3);
				}
			}
		}
		return list;
	}

	private static List<int> ReadInts(JsonNode? node)
	{
		List<int> list = new List<int>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
			{
				list.Add(value);
			}
		}
		return list;
	}

	private static int ReadIntAt(JsonNode? node, int index)
	{
		if (!(node is JsonArray jsonArray) || index >= jsonArray.Count)
		{
			return 0;
		}
		int value;
		return (jsonArray[index] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out value)) ? value : 0;
	}

	private static DateTime? ParseTime(JsonNode? node)
	{
		string text = node?.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text.Trim(new char[2] { '[', ']' }), "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}
}
