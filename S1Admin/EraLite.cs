using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace S1Admin;

/// <summary>
/// 「轻量复刻期」：这些期在本客户端里没有可用的 DLC 版本内容（1.1~1.4 的
/// 版本活动主界面/章节/任务板/商店在这套客户端里都跑不通），所以切过去时
/// 只替换<b>卡池</b>和<b>活动列表</b>（含试玩关），其余 DLC 内容一律保持
/// 游戏默认：版本活动入口隐藏，DLC 配置按默认期（当前客户端版本）下发。
///
/// 名单在 Config/EraLite.json 里，可随时改，不用重编译：
///   { "lite": [1, 2, 3, 4], "dlcEra": 0 }
/// dlcEra = 0（或省略）表示<b>按游戏默认兜底</b>：不钉任何一期，与 GM 里选
/// 「游戏默认」走完全同一条路径（DLC 面板/章节/商店/任务板都交回游戏自身
/// 默认逻辑）。填具体期号才固定到那一期。
/// 文件不存在时用内置默认值（1~4 期、dlcEra = 0 即游戏默认）。
/// </summary>
public static class EraLite
{
	/// <summary>dlcEra 缺省值：0 = 游戏默认（不钉期）。</summary>
	private const uint FallbackDlcEra = 0u;

	private static readonly uint[] FallbackLite = new uint[4] { 1u, 2u, 3u, 4u };

	private static readonly object Gate = new object();

	private static long _stamp = -1L;

	private static HashSet<uint> _lite = new HashSet<uint>(FallbackLite);

	private static uint _dlcEra = FallbackDlcEra;

	private static string PathOf()
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "EraLite.json");
	}

	private static void Reload()
	{
		string path = PathOf();
		long num;
		try
		{
			num = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : -1L;
		}
		catch
		{
			num = -1L;
		}
		lock (Gate)
		{
			if (num == _stamp)
			{
				return;
			}
			_stamp = num;
			if (num < 0)
			{
				_lite = new HashSet<uint>(FallbackLite);
				_dlcEra = FallbackDlcEra;
				return;
			}
		}
		HashSet<uint> hashSet = new HashSet<uint>();
		uint num2 = 0u;
		try
		{
			if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject)
			{
				if (jsonObject["lite"] is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						try
						{
							uint value = item.GetValue<uint>();
							if (value != 0)
							{
								hashSet.Add(value);
							}
						}
						catch
						{
						}
					}
				}
				if (jsonObject["dlcEra"] != null)
				{
					try
					{
						num2 = jsonObject["dlcEra"].GetValue<uint>();
					}
					catch
					{
						num2 = 0u;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log("读 EraLite.json 失败：" + ex.Message);
			return;
		}
		lock (Gate)
		{
			if (hashSet.Count > 0)
			{
				_lite = hashSet;
			}
			_dlcEra = (num2 > 0) ? num2 : FallbackDlcEra;
		}
	}

	/// <summary>这一期是否「只换卡池 + 活动列表」。</summary>
	public static bool Is(uint era)
	{
		if (era == 0)
		{
			return false;
		}
		Reload();
		lock (Gate)
		{
			return _lite.Contains(era);
		}
	}

	/// <summary>
	/// DLC 侧（版本活动入口/窗口/商店/任务板）该用哪一期。轻量期返回 dlcEra，
	/// 其余原样返回。dlcEra = 0 表示游戏默认（不钉期）。
	/// </summary>
	public static uint DlcEra(uint era)
	{
		if (!Is(era))
		{
			return era;
		}
		lock (Gate)
		{
			// 0 不再被替换成某一期：交回游戏自身默认，与 GM 选「游戏默认」一致。
			return _dlcEra;
		}
	}

	public static string Describe(uint era)
	{
		Reload();
		lock (Gate)
		{
			return Is(era) ? $"第 {era} 期为轻量复刻（只换卡池+活动列表，DLC 走{((_dlcEra != 0) ? ("默认期 " + _dlcEra) : "游戏默认")}）" : "";
		}
	}

	private static void Log(string message)
	{
		try
		{
			string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "eralite.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
		}
		catch
		{
		}
	}
}
