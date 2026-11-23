using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BattlePass;

[CallGSApi("BattlePassLogic_ClientRefresh")]
public class BattlePassLogic_ClientRefresh : ICallGSHandler
{
	private const uint GroupId = 25u;

	private const uint CurIdSid = 1u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		BattlePassTimeExcel battlePass = ResolveCurrent(now: DateTime.Now, configs: GameData.BattlePassTimeData.Values);
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (battlePass == null)
			{
				SetAttr(player, 1u, 0u, sync);
				await CallGSRouter.SendScript(connection, "BattlePassLogic_ClientRefresh", "{}", sync);
				return;
			}
			SetAttr(player, 1u, battlePass.Id, sync);
			JsonObject response = new JsonObject
			{
				["nId"] = battlePass.Id,
				["nStartTime"] = ToUnixSeconds(ParseConfigTime(battlePass.StartTime)),
				["nEndTime"] = ToUnixSeconds(ParseConfigTime(battlePass.EndTime))
			};
			await CallGSRouter.SendScript(connection, "BattlePassLogic_ClientRefresh", response.ToJsonString(), sync);
		}
	}

	private static BattlePassTimeExcel? ResolveCurrent(IEnumerable<BattlePassTimeExcel> configs, DateTime now)
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
		return source.LastOrDefault(x => x.Start <= now && x.End > x.Start)?.Config;
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

	private static long ToUnixSeconds(DateTime? value)
	{
		return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, sid);
		if (orCreateAttr.Val != value)
		{
			orCreateAttr.Val = value;
			sync.Custom[player.ToPackedAttrKey(25u, sid)] = value;
			sync.Custom[player.ToShiftedAttrKey(25u, sid)] = value;
		}
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 25 && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 25u,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}
}
