using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Command;

public class PlayerCommandSender(PlayerInstance player) : ICommandSender
{
	public PlayerInstance Player = player;

	public async ValueTask SendMsg(string msg)
	{
		ChatMsg data = new ChatMsg
		{
			Type = ChatType.Friend,
			Sender = (uint)ConfigManager.Config.ServerOption.ServerProfile.Uid,
			Recver = (uint)Player.Uid,
			Text = ChatMessageHelper.NormalizeForClient(msg),
			Profile = Player.ToServerFriendProto(),
			TimeStamp = ChatMessageHelper.BuildClientTimestamp()
		};
		await Player.SendPacket(1042, data);
	}

	public int GetSender()
	{
		return Player.Uid;
	}
}
