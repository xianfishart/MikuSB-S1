using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MikuSB.Enums.Packet;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server;

public class Connection : SocketConnection
{
	private static readonly Logger Logger = new Logger("GameServer");

	private static readonly HashSet<string> DummyPacketNames = new HashSet<string> { "ReqOnlineRoomReconnect", "ReqOnlineRoom", "ReqOnlineRoomStart", "ReqOnlineRoomExit", "ReqOnlineRoomInvite", "ReqOnlineRoomAccept", "ReqOnlineRoomUpdate", "ReqOnlineRoomChatAccept", "ReqOnlineRoomUpdateMap" };

	public PlayerInstance? Player { get; set; }

	public Connection(Socket socket, IPEndPoint remote)
		: base(socket, remote)
	{
	}

	public override async void Start()
	{
		Logger.Info($"New connection from {RemoteEndPoint}.");
		State = SessionStateEnum.WAITING_FOR_TOKEN;
		await ReceiveLoop();
	}

	public override void Stop(bool isServerStop = false)
	{
		Player?.OnLogoutAsync();
		SocketListener.UnregisterConnection(this);
		base.Stop(isServerStop);
	}

	public static int GetInt32(byte[] buf, int index)
	{
		int network = BitConverter.ToInt32(buf, index);
		return IPAddress.NetworkToHostOrder(network);
	}

	protected async Task ReceiveLoop()
	{
		try
		{
			NetworkStream stream = new NetworkStream(Socket, ownsSocket: false);
			while (SocketConnected())
			{
				BasePacket decodedPacket = await new PacketCodec().ReadPacketAsync(stream, CancelToken.Token);
				if (decodedPacket == null)
				{
					Logger.Info("Client disconnected");
					break;
				}
				switch (decodedPacket.Framing)
				{
				case PacketFraming.FourByteLittleEndianLength:
				case PacketFraming.TwoByteBigEndianLength:
					Framing = decodedPacket.Framing;
					LogPacket("Recv", decodedPacket.CmdId, decodedPacket.Body.ToArray(), Framing);
					await HandlePacket(decodedPacket.CmdId, decodedPacket.Body.ToArray());
					break;
				case PacketFraming.Control:
					Logger.Info("Control packet received");
					break;
				case PacketFraming.Unknown:
					Logger.Warn("Unknown packet format received");
					break;
				}
			}
		}
		catch (OperationCanceledException)
		{
			Logger.Info("ReceiveLoop cancelled");
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			Logger.Info($"ReceiveLoop error: {ex3}");
		}
		finally
		{
			Socket.Close();
		}
		Stop();
	}

	private async Task HandlePacket(ushort opcode, byte[] payload)
	{
		string packetName = SocketConnection.LogMap.GetValueOrDefault(opcode);
		if (DummyPacketNames.Contains(packetName))
		{
			await SendDummy(packetName);
			Logger.Info("[Dummy] Send Dummy " + packetName);
			return;
		}
		Handler handler = HandlerManager.GetHandler(opcode);
		if (handler != null)
		{
			_ = State;
			try
			{
				await handler.OnHandle(this, payload, (ushort)DownStreamSeqNo);
				return;
			}
			catch (Exception ex)
			{
				Exception e = ex;
				Logger.Error(e.Message, e);
				return;
			}
		}
		if (ConfigManager.Config.ServerOption.EnableDebug && ConfigManager.Config.ServerOption.DebugNoHandlerPacket && !SocketConnection.IgnoreLog.Contains(opcode))
		{
			Logger.Error($"No handler found for {packetName}({opcode})");
		}
	}

	private async Task SendDummy(string packetName)
	{
		string respName = packetName.Replace("Req", "Rsp");
		if (!(respName == packetName))
		{
			int respOpcode = SocketConnection.LogMap.FirstOrDefault((KeyValuePair<int, string> x) => x.Value == respName).Key;
			await SendPacket(respOpcode);
		}
	}
}
