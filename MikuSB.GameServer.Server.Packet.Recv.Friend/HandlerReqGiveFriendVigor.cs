using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Friend;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.Packet.Send.Friend;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Friend;

/// <summary>
/// 好友赠送体力（ReqGiveFriendvigor = 35）。
/// 2026-11-11 新增：原生服务端未实现该协议，客户端点「赠送 / 领取体力」后
/// 请求无人应答，界面一直停在加载态。这里补上回包（RspGiveFriendvigor = 36）。
/// 2026-11-11 二次修正：实测点「领取好友体力」时客户端发的是本协议（空 body），
/// 因此把当日首次领取的 +20 也接在这里，与 37 共用同一份每日标记，
/// 谁先到就算领过，避免重复加。
/// </summary>
[Opcode(35)]
public class HandlerReqGiveFriendVigor : Handler
{
	private static readonly Logger Log = new Logger("ReqGiveFriendVigor");

	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ulong target = 0uL;
		try
		{
			if (data != null && data.Length > 0)
			{
				FriendVigor req = FriendVigor.Parser.ParseFrom(data);
				target = req.Pid;
				Log.Debug("ReqGiveFriendVigor pid=" + target + " have=" + req.HaveVigor + " got=" + req.VigorGot + " back=" + req.ReturnVigor);
			}
			else
			{
				Log.Debug("ReqGiveFriendVigor (empty body)");
			}
		}
		catch (Exception ex)
		{
			Log.Debug("ReqGiveFriendVigor parse fail: " + ex.Message);
		}

		bool granted = FriendVigorState.TryClaim(player, out NtfSyncPlayer sync);
		if (granted && player != null)
		{
			Log.Info($"ReqGiveFriendVigor uid={player.Uid} claimed {FriendVigorState.VigorPerClaim} vigor, now {player.Data.Vigor} (pid={target}).");
		}
		else if (player != null)
		{
			Log.Info($"ReqGiveFriendVigor uid={player.Uid} already claimed today (pid={target}).");
		}

		await connection.SendPacket(36, seqNo);
		if (granted && player != null && sync != null)
		{
			// 刷新好友体力面板（已领取状态）+ 体力数字。
			await player.SendPacket(new PacketNtfFriendVigor(player));
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
	}
}
