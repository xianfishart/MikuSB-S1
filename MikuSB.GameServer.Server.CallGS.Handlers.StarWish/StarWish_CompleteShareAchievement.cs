using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_CompleteShareAchievement")]
public class StarWish_CompleteShareAchievement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			StarWishAttr.Set(player, 7u, 1u, sync);
			// 第 438 节：重复任务「心愿传递 = 分享一次星祈心愿界面」（activity_quest 23001，Type 387）。
			MikuSB.GameServer.Game.Activity.ActivityQuestService.Complete(player, 387, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			// 第 425 节：客户端 s2c.tbCmds 里没有 StarWish_CompleteShareAchievement
			// （548 个已注册命令里查过），回同名包只会让 S2C.lua:32 抛
			// "No command named: StarWish_CompleteShareAchievement"。
			// 数据已经写进玩家存档，成就界面下次打开会从数据里读，这里只补一条 1004 同步。
			if (sync != null)
			{
				await connection.SendPacket(1004, sync, 0);
			}
		}
	}
}
