using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("gmt", "Push a GM lua snippet to online clients (debug).", "Usage: /gmt <lua code>", new string[] { "lua2" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGmt : ICommands
{
	[CommandDefault]
	public async ValueTask Send(CommandArg arg)
	{
		string code = arg.RawArg?.Trim() ?? "";
		if (code.StartsWith("gmt ", StringComparison.OrdinalIgnoreCase))
		{
			code = code.Substring(4).Trim();
		}
		else if (code.Equals("gmt", StringComparison.OrdinalIgnoreCase))
		{
			code = "";
		}
		if (code.Length == 0)
		{
			await arg.SendMsg("用法: /gmt <lua 代码>");
			return;
		}
		string payload = new JsonObject { ["code"] = code }.ToJsonString();
		int count = 0;
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			SocketConnection value = item.Value;
			Connection conn = value as Connection;
			if (conn != null && conn.Player != null)
			{
				await CallGSRouter.SendScript(conn, "GM", payload);
				count++;
			}
		}
		await arg.SendMsg($"已下发到 {count} 个在线连接（{code.Length} 字符）");
	}
}
