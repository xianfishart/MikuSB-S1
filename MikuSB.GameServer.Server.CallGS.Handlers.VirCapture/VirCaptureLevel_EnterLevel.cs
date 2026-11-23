using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureLevel_EnterLevel")]
public class VirCaptureLevel_EnterLevel : ICallGSHandler
{
	private const uint GroupId = 128u;

	private const uint MapDataStart = 10000u;

	private const uint MaxMapCount = 3u;

	private const uint MaxMapDataLen = 3000u;

	private const uint OffMapId = 1u;

	private const uint OffDayNight = 7u;

	private const uint OffMapLevel = 8u;

	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureEnterLevelParam req = JsonSerializer.Deserialize<VirCaptureEnterLevelParam>(param);
		if (req == null || req.LevelId == 0 || req.TeamId <= 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		DateTime now = DateTime.Now;
		VirCaptureTimeExcel act = ResolveCurrent(GameData.VirCaptureTimeData.Values, now);
		if (act == null || !act.CaptureRegionId.Contains((uint)req.LevelId))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_EnterLevel", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		if (!GameData.VirCaptureCaptureRegionData.TryGetValue((uint)req.LevelId, out VirCaptureCaptureRegionExcel region))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		DateTime? regionStart = ParseConfigTime(region.StartTime);
		DateTime? regionEnd = ParseConfigTime(region.EndTime);
		if (!regionStart.HasValue || !regionEnd.HasValue || now < regionStart.Value || now >= regionEnd.Value)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureLevel_EnterLevel", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		EnsureMapState(player, (uint)req.LevelId, sync);
		string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
		await CallGSRouter.SendScript(connection, "VirCaptureLevel_EnterLevel", rsp, sync);
	}

	private static void EnsureMapState(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		uint num = FindOrAllocateMapSlot(player, levelId);
		if (num != 0)
		{
			EnsureMapAttr(player, num + 1, levelId, sync);
			EnsureMapAttr(player, num + 7, 1u, sync);
			EnsureMapAttr(player, num + 8, 1u, sync);
		}
	}

	private static uint FindOrAllocateMapSlot(PlayerInstance player, uint levelId)
	{
		uint? num = null;
		for (uint num2 = 0u; num2 < 3; num2++)
		{
			uint slotStart = 10000 + num2 * 3000;
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == slotStart + 1);
			if (playerAttr != null && playerAttr.Val == levelId)
			{
				return slotStart;
			}
			if (!num.HasValue && (playerAttr == null || playerAttr.Val == 0))
			{
				num = slotStart;
			}
		}
		return num.GetValueOrDefault();
	}

	private static void EnsureMapAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 128u,
				Sid = sid,
				Val = minValue
			};
			player.Data.Attrs.Add(playerAttr);
			SyncAttr(player, sync, sid, minValue);
		}
		else if (playerAttr.Val < minValue)
		{
			playerAttr.Val = minValue;
			SyncAttr(player, sync, sid, playerAttr.Val);
		}
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		sync.Custom[player.ToPackedAttrKey(128u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(128u, sid)] = value;
	}

	private static VirCaptureTimeExcel? ResolveCurrent(IEnumerable<VirCaptureTimeExcel> configs, DateTime now)
	{
		var source = (from x in configs
			select new
			{
				Config = x,
				Start = ParseConfigTime(x.StartTime),
				End = ParseConfigTime(x.EndTime)
			} into x
			where x.Start.HasValue && x.End.HasValue
			orderby x.Start
			select x).ToList();
		var anon = source.FirstOrDefault(x =>
		{
			int result;
			if (x.Start <= now)
			{
				DateTime value = now;
				DateTime? end = x.End;
				result = ((value < end) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		});
		if (anon != null)
		{
			return anon.Config;
		}
		var anon2 = source.LastOrDefault(x => x.Start <= now);
		if (anon2 != null && anon2.End > anon2.Start)
		{
			return anon2.Config;
		}
		return null;
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}
}
