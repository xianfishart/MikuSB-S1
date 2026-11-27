using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

/// <summary>
/// 第 585 节：主界面入口显示开关（当前只有「归途道标」一项）。
///
/// 归途道标 = 回归活动入口。主界面上的 BtnReturn 挂在一格 CanvasPanel_20 里，
/// 显不显示由客户端 umg_main.ShowBtnReturn 里的 PlayerReturn.IsOpen() 决定
/// （热注入扫 ShowBtnReturn 常量表实测：SetCollapsedOrSelfHitTestInvisible(CanvasPanel_20)）。
///
/// 所以开关不做服务端活动窗口的事，只写一个文本标记，让客户端补丁
/// （工具\bp\hudret.lua）在 IsOpen 上返回 false，原生收格逻辑自己把这一格并掉。
/// 好处：不碰活动数据，随时可逆；关掉后活动列表里的同名条目也一起消失。
///
/// 状态存放：Config\HudEntryRet.txt，内容 1 = 显示、0 = 隐藏；文件不存在按显示处理。
/// 网页控制台点一次 => 写文件 + 立即把补丁推给所有在线客户端（不用重登）。
/// 重登时由 DlcClientPatches 的常驻清单再 dofile 一次，读同一个文件，状态自动延续。
/// </summary>
public static class HudEntrySwitch
{
	private static readonly object Gate = new object();

	private static bool? _cache;

	private static string FlagPath()
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "HudEntryRet.txt");
	}

	/// <summary>当前是否显示归途道标入口。文件缺失/内容异常一律按显示处理。</summary>
	public static bool ReturnVisible()
	{
		lock (Gate)
		{
			if (_cache.HasValue)
			{
				return _cache.Value;
			}
			bool value = true;
			try
			{
				string path = FlagPath();
				if (File.Exists(path))
				{
					string text = File.ReadAllText(path).Trim();
					if (text.StartsWith("0", StringComparison.Ordinal))
					{
						value = false;
					}
				}
			}
			catch
			{
			}
			_cache = value;
			return value;
		}
	}

	/// <summary>写入开关并立即推给在线客户端。返回纯文本结果说明。</summary>
	public static string SetReturnVisible(bool visible)
	{
		string path = FlagPath();
		try
		{
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.WriteAllText(path, visible ? "1" : "0");
		}
		catch (Exception ex)
		{
			return "写入开关失败：" + ex.Message;
		}
		lock (Gate)
		{
			_cache = visible;
		}
		int pushed = PushToOnline();
		string state = visible ? "显示" : "隐藏";
		return $"归途道标已{state}（已通知 {pushed} 个在线客户端）";
	}

	/// <summary>把补丁下发给所有在线客户端。返回送达数量。</summary>
	public static int PushToOnline()
	{
		string lua = BuildLua();
		if (lua == null)
		{
			return 0;
		}
		JsonObject jsonObject = new JsonObject();
		jsonObject["code"] = lua;
		string payload = jsonObject.ToJsonString();
		int count = 0;
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections)
		{
			try
			{
				if (item.Value is Connection { Player: not null } connection)
				{
					CallGSRouter.SendScript(connection, "GM", payload).GetAwaiter().GetResult();
					count++;
				}
			}
			catch
			{
			}
		}
		return count;
	}

	/// <summary>
	/// 注入脚本：先塞开关文件路径，再 dofile 补丁本体。
	/// 补丁本体在 工具\bp\hudret.lua（随整合包下发），这里只传路径，避免长脚本走控制台。
	/// </summary>
	private static string BuildLua()
	{
		string dir = ToolsScriptDir();
		if (dir == null)
		{
			return null;
		}
		string flag = FlagPath().Replace('\\', '/');
		return "_G.MikuHudFlag = [[" + flag + "]] dofile([[" + dir + "/hudret.lua]]) return [[hudret]]";
	}

	/// <summary>
	/// 与 DlcClientPatches.ToolsScriptDir 同一套候选顺序：
	/// ① 服务端目录\工具\bp（整合包布局）；② 当前目录\工具\bp；③ 本机开发目录。
	/// </summary>
	private static string ToolsScriptDir()
	{
		try
		{
			string[] candidates = new string[]
			{
				Path.Combine(AppContext.BaseDirectory, "工具", "bp"),
				Path.Combine(Directory.GetCurrentDirectory(), "工具", "bp"),
				"D:\\DSH\\.tools"
			};
			foreach (string dir in candidates)
			{
				if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
				{
					return dir.Replace('\\', '/').TrimEnd('/');
				}
			}
		}
		catch
		{
		}
		return "工具/bp";
	}
}
