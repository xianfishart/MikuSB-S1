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
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public static class HouseDropService
{
	private sealed class PosRow
	{
		public int PosId { get; set; }

		public int FloorId { get; set; }
	}

	private sealed class ClipRow
	{
		public int ClipId { get; set; }

		public int Weight { get; set; }
	}

	private const uint Gid = 101u;

	private const uint HouseInfoStart = 3000u;

	private const uint DropFirst = 1u;

	private const uint DropCount = 15u;

	private const uint HasSpawnDropFirstSid = 3029u;

	private const uint ClipCountStart = 3030u;

	private const uint RefreshMarkerGid = 999u;

	private const uint RefreshMarkerSid = 2u;

	private static List<PosRow>? _positions;

	private static List<ClipRow>? _clips;

	public static void Ensure(PlayerInstance player)
	{
		try
		{
			DateTime now = DateTime.Now;
			string s = ((now.Hour < 12) ? "00" : "12");
			uint num = (uint)(int.Parse(now.ToString("yyyyMMdd")) * 100) + uint.Parse(s);
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 999 && x.Sid == 2);
			if (playerAttr != null && playerAttr.Val == num)
			{
				return;
			}
			List<PosRow> list = LoadPositions();
			if (list.Count != 0)
			{
				for (uint num2 = 1u; num2 < 16; num2++)
				{
					SetAttr(player, 3000 + num2, 0u);
				}
				int num3 = (int)Math.Min(15L, list.Count);
				for (int num4 = 0; num4 < num3; num4++)
				{
					SetAttr(player, (uint)(3001 + num4), (uint)list[num4].PosId);
				}
				SetAttr(player, 3029u, 1u);
				if (playerAttr == null)
				{
					player.Data.Attrs.Add(new PlayerAttr
					{
						Gid = 999u,
						Sid = 2u,
						Val = num
					});
				}
				else
				{
					playerAttr.Val = num;
				}
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
	}

	public static async Task<(bool Ok, int PosId, int ClipId)> Collect(Connection connection, int posId, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return (Ok: false, PosId: 0, ClipId: 0);
		}
		uint slot = 0u;
		for (uint i = 0u; i < 15; i++)
		{
			uint sid = 3001 + i;
			if (HouseAttr.Read(player, sid) == (uint)posId)
			{
				slot = sid;
				break;
			}
		}
		if (slot == 0)
		{
			return (Ok: false, PosId: 0, ClipId: 0);
		}
		int clipId = PickClip(DateTime.Now.Ticks);
		await HouseAttr.SetAsync(connection, slot, 0u, sync);
		uint clipSid = (uint)(3030 + clipId);
		uint current = HouseAttr.Read(player, clipSid);
		uint dropCount = current & 0x7FFF;
		uint friendCount = current >> 15;
		uint next = (friendCount << 15) | ((dropCount + 1) & 0x7FFF);
		await HouseAttr.SetAsync(connection, clipSid, next, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		TaskEventService.OnClipCollected(player);
		return (Ok: true, PosId: posId, ClipId: clipId);
	}

	private static int PickClip(long seed)
	{
		List<ClipRow> list = (from x in LoadClips()
			where x.Weight > 0
			select x).ToList();
		if (list.Count == 0)
		{
			return 1;
		}
		int num = list.Sum((ClipRow x) => Math.Max(1, x.Weight));
		int num2 = (int)(Math.Abs(seed) % num);
		foreach (ClipRow item in list)
		{
			num2 -= Math.Max(1, item.Weight);
			if (num2 < 0)
			{
				return item.ClipId;
			}
		}
		return list[0].ClipId;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (playerAttr == null)
		{
			player.Data.Attrs.Add(new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = value
			});
		}
		else
		{
			playerAttr.Val = value;
		}
	}

	private static List<PosRow> LoadPositions()
	{
		if (_positions != null)
		{
			return _positions;
		}
		_positions = new List<PosRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "puzzle";
			buffer[4] = "drop_points.json";
			string path = Path.Combine(buffer);
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "PosId");
						if (num > 0)
						{
							_positions.Add(new PosRow
							{
								PosId = num,
								FloorId = HouseJson.NumField(obj, "FloorId")
							});
						}
					}
				}
			}
		}
		catch
		{
		}
		return _positions;
	}

	private static List<ClipRow> LoadClips()
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
						int num = HouseJson.NumField(obj, "ClipId");
						if (num > 0)
						{
							_clips.Add(new ClipRow
							{
								ClipId = num,
								Weight = HouseJson.NumField(obj, "DropWeight")
							});
						}
					}
				}
			}
		}
		catch
		{
		}
		return _clips;
	}
}
