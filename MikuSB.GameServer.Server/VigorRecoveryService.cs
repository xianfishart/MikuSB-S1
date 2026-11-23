using System;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server;

public static class VigorRecoveryService
{
	private static bool _started;

	private static readonly object Gate = new object();

	private static readonly Logger Log = new Logger("VigorRecoveryService");

	public static void Start()
	{
		lock (Gate)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		Task.Run(async () =>
		{
			_ = 1;
			while (true)
			{
				try
				{
					await Task.Delay(60000);
					Connection[] array;
					lock (SocketListener.Connections)
					{
						array = SocketListener.Connections.Values.OfType<Connection>().ToArray();
					}
					Connection[] array2 = array;
					Connection[] array3 = array2;
					foreach (Connection connection in array3)
					{
						if (connection?.Player != null && connection.State == SessionStateEnum.ACTIVE && VigorRecovery.Apply(connection.Player))
						{
							DatabaseHelper.SaveDatabaseType(connection.Player.Data);
							NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
							ntfSyncPlayer.Core[0u] = connection.Player.Data.Level;
							ntfSyncPlayer.Core[1u] = (uint)Math.Max(0, connection.Player.Data.Exp);
							ntfSyncPlayer.Core[2u] = connection.Player.Data.Vigor;
							ntfSyncPlayer.Core[4u] = connection.Player.Attributes.GetValue(9900u, 1u);
							await connection.SendPacket(1004, ntfSyncPlayer, 0);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn("recovery tick failed: " + ex.Message);
				}
			}
		});
		Log.Info("started: vigor recovers 1 point per " + 300 + "s, cap from levels.json");
	}
}
