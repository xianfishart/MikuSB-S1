using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Friend;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.GameServer.Server.Packet.Send.Friend;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Friend;

/// <summary>
/// 领取好友赠送的体力（ReqRecvFriendvigor = 37）。
/// 2026-11-11 新增：先只回空包让界面收尾，但体力数值不动；
/// 本次接到真实体力上：每个自然日首次领取给玩家 +20 体力（PortVigor.Add，
/// 即 Core[2]），并落库；同日重复领取只回包不再加。
/// 回包 RspRecvFriendvigor = 38 携带 FriendVigorList（已领取状态），
/// 之后再用 NtfCallScript(1010) 的 ExtraSync 把新体力刷到客户端。
/// </summary>
[Opcode(37)]
public class HandlerReqRecvFriendVigor : Handler
{
	private static readonly Logger Log = new Logger("ReqRecvFriendVigor");

	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int requested = 0;
		try
		{
			if (data != null && data.Length > 0)
			{
				FriendVigorList req = FriendVigorList.Parser.ParseFrom(data);
				requested = req.List.Count;
				Log.Debug($"ReqRecvFriendVigor count={requested} first={(requested > 0 ? req.List[0].Pid : 0uL)}");
			}
			else
			{
				Log.Debug("ReqRecvFriendVigor (empty body)");
			}
		}
		catch (Exception ex)
		{
			Log.Debug("ReqRecvFriendVigor parse fail: " + ex.Message);
		}

		bool granted = FriendVigorState.TryClaim(player, out NtfSyncPlayer? sync);
		if (granted && player != null)
		{
			Log.Info($"ReqRecvFriendVigor uid={player.Uid} claimed {FriendVigorState.VigorPerClaim} vigor, now {player.Data.Vigor} (requested={requested}).");
		}
		else if (player != null)
		{
			Log.Info($"ReqRecvFriendVigor uid={player.Uid} already claimed today (requested={requested}).");
		}

		FriendVigorList rsp = new FriendVigorList();
		foreach (FakeFriend f in FakeFriendTable.All)
		{
			if (!f.HasVigor)
			{
				continue;
			}
			rsp.List.Add(new FriendVigor
			{
				Pid = f.Pid,
				HaveVigor = true,
				VigorGot = true,
				ReturnVigor = false
			});
		}
		await connection.SendPacket(38, rsp, seqNo);
		if (granted && player != null && sync != null)
		{
			await player.SendPacket(new PacketNtfFriendVigor(player));
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
	}
}
