using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_CheckCycleLevel")]
public class ClimbTowerLogic_CheckCycleLevel : ICallGSHandler
{
	private const uint TowerGroupId = 3u;

	private const uint TimeSubId = 1u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ClimbTowerTimeExcel current = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
		if (current == null)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_CheckCycleLevel", "{}");
			return;
		}
		uint currentTimeId = GetAttr(player.Data, 3u, 1u);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (currentTimeId != current.ID)
		{
			ResetTowerAttrs(player, sync);
			SetAttr(player.Data, 3u, 1u, current.ID, sync, player);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "ClimbTowerLogic_CheckCycleLevel", $"{{\"timeID\":{current.ID}}}", sync);
	}

	private static ClimbTowerTimeExcel? ResolveCurrentCycle(IEnumerable<ClimbTowerTimeExcel> configs, DateTime now)
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
		if (anon2 != null)
		{
			return anon2.Config;
		}
		return source.FirstOrDefault()?.Config;
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

	private static uint GetAttr(PlayerGameData data, uint gid, uint sid)
	{
		return data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid)?.Val ?? 0;
	}

	private static void ResetTowerAttrs(PlayerInstance player, NtfSyncPlayer sync)
	{
		List<PlayerAttr> list = player.Data.Attrs.Where((PlayerAttr x) => x.Gid == 3).ToList();
		foreach (PlayerAttr item in list)
		{
			sync.Custom[player.ToPackedAttrKey(item.Gid, item.Sid)] = 0u;
			sync.Custom[player.ToShiftedAttrKey(item.Gid, item.Sid)] = 0u;
		}
		player.Data.Attrs.RemoveAll((PlayerAttr x) => x.Gid == 3);
	}

	private static void SetAttr(PlayerGameData data, uint gid, uint sid, uint value, NtfSyncPlayer sync, PlayerInstance player)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid
			};
			data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = value;
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}
}
