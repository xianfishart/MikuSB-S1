using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MikuSB.Database;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Player;

public static class PendingAccountReset
{
	private static readonly Logger Logger = new Logger("AccountReset");

	private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "Config", "Database");

	private static string ResetFlag(int uid)
	{
		return Path.Combine(Dir, $"pending_reset_{uid}.json");
	}

	public static void Request(int uid)
	{
		Directory.CreateDirectory(Dir);
		File.WriteAllText(ResetFlag(uid), $"{{\"uid\":{uid},\"time\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"}}");
		StartClientFlagReset();
		Logger.Info($"account reset requested uid={uid} (takes effect after restart)");
	}

	public static bool HasPending(int uid)
	{
		return File.Exists(ResetFlag(uid));
	}

	public static void ApplyAll()
	{
		try
		{
			if (!Directory.Exists(Dir))
			{
				return;
			}
			string[] files = Directory.GetFiles(Dir, "pending_reset_*.json");
			foreach (string path in files)
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
				if (int.TryParse(fileNameWithoutExtension.Substring("pending_reset_".Length), out var result))
				{
					Apply(result);
				}
			}
		}
		catch (Exception e)
		{
			Logger.Error("pending account reset scan failed", e);
		}
	}

	public static bool Apply(int uid)
	{
		if (!HasPending(uid))
		{
			return false;
		}
		string databaseName = ConfigManager.Config.GameServer.DatabaseName;
		string text = Path.Combine(ConfigManager.Config.Path.DatabasePath, databaseName);
		try
		{
			string text2 = Path.Combine(ConfigManager.Config.Path.DatabasePath, "Backups", $"新号重置_{DateTime.Now:yyyyMMdd_HHmmss}");
			Directory.CreateDirectory(text2);
			if (File.Exists(text))
			{
				File.Copy(text, Path.Combine(text2, databaseName), overwrite: true);
			}
			string[] array = new string[4] { "Player", "character_data", "inventory_data", "lineup_data" };
			foreach (string value in array)
			{
				DatabaseHelper.sqlSugarScope.Ado.ExecuteCommand($"delete from {value} where Uid = {uid}");
			}
			DatabaseHelper.UidInstanceMap.TryRemove(uid, out List<BaseDatabaseDataHelper> _);
			DatabaseHelper.ToSaveUidList.Remove(uid);
			File.Delete(ResetFlag(uid));
			Logger.Info($"account reset applied uid={uid}, backup={text2}");
			return true;
		}
		catch (Exception e)
		{
			Logger.Error($"account reset failed uid={uid}", e);
			return false;
		}
	}

	private static void StartClientFlagReset()
	{
		try
		{
			string text = "D:\\DSH\\tools\\reset_client_firsttime.ps1";
			if (File.Exists(text))
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = "powershell",
					Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + text + "\" -WaitForGame",
					CreateNoWindow = true,
					UseShellExecute = false,
					WindowStyle = ProcessWindowStyle.Hidden
				};
				Process.Start(startInfo);
				Logger.Info("client first-time reset script started (waits for game exit)");
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("client first-time reset script failed: " + ex.Message);
		}
	}
}
