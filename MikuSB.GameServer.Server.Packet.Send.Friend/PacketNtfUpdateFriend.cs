using System;
using MikuSB.GameServer.Game.Friend;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Send.Friend;

public class PacketNtfUpdateFriend : BasePacket
{
	public PacketNtfUpdateFriend(PlayerInstance player)
		: base(1016)
	{
		PlayerProfileArray playerProfileArray = new PlayerProfileArray();
		if (ConfigManager.Config.ServerOption.ShowServerProfileAsFriend)
		{
			playerProfileArray.List.Add(player.ToServerFriendProto());
		}
		// 2026-11-11：好友列表改为从 FakeFriendTable 派生，加人只改那张表。
		foreach (FakeFriend f in FakeFriendTable.All)
		{
			playerProfileArray.List.Add(BuildFakeFriend(f));
		}
		SetData(playerProfileArray);
	}

	private static PlayerProfile BuildFakeFriend(FakeFriend f)
	{
		PlayerProfile playerProfile = new PlayerProfile
		{
			Pid = f.Pid,
			Account = f.Name,
			Name = f.Name,
			Sign = f.Sign,
			Sex = (f.Female ? Sex.Female : Sex.Male),
			Level = f.Level,
			FriendCount = 0u,
			LogoutTime = 0u
		};
		for (int i = 1; i < 5; i++)
		{
			playerProfile.ShowItems.Add(new Item());
		}
		if (f.FaceTemplateId != FakeFriendTable.FaceNone)
		{
			playerProfile.ShowItems.Add(new Item
			{
				Id = f.Pid,
				Template = f.FaceTemplateId,
				Count = 1u,
				Flag = 0u
			});
		}
		else
		{
			playerProfile.ShowItems.Add(new Item());
		}
		playerProfile.ShowItems.Add(new Item());
		// 生日走 ShowAttrs：排布与玩家自己的 BuildShowAttrs 完全一致，
		// 下标 21 = 月、22 = 日、23 = 是否对他人展示、24 固定 1。
		uint[] showAttrs = new uint[25];
		showAttrs[21] = (uint)Math.Max(0, f.BirthMonth);
		showAttrs[22] = (uint)Math.Max(0, f.BirthDay);
		showAttrs[23] = (f.ShowBirthday ? 1u : 0u);
		showAttrs[24] = 1u;
		playerProfile.ShowAttrs.AddRange(showAttrs);
		return playerProfile;
	}
}
