using System;
using System.IO;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Progress;
using MikuSB.GameServer.Game.Support;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.GameServer.Server.Packet.Send.Friend;
using MikuSB.GameServer.Server.Packet.Send.Login;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(3)]
public class HandlerReqReconnect : Handler
{
	private static readonly Logger Logger = new Logger("ReqReconnect");

	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		ReqReconnect req = ReqReconnect.Parser.ParseFrom(data);
		int uid = (int)req.Pid;
		AccountData account = AccountData.GetAccountByUid(uid) ?? AccountData.GetAccountByUid(1) ?? AccountData.GetAccountByComboToken("development");
		if (account == null)
		{
			Logger.Warn($"Rejected reconnect: pid={uid}");
			await connection.SendPacket(1009);
		}
		else if (ResourceManager.IsLoaded)
		{
			uid = account.Uid;
			PlayerGameData pd = DatabaseHelper.GetInstance<PlayerGameData>(uid);
			connection.Player = ((pd == null) ? new PlayerInstance(uid) : new PlayerInstance(pd));
			connection.DebugFile = Path.Combine(ConfigManager.Config.Path.LogPath, "Debug/", $"{uid}/", $"Debug-{DateTime.Now:yyyy-MM-dd HH-mm-ss}.log");
			await connection.Player.OnEnterGame();
			connection.Player.Connection = connection;
			connection.State = SessionStateEnum.ACTIVE;
			try
			{
				await GrantAllItems.GrantMissing(connection.Player, sendPackets: false);
			}
			catch (Exception value)
			{
				Logger.Error($"GrantAllItems failed: {value}");
			}
			SupportRikiService.Sync(connection.Player);
			DormDailyEvents.Ensure(connection.Player);
			DormPuzzleService.Ensure(connection.Player);
			SevenDayProgress.Ensure(connection.Player);
			TaskProgressService.Ensure(connection.Player);
			OnlineMissionAutoComplete.Ensure(connection.Player);
			ActivityQuestProgress.Ensure(connection.Player);
			// 第 450 节：绘梦沙盘「繁荣嘉奖 → 绘梦档案」的任务进度按已通关/星级补齐。
			MikuSB.GameServer.Server.CallGS.Handlers.Dlc.RmolTaskService.Ensure(connection.Player);
			await FirstRechargeService.Ensure(connection);
			await connection.SendPacket(new PacketRspReconnect(connection.Player, req.SessionId), 0);
			await connection.SendPacket(new PacketNtfCallScript(connection.Player), 0);
			await connection.Player.OnHeartBeat();
			await connection.SendPacket(new PacketNtfUpdateFriend(connection.Player), 0);
		}
	}
}
