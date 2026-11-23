using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public static class DormPuzzleService
{
	public sealed class MapRow
	{
		public int MapId { get; set; }

		public int Weight { get; set; }

		public List<int[]> CompleteReward { get; set; } = new List<int[]>();

		public List<int[]> PercentReward { get; set; } = new List<int[]>();
	}

	public sealed class ClipRow
	{
		public int ClipId { get; set; }

		public int DefaultNum { get; set; }
	}

	private const uint Gid = 101u;

	private const uint HouseInfoStart = 3000u;

	private const uint PuzzleMapStateOffset = 61u;

	private const uint PuzzleMapIdOffset = 62u;

	private const uint PuzzleRefreshTimeOffset = 63u;

	private const uint PuzzleFreeRefreshOffset = 168u;

	private const uint PuzzleBuyRefreshOffset = 173u;

	private const uint ClipCountStartOffset = 30u;

	private const uint WeeklyMarkerGid = 999u;

	private const uint WeeklyMarkerSid = 1u;

	private static List<MapRow>? _maps;

	private static List<ClipRow>? _clips;

	public static void Ensure(PlayerInstance player)
	{
		try
		{
			uint num = uint.Parse(DateTime.Now.ToString("yyyyMMdd"));
			uint num2 = num;
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 999 && x.Sid == 1);
			HouseDropService.Ensure(player);
			if (playerAttr != null && playerAttr.Val == num2)
			{
				EnsurePuzzleMap(player);
				return;
			}
			player.SetAttr(101u, 3063u, 2u);
			player.SetAttr(101u, 3168u, 2u);
			player.SetAttr(101u, 3173u, 0u);
			player.SetAttr(101u, 63050u, 0u);
			// 2026-11-13 追加（修复记录第 296 节）：3166 = SetWishClipTimes，
			// 客户端 uw_dorm_jigsaw_give 的许愿按钮拿它当「今天是否已许愿」的判据（只判断 != 0，
			// 非 0 就弹 ui.ClipSetTimeOut 并且不打开 DormClipWish）。SetWishClipId 会把它写成
			// yyyyMMdd，所以必须跟着每日重置一起清零，否则许过愿的角色再也改不了关键断片。
			player.SetAttr(101u, 3166u, 0u);
			if (player.GetAttr(101u, 3062u) == 0)
			{
				player.SetAttr(101u, 3062u, 1u);
			}
			SeedClips(player);
			if (playerAttr == null)
			{
				player.Data.Attrs.Add(new PlayerAttr
				{
					Gid = 999u,
					Sid = 1u,
					Val = num2
				});
			}
			else
			{
				playerAttr.Val = num2;
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		catch
		{
		}
	}

	private static void EnsurePuzzleMap(PlayerInstance player)
	{
		if (player.GetAttr(101u, 3062u) == 0)
		{
			uint attr = player.GetAttr(101u, 3063u);
			uint attr2 = player.GetAttr(101u, 3168u);
			if (attr != 0 || attr2 != 0)
			{
				player.SetAttr(101u, 3062u, 1u);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
	}

	private static void SeedClips(PlayerInstance player)
	{
		foreach (ClipRow item in LoadClips())
		{
			if (item.ClipId <= 0 || item.DefaultNum <= 0)
			{
				continue;
			}
			uint sid = (uint)(3030 + item.ClipId);
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
			if (playerAttr == null || playerAttr.Val == 0)
			{
				uint val = (uint)Math.Min(item.DefaultNum, 32767);
				if (playerAttr == null)
				{
					player.Data.Attrs.Add(new PlayerAttr
					{
						Gid = 101u,
						Sid = sid,
						Val = val
					});
				}
				else
				{
					playerAttr.Val = val;
				}
			}
		}
	}

	public static List<MapRow> LoadMaps()
	{
		if (_maps != null)
		{
			return _maps;
		}
		_maps = new List<MapRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "puzzle";
			buffer[4] = "puzzle_map.json";
			string path = Path.Combine(buffer);
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						_maps.Add(new MapRow
						{
							MapId = HouseJson.NumField(jsonObject, "MapId"),
							Weight = Math.Max(1, HouseJson.NumField(jsonObject, "Weight")),
							CompleteReward = ReadRewards(jsonObject["CompleteReward"]),
							PercentReward = ReadRewards(jsonObject["PercentReward"])
						});
					}
				}
			}
		}
		catch
		{
		}
		return _maps;
	}

	public static List<ClipRow> LoadClips()
	{
		if (_clips != null)
		{
			return _clips;
		}
		_clips = new List<ClipRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "puzzle";
			buffer[4] = "clips.json";
			string path = Path.Combine(buffer);
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						_clips.Add(new ClipRow
						{
							ClipId = HouseJson.NumField(obj, "ClipId"),
							DefaultNum = HouseJson.NumField(obj, "DefaultNum")
						});
					}
				}
			}
		}
		catch
		{
		}
		return _clips;
	}

	public static void AddVoucher(PlayerInstance player, NtfSyncPlayer sync, uint count)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == 27);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 1u,
				Sid = 27u,
				Val = count
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val += count;
		}
		sync.Custom[player.ToPackedAttrKey(1u, 27u)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(1u, 27u)] = playerAttr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	private static List<int[]> ReadRewards(JsonNode? node)
	{
		List<int[]> list = new List<int[]>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonArray { Count: >=5 } jsonArray2)
			{
				int[] array = new int[5];
				for (int i = 0; i < 5; i++)
				{
					array[i] = ((jsonArray2[i] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value)) ? value : 0);
				}
				if (array[0] > 0)
				{
					list.Add(array);
				}
			}
		}
		return list;
	}
}
