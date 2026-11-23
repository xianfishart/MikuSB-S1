using System.Linq;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server;

public class Listener : SocketListener
{
	public static Connection? GetActiveConnection(int uid)
	{
		return SocketListener.Connections.Values.FirstOrDefault((SocketConnection c) =>
		{
			Connection obj = c as Connection;
			return obj != null && obj.Player?.Uid == uid && c.State == SessionStateEnum.ACTIVE;
		}) as Connection;
	}
}
