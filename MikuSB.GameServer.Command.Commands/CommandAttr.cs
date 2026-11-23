using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 直接读写玩家属性（第 441 节加的调试命令）。
///
/// 排查「活动数据没落库」这类问题时，经常需要把某个 gid/sid 摆成某个值再看客户端表现，
/// 之前只能停服改 sqlite。用法：
///   /attr 179:21            读一个
///   /attr 179:21=1          写一个
///   /attr 179:20-30         列出一段（含 0 值）
///   /attr dump 179          列出整组非 0 值
/// 末尾可加 @uid 指定在线玩家。
/// </summary>
[CommandInfo("attr", "Read/write player attributes (gid:sid[=val], gid:sidA-sidB, dump gid).", "Usage: /attr 179:21  |  /attr 179:21=1  |  /attr 179:20-30  |  /attr dump 179", new string[]
{
	"attr"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandAttr : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		string raw = ((arg.Args.Count > 0) ? arg.Args[0] : string.Empty).Trim();
		string raw2 = ((arg.Args.Count > 1) ? arg.Args[1] : string.Empty).Trim();
		if (raw.Length == 0)
		{
			await arg.SendMsg("Usage: /attr 179:21  |  /attr 179:21=1  |  /attr 179:20-30  |  /attr dump 179");
			return;
		}
		if (string.Equals(raw, "dump", StringComparison.OrdinalIgnoreCase))
		{
			uint gid;
			if (!uint.TryParse(raw2, out gid))
			{
				await arg.SendMsg("Usage: /attr dump <gid>");
				return;
			}
			var rows = player.Data.Attrs.Where((PlayerAttr x) => x.Gid == gid).OrderBy((PlayerAttr x) => x.Sid).Select((PlayerAttr x) => $"{x.Sid}={x.Val}");
			string text = string.Join(", ", rows);
			await arg.SendMsg($"attr {gid}: {(text.Length == 0 ? "(empty)" : text)}");
			return;
		}
		int eq = raw.IndexOf('=');
		if (eq > 0)
		{
			uint gid2;
			uint sid2;
			uint val;
			if (!TryParsePair(raw.Substring(0, eq), out gid2, out sid2) || !uint.TryParse(raw.Substring(eq + 1), out val))
			{
				await arg.SendMsg("Bad format. Example: /attr 179:21=1");
				return;
			}
			PlayerAttr attr = player.Attributes.GetOrCreate(gid2, sid2);
			uint old = attr.Val;
			attr.Val = val;
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.Custom[player.ToPackedAttrKey(gid2, sid2)] = val;
			sync.Custom[player.ToShiftedAttrKey(gid2, sid2)] = val;
			await CallGSRouter.SendScript(arg.Target, "GM_AttrSync", "{}", sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await arg.SendMsg($"attr {gid2}/{sid2}: {old} -> {val}");
			return;
		}
		int dash = raw.IndexOf('-');
		if (dash > 0)
		{
			uint gid3;
			uint from;
			uint to;
			if (!TryParsePair(raw.Substring(0, dash), out gid3, out from) || !uint.TryParse(raw.Substring(dash + 1), out to) || to < from || to - from > 200)
			{
				await arg.SendMsg("Bad range. Example: /attr 179:20-30");
				return;
			}
			var sb = new System.Text.StringBuilder();
			for (uint sid3 = from; sid3 <= to; sid3++)
			{
				uint v = player.Attributes.GetValue(gid3, sid3);
				sb.Append(sid3).Append('=').Append(v).Append(' ');
			}
			await arg.SendMsg($"attr {raw}: {sb}");
			return;
		}
		uint gid4;
		uint sid4;
		if (!TryParsePair(raw, out gid4, out sid4))
		{
			await arg.SendMsg("Bad format. Example: /attr 179:21");
			return;
		}
		await arg.SendMsg($"attr {gid4}/{sid4} = {player.Attributes.GetValue(gid4, sid4)}");
	}

	private static bool TryParsePair(string text, out uint gid, out uint sid)
	{
		gid = 0u;
		sid = 0u;
		string[] parts = text.Split(':');
		return parts.Length == 2 && uint.TryParse(parts[0], out gid) && uint.TryParse(parts[1], out sid);
	}
}
