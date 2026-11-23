using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_RecordProgres")]
public class ClimbTowerLogic_RecordProgres : ICallGSHandler
{
	private const uint TowerGroupId = 3u;

	private const uint BasicProgressSid = 2u;

	private const uint AdvancedProgressSid = 3u;

	private const uint LevelStateSidBase = 10000u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ClimbTowerRecordProgressParam req = JsonSerializer.Deserialize<ClimbTowerRecordProgressParam>(param);
		if (req == null || req.LevelId == 0 || req.Area <= 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_RecordProgres", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		ClimbTowerTimeExcel cycle = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
		if (cycle == null)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_RecordProgres", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		int towerType = ResolveTowerType(cycle, (uint)req.LevelId);
		if (towerType == 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_RecordProgres", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr levelState = GetOrCreateAttr(sid: (uint)(10000 + req.LevelId), data: player.Data, gid: 3u);
		levelState.Val = MergeAreaStars(levelState.Val, req.Area, req.StarMask);
		SyncAttr(sync, player, levelState);
		uint progressSid = ((towerType == 1) ? 2u : 3u);
		PlayerAttr progressAttr = GetOrCreateAttr(player.Data, 3u, progressSid);
		progressAttr.Val = ((req.Area < 3) ? PackProgress((uint)req.LevelId, (uint)(req.Area + 1)) : 0u);
		SyncAttr(sync, player, progressAttr);
		if (req.RoleHP.Count > 0 || req.TeamEnergy.HasValue)
		{
			SaveRoleState(player, sync, towerType, req.RoleHP, req.TeamEnergy.GetValueOrDefault());
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "ClimbTowerLogic_RecordProgres", "{}", sync);
	}

	private static void SaveRoleState(PlayerInstance player, NtfSyncPlayer sync, int towerType, List<List<int>> roleHp, int teamEnergy)
	{
		uint num = ((towerType != 2) ? 1u : 4u);
		for (uint num2 = num; num2 < num + 3; num2++)
		{
			PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 3u, num2 * 10);
			PlayerAttr orCreateAttr2 = GetOrCreateAttr(player.Data, 3u, num2 * 10 + 1);
			orCreateAttr.Val = 0u;
			orCreateAttr2.Val = 0u;
			SyncAttr(sync, player, orCreateAttr);
			SyncAttr(sync, player, orCreateAttr2);
		}
		for (int i = 0; i < Math.Min(roleHp.Count, 3); i++)
		{
			List<int> list = roleHp[i];
			if (list != null && list.Count >= 2)
			{
				uint num3 = num + (uint)i;
				PlayerAttr orCreateAttr3 = GetOrCreateAttr(player.Data, 3u, num3 * 10);
				PlayerAttr orCreateAttr4 = GetOrCreateAttr(player.Data, 3u, num3 * 10 + 1);
				orCreateAttr3.Val = (uint)Math.Max(0, list[0]);
				orCreateAttr4.Val = (uint)Math.Max(0, list[1]);
				SyncAttr(sync, player, orCreateAttr3);
				SyncAttr(sync, player, orCreateAttr4);
			}
		}
		PlayerAttr orCreateAttr5 = GetOrCreateAttr(player.Data, 3u, num * 10 + 2);
		orCreateAttr5.Val = (uint)Math.Max(0, teamEnergy);
		SyncAttr(sync, player, orCreateAttr5);
	}

	private static uint MergeAreaStars(uint currentValue, int area, int starMask)
	{
		int num = Math.Clamp(area, 1, 3) - 1;
		uint num2 = currentValue;
		for (int i = 0; i < 3; i++)
		{
			if (((starMask >> i) & 1) != 0)
			{
				int num3 = num * 3 + i;
				num2 |= (uint)(1 << num3);
			}
		}
		return num2;
	}

	private static uint PackProgress(uint levelId, uint area)
	{
		return (area << 24) | (levelId & 0xFFFFFF);
	}

	private static int ResolveTowerType(ClimbTowerTimeExcel cycle, uint levelId)
	{
		if (ContainsLevel(cycle.GetLevelGroups(1), levelId))
		{
			return 1;
		}
		if (ContainsLevel(cycle.GetLevelGroups(2), levelId))
		{
			return 2;
		}
		return 0;
	}

	private static bool ContainsLevel(IEnumerable<IReadOnlyList<uint>> groups, uint levelId)
	{
		return groups.Any((IReadOnlyList<uint> group) => group.Any((uint id) => id == levelId));
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

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
