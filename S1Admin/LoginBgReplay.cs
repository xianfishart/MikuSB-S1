using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace S1Admin;

/// <summary>
/// 第 525 节：活动复刻期 → 登录界面背景 联动。
///
/// 原理：客户端启动时只认 pak，所以「换背景」= 把事先做好的覆盖包
/// 拷到 Game\Content\Paks\mods\Patch_Miku_LoginBG_100_P.pak。
/// 31 个覆盖包模板放 S1\Resources\loginbg\bg-NN.pak（NN = 切换器菜单编号 00~30），
/// 对照表由服务端直接读桌面那份 txt（用户自己填），map.txt 只补桌面表里没有的期号
/// （格式 era<TAB>编号；99 = 游戏默认）。桌面表同名期号优先。
///
/// 客户端正在运行时目标 pak 被占用，拷贝会失败；此时把编号写进 pending.txt，
/// 下次调用（GM 页面刷新 / 切期 / 服务端重启）会自动补上。
/// </summary>
public static class LoginBgReplay
{
	private static readonly object Gate = new object();

	private const string PakName = "Patch_Miku_LoginBG_100_P.pak";

	private static readonly string LogPath = @"D:\DSH\loginbg_replay.log";

	/// <summary>最大可选编号：0~18 是主视觉/贴图，19~24 是登录场景自带背景底板，25~30 是无贴图期次的 3D 场景。</summary>
	private const int MaxIndex = 30;

	public static string Root
	{
		get
		{
			string cwd = Directory.GetCurrentDirectory();
			if (Directory.Exists(Path.Combine(cwd, "Resources")))
			{
				return cwd;
			}
			return AppContext.BaseDirectory;
		}
	}

	private static string TplDir => Path.Combine(Root, "Resources", "loginbg");

	private static string PendingFile => Path.Combine(TplDir, "pending.txt");

	/// <summary>最近一次套用（或排队）的编号，供 GM 页面显示「当前在用」。</summary>
	private static string CurrentFile => Path.Combine(TplDir, "current.txt");

	private static string MapFile => Path.Combine(TplDir, "map.txt");

	private static string DesktopMap => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
		"登录背景-复刻期对照表.txt");

	private static string TargetPak => Path.GetFullPath(Path.Combine(
		Root, "..", "Game", "Content", "Paks", "mods", PakName));

	private static void Log(string msg)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
			File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
	}

	/// <summary>读对照表：桌面 txt 优先，Resources\loginbg\map.txt 补缺。返回 era -> 编号(0~30)。</summary>
	public static Dictionary<uint, int> LoadMap()
	{
		Dictionary<uint, int> map = new Dictionary<uint, int>();
		foreach (string path in new[] { DesktopMap, MapFile })
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}
				foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#"))
					{
						continue;
					}
					string[] cols = line.Split('\t');
					if (cols.Length < 2)
					{
						cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					}
					if (cols.Length < 2)
					{
						continue;
					}
					if (!uint.TryParse(cols[0].Trim(), out uint era) || era == 0)
					{
						continue;
					}
					// 桌面表第 5 列是编号，map.txt 第 2 列是编号
					string cell = cols.Length >= 5 ? cols[4] : cols[1];
					cell = cell.Trim();
					if (cell.Length == 0 || cell == "-")
					{
						continue;
					}
					// 编号 0~30：0~18 主视觉贴图，19~24 登录场景背景底板，25~30 无贴图期次的 3D 场景（第 527 节）
					// 桌面表先读、map.txt 后读：同名期号保留桌面表的值，避免旧 map 覆盖新编辑
					if (int.TryParse(cell, out int idx) && idx >= 0 && idx <= MaxIndex && !map.ContainsKey(era))
					{
						map[era] = idx;
					}
				}
			}
			catch
			{
			}
		}
		return map;
	}

	/// <summary>切期后调用。返回给 GM 页面的提示；无映射返回 null（不动背景）。</summary>
	public static string Apply(uint eraId)
	{
		lock (Gate)
		{
			int idx;
			try
			{
				if (!LoadMap().TryGetValue(eraId, out idx))
				{
					return null;
				}
			}
			catch
			{
				return null;
			}
			return CopyTemplate(idx, eraId);
		}
	}

	/// <summary>当前（或已排队待生效）的登录背景编号；未知返回 -1。</summary>
	public static int CurrentIndex()
	{
		try
		{
			if (File.Exists(CurrentFile) && int.TryParse(File.ReadAllText(CurrentFile).Trim(), out int idx))
			{
				return idx;
			}
		}
		catch
		{
		}
		try
		{
			if (File.Exists(PendingFile) && int.TryParse(File.ReadAllText(PendingFile).Trim(), out int idx2))
			{
				return idx2;
			}
		}
		catch
		{
		}
		return -1;
	}

	/// <summary>是否还有「客户端占用导致没写进去」的切换在排队。</summary>
	public static bool IsPending()
	{
		try
		{
			return File.Exists(PendingFile);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>GM 页面单独换背景：直接按编号切（不经过期号映射）。</summary>
	public static string ApplyIndex(int idx, uint eraId)
	{
		if (idx < 0 || idx > MaxIndex)
		{
			return $"登录背景编号超出范围（0~{MaxIndex}）";
		}
		lock (Gate)
		{
			return CopyTemplate(idx, eraId);
		}
	}

	/// <summary>补做上次因客户端占用而没写成功的切换。</summary>
	public static void ApplyPending()
	{
		try
		{
			if (!File.Exists(PendingFile))
			{
				return;
			}
			if (!int.TryParse(File.ReadAllText(PendingFile).Trim(), out int idx))
			{
				File.Delete(PendingFile);
				return;
			}
			lock (Gate)
			{
				CopyTemplate(idx, 0);
			}
		}
		catch
		{
		}
	}

	private static string CopyTemplate(int idx, uint eraId)
	{
		string src = Path.Combine(TplDir, string.Format(CultureInfo.InvariantCulture, "bg-{0:D2}.pak", idx));
		if (!File.Exists(src))
		{
			Log($"缺少模板 bg-{idx:D2}.pak（era={eraId}）");
			return $"登录背景：缺少模板 bg-{idx:D2}.pak，未切换";
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(TargetPak)!);
			File.Copy(src, TargetPak, true);
			if (File.Exists(PendingFile))
			{
				File.Delete(PendingFile);
			}
			try
			{
				Directory.CreateDirectory(TplDir);
				File.WriteAllText(CurrentFile, idx.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
			}
			catch
			{
			}
			Log($"已套用 bg-{idx:D2}（era={eraId}）-> {TargetPak}");
			return $"登录背景已切到 {idx} 号（关掉游戏后重新 /game 才会看到，登录界面不会热切换）";
		}
		catch (Exception ex)
		{
			try
			{
				Directory.CreateDirectory(TplDir);
				File.WriteAllText(PendingFile, idx.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
				File.WriteAllText(CurrentFile, idx.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
			}
			catch
			{
			}
			Log($"写入失败（era={eraId}, idx={idx}）：{ex.GetType().Name} {ex.Message}；已记 pending");
			return $"登录背景 {idx} 号已排队：游戏客户端正在运行、占用着包。请关掉游戏，再在 MikuSB 输入 /game（启动前会自动套用）";
		}
	}
}
