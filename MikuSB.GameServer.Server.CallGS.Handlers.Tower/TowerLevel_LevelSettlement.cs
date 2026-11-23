using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerLevel_LevelSettlement")]
public class TowerLevel_LevelSettlement : ICallGSHandler
{
	private static readonly Logger Logger = new Logger("Tower");

	private const uint TowerGroupId = 3u;

	private const uint LaunchPassGroupId = 22u;

	private const uint BasicProgressSid = 2u;

	private const uint AdvancedProgressSid = 3u;

	private const uint LevelStateSidBase = 10000u;

	private const int FinalArea = 3;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonNode, NtfSyncPlayer) tuple = HandleSettlement(connection.Player, JsonNode.Parse(param));
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "TowerLevel_LevelSettlement", arg: response.ToJsonString());
	}

	public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
	{
		TowerLevelSettlementParam towerLevelSettlementParam = ((tbParam != null) ? tbParam.Deserialize<TowerLevelSettlementParam>() : null);
		if (towerLevelSettlementParam == null || towerLevelSettlementParam.TowerId == 0 || towerLevelSettlementParam.LevelId == 0)
		{
			Logger.Error("Invalid tower settlement payload: " + (tbParam?.ToJsonString() ?? "null"));
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		ClimbTowerTimeExcel climbTowerTimeExcel = ResolveCurrentCycle(GameData.ClimbTowerTimeData.Values, DateTime.Now);
		if (climbTowerTimeExcel == null)
		{
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		int num = ResolveTowerType(climbTowerTimeExcel, (uint)towerLevelSettlementParam.TowerId);
		if (num == 0)
		{
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		uint num2 = (uint)(10000 + towerLevelSettlementParam.TowerId);
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 3u, num2);
		orCreateAttr.Val = MergeAreaStars(orCreateAttr.Val, 3, towerLevelSettlementParam.StarMask);
		SyncAttr(ntfSyncPlayer, player, orCreateAttr);
		uint num3 = ((num == 1) ? 2u : 3u);
		PlayerAttr orCreateAttr2 = GetOrCreateAttr(player.Data, 3u, num3);
		orCreateAttr2.Val = 0u;
		SyncAttr(ntfSyncPlayer, player, orCreateAttr2);
		PlayerAttr orCreateAttr3 = GetOrCreateAttr(player.Data, 22u, (uint)towerLevelSettlementParam.LevelId);
		orCreateAttr3.Val = Math.Max(1u, orCreateAttr3.Val + 1);
		SyncAttr(ntfSyncPlayer, player, orCreateAttr3);
		Logger.Info($"Tower settlement saved. uid={player.Uid} towerId={towerLevelSettlementParam.TowerId} levelId={towerLevelSettlementParam.LevelId} starMask={towerLevelSettlementParam.StarMask} towerStateSid={num2} towerStateVal={orCreateAttr.Val} progressSid={num3} passVal={orCreateAttr3.Val}");
		DatabaseHelper.SaveDatabaseType(player.Data);
		return (Response: new JsonObject(), Sync: ntfSyncPlayer);
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
