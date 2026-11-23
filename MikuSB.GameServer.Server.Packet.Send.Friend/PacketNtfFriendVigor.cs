using MikuSB.GameServer.Game.Friend;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Send.Friend;

/// <summary>
/// 好友体力状态推送（NtfFriendVigor = 1019）。
/// 2026-11-11 新增：客户端好友界面的「赠送 / 领取体力」依赖这份状态，
/// 原生服务端从未下发，导致界面进入等待态。这里按 FakeFriendTable 逐个补齐：
/// 可赠送、领取状态按当日是否已领决定、未回赠。领取记录见 FriendVigorState。
/// </summary>
public class PacketNtfFriendVigor : BasePacket
{
	public PacketNtfFriendVigor(PlayerInstance player)
		: base(1019)
	{
		bool vigorGot = FriendVigorState.HasClaimedToday(player);
		FriendVigorList friendVigorList = new FriendVigorList();
		foreach (FakeFriend f in FakeFriendTable.All)
		{
			if (!f.HasVigor)
			{
				continue;
			}
			friendVigorList.List.Add(new FriendVigor
			{
				Pid = f.Pid,
				HaveVigor = true,
				VigorGot = vigorGot,
				ReturnVigor = false
			});
		}
		SetData(friendVigorList);
	}
}
