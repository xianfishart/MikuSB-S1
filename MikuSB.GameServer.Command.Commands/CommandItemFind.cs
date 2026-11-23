using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 在所有 Excel 物品表里按关键字搜索（反射遍历，不依赖具体列名）。
/// 用法：/itemfind &lt;关键字&gt; [最大条数=20]
/// 输出每行的 G/D/P/L + 命中的字符串字段，用来定位「某道具叫什么、id 是多少」。
/// </summary>
[CommandInfo("itemfind", "Search all item excel tables by keyword.", "Usage: /itemfind <keyword> [max=20]", new string[] { "ifind" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandItemFind : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		string kw = arg.Args.Count > 0 ? arg.Args[0] : "";
		int max = (int)arg.GetInt(1);
		if (max <= 0)
		{
			max = 20;
		}
		if (string.IsNullOrWhiteSpace(kw))
		{
			await arg.SendMsg("Usage: /itemfind <keyword> [max=20]");
			return;
		}
		StringBuilder sb = new StringBuilder();
		int hits = 0;
		Type gdType = typeof(MikuSB.Data.GameData);
		PropertyInfo[] props = gdType.GetProperties(BindingFlags.Public | BindingFlags.Static);
		foreach (PropertyInfo prop in props)
		{
			object val;
			try
			{
				val = prop.GetValue(null);
			}
			catch
			{
				continue;
			}
			IEnumerable seq = val as IEnumerable;
			if (seq == null || val is string)
			{
				continue;
			}
			int scanned = 0;
			foreach (object row in seq)
			{
				if (row == null)
				{
					continue;
				}
				scanned++;
				string text;
				try
				{
					text = DescribeRow(row, kw);
				}
				catch
				{
					continue;
				}
				if (text == null)
				{
					continue;
				}
				sb.Append('[').Append(prop.Name).Append("] ").Append(text).Append('\n');
				hits++;
				if (hits >= max)
				{
					break;
				}
			}
			if (hits >= max)
			{
				break;
			}
		}
		await arg.SendMsg(hits == 0 ? ("itemfind: no hit for " + kw) : sb.ToString());
	}

	/// <summary>命中则返回「G/D/P/L + 关键字段」，未命中返回 null。</summary>
	private static string DescribeRow(object row, string kw)
	{
		object keyObj = null;
		Type rt = row.GetType();
		if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
		{
			keyObj = rt.GetProperty("Key").GetValue(row);
			object inner = rt.GetProperty("Value").GetValue(row);
			if (inner == null)
			{
				return null;
			}
			row = inner;
		}
		Type t = row.GetType();
		PropertyInfo[] ps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		List<string> strs = new List<string>();
		string g = "?", d = "?", p = "?", l = "?";
		bool hit = kw == "*";
		foreach (PropertyInfo pi in ps)
		{
			object v;
			try
			{
				v = pi.GetValue(row);
			}
			catch
			{
				continue;
			}
			string name = pi.Name;
			if (name == "Genre") g = Convert.ToString(v);
			else if (name == "Detail") d = Convert.ToString(v);
			else if (name == "Particular") p = Convert.ToString(v);
			else if (name == "Level") l = Convert.ToString(v);

			if (v is string s && s.Length > 0 && s.Length < 400)
			{
				if (s.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					hit = true;
				}
				if (strs.Count < 8)
				{
					strs.Add(name + "=" + s);
				}
			}
			else if (kw == "*" && strs.Count < 8 && v != null && !(v is IEnumerable))
			{
				strs.Add(name + "=" + Convert.ToString(v));
			}
		}
		if (!hit)
		{
			return null;
		}
		string k = keyObj == null ? "" : ("key=" + Convert.ToString(keyObj) + " ");
		return t.Name + "  " + k + g + "/" + d + "/" + p + "/" + l + "  " + string.Join(" | ", strs);
	}
}
