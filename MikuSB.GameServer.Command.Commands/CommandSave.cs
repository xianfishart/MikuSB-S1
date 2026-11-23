using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 立即把在线玩家数据写进数据库（2026-11-14 新增，修复记录第 301 节附带）。
///
/// 起因：改代码要重启服务端，而 Stop-Process 是强杀，内存里还没落库的改动会丢
/// （例如 /money set 只改内存不存盘）。重启前先 /save 一下就不会丢。
///
/// 用法：/save            → 保存所有在线玩家
///       /save @uid       → 只保存指定玩家
/// </summary>
[CommandInfo("save", "Flush online player data to the database now.", "Usage: /save  (append @uid to save one player)", new string[]
{
	"flush"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandSave : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		List<PlayerInstance> targets;
		if (arg.Target is Connection one && one.Player != null)
		{
			targets = new List<PlayerInstance> { one.Player };
		}
		else
		{
			targets = (from c in SocketListener.Connections.Values.OfType<Connection>()
				where c.Player != null
				select c.Player).ToList();
		}
		if (targets.Count == 0)
		{
			await arg.SendMsg("save: 没有在线玩家。控制台执行时请加 @uid（例：/save @3）。");
			return;
		}
		foreach (PlayerInstance player in targets)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		await arg.SendMsg($"save: 已保存 {targets.Count} 名玩家（uid {string.Join(",", targets.Select((PlayerInstance p) => p.Uid))}）。");
	}
}
