using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace S1Admin;

/// <summary>
/// 第 418 节（2026-11-18）：切期后「当期期号」属性不会自己更新。
///
/// 这些属性只在 HandlerReqLogin 里播种一次，切期接口（GM 网页 replay / `/dlc N`）
/// 只改 Gid15/Sid1 和各类时间窗推送，不会重跑 DormActSeed / SeasonBossActSeed。
/// 于是存档里留着上一期的玩法期号：
///   第 30 期 → 第 31 期实测 18/1 仍是 6（30 期的 Teamboss），
///   而 31 期应是 7；客户端 SeasonBoss.GetCurActID()=6，
///   act 6 的赛季行是 2025-07 的老窗口，IsTermOpen()=false，
///   版本活动界面点「噬神斗场」就弹「暂未开放」。
///
/// 这里把登录时那套播种原样搬出来，切期后对在线玩家重跑一遍，
/// 并把这些属性用 NtfSyncPlayer(1004) 立刻推给客户端（不必重登）。
/// </summary>
public static class DlcEraSeeds
{
	private static readonly Logger Logger = new Logger("EraSeeds");

	/// <summary>播种会写的属性：18/1、18/2 噬神斗场，72/1 心意寄语，12/0 捞金派对，164/1 爆爆菜园。</summary>
	private static readonly uint[][] Keys = new uint[5][]
	{
		new uint[2] { 18u, 1u },
		new uint[2] { 18u, 2u },
		new uint[2] { 72u, 1u },
		new uint[2] { 12u, 0u },
		new uint[2] { 164u, 1u }
	};

	/// <summary>与 HandlerReqLogin 完全相同的四个播种，顺序也保持一致。</summary>
	public static void Apply(PlayerInstance player)
	{
		try
		{
			DormActSeed.Apply(player);
		}
		catch (Exception ex)
		{
			Logger.Warn("DormActSeed failed: " + ex.Message);
		}
		try
		{
			SeasonBossActSeed.Apply(player);
		}
		catch (Exception ex2)
		{
			Logger.Warn("SeasonBossActSeed failed: " + ex2.Message);
		}
		try
		{
			ChessActivityService.Seed(player, null);
		}
		catch (Exception ex3)
		{
			Logger.Warn("Chess seed failed: " + ex3.Message);
		}
		try
		{
			FarmWarActivityService.Seed(player, null);
		}
		catch (Exception ex4)
		{
			Logger.Warn("FarmWar seed failed: " + ex4.Message);
		}
	}

	public static async Task Push(Connection connection, string reason)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		Apply(player);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<string> list = new List<string>();
		foreach (uint[] key in Keys)
		{
			PlayerAttr attr = player.Attributes.Get(key[0], key[1]);
			if (attr == null)
			{
				continue;
			}
			sync.Custom[player.ToPackedAttrKey(key[0], key[1])] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(key[0], key[1])] = attr.Val;
			list.Add($"{key[0]}/{key[1]}={attr.Val}");
		}
		await connection.SendPacket(1004, sync, 0);
		Logger.Info($"{reason}: 重播种期号属性 uid={player.Uid} {string.Join(" ", list)}");
	}

	public static void PushAllOnline(string reason)
	{
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			if (!(item.Value is Connection conn) || conn.Player == null)
			{
				continue;
			}
			Task.Run(async delegate
			{
				try
				{
					await Push(conn, reason);
				}
				catch (Exception ex)
				{
					Logger.Warn($"{reason}: 重播种失败 uid={conn.Player?.Uid} {ex.Message}");
				}
			});
		}
	}
}
