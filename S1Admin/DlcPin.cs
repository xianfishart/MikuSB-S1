using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace S1Admin;

public static class DlcPin
{
	/// <summary>
	/// 「游戏默认」（不固定任何一期）用的哨兵期号。
	/// 之前用 0 表示默认，但 0 在整条链路上还有「没显式写 / 读取失败 / 无效期」
	/// 三种含义，任何一处 TryGetValue(0) 失败后的分支都可能把默认态带回某一期
	/// （历史上就被带回过第 11 期）。改成 99：它天然不在 dlc_activities.json 里，
	/// 只是一个「明确的、非零的」默认标记，各处判断统一走 IsDefault（第 226 节）。
	/// </summary>
	public const uint DefaultEra = 99u;

	/// <summary>是否「游戏默认」态。兼容历史遗留的 0。</summary>
	public static bool IsDefault(uint era)
	{
		return era == 0u || era == DefaultEra;
	}

	/// <summary>
	/// 下发客户端用的期号：默认态一律给 0。
	/// 客户端那 26 个补丁脚本里到处是 <c>_G.MikuDmEra ~= 0</c> 这种判断，
	/// 保持 0 就不用动客户端语义（第 226 节）。
	/// </summary>
	public static uint ClientEra(uint era)
	{
		return IsDefault(era) ? 0u : era;
	}

	private static readonly object Gate = new object();

	private static string _cachedConfigStamp = "";

	private static int _cachedPin = -1;

	private static string _cachedActivityStamp = "";

	private static HashSet<int> _cachedIds;

	public static uint Override()
	{
		int num = ReadPin();
		if (num == (int)DefaultEra)
		{
			return DefaultEra;
		}
		if (num <= 0)
		{
			return 0u;
		}
		if (!ActivityExists(num))
		{
			return 0u;
		}
		return (uint)num;
	}

	private static int ReadPin()
	{
		try
		{
			string path = OwnPath();
			if (File.Exists(path))
			{
				string text = File.GetLastWriteTimeUtc(path).Ticks + ":own";
				lock (Gate)
				{
					if (text == _cachedConfigStamp)
					{
						return _cachedPin;
					}
				}
				int num = 0;
				bool hasId = false;
				if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject jsonObject && jsonObject["id"] != null)
				{
					num = jsonObject["id"].GetValue<int>();
					hasId = true;
				}
				lock (Gate)
				{
					_cachedConfigStamp = text;
					_cachedPin = num;
				}
				// 显式写了 id（含 0 = 游戏默认）就以它为准，
				// 只有文件里没有 id 键时才回退 Config.620.json 的历史快照（第 171 节）。
				return hasId ? num : SnapshotPin();
			}
			string path2 = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
			if (!File.Exists(path2))
			{
				return SnapshotPin();
			}
			string text2 = File.GetLastWriteTimeUtc(path2).Ticks.ToString();
			lock (Gate)
			{
				if (text2 == _cachedConfigStamp)
				{
					return _cachedPin;
				}
			}
			int num2 = 0;
			bool hasPin2 = false;
			if (JsonNode.Parse(File.ReadAllText(path2, Encoding.UTF8)) is JsonObject jsonObject2 && jsonObject2["ServerOption"] is JsonObject jsonObject3 && jsonObject3["PinDlcActivityId"] != null)
			{
				num2 = jsonObject3["PinDlcActivityId"].GetValue<int>();
				hasPin2 = true;
			}
			lock (Gate)
			{
				_cachedConfigStamp = text2;
				_cachedPin = num2;
			}
			return hasPin2 ? num2 : SnapshotPin();
		}
		catch
		{
			return 0;
		}
	}

	private static string OwnPath()
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "DlcPin.json");
	}

	private static int SnapshotPin()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.620.json");
			if (!File.Exists(path))
			{
				return 0;
			}
			return (((!(JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject jsonObject)) ? null : (jsonObject["ServerOption"] as JsonObject))?["PinDlcActivityId"])?.GetValue<int>() ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	private static bool ActivityExists(int id)
	{
		return ActivityIds()?.Contains(id) ?? false;
	}

	public static bool Write(uint id)
	{
		try
		{
			JsonObject jsonObject = new JsonObject { ["id"] = (int)id };
			File.WriteAllText(OwnPath(), jsonObject.ToJsonString());
			lock (Gate)
			{
				_cachedConfigStamp = "";
				_cachedPin = -1;
			}
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
			if (!File.Exists(path))
			{
				return true;
			}
			if (!(JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject jsonObject2))
			{
				return true;
			}
			JsonObject jsonObject3 = jsonObject2["ServerOption"] as JsonObject;
			if (jsonObject3 == null)
			{
				jsonObject3 = (JsonObject)(jsonObject2["ServerOption"] = new JsonObject());
			}
			jsonObject3["PinDlcActivityId"] = (int)id;
			File.WriteAllText(path, jsonObject2.ToJsonString());
			lock (Gate)
			{
				_cachedConfigStamp = "";
				_cachedPin = -1;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static HashSet<int> ActivityIds()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_activities.json");
			if (!File.Exists(path))
			{
				return null;
			}
			string text = File.GetLastWriteTimeUtc(path).Ticks.ToString();
			lock (Gate)
			{
				if (text == _cachedActivityStamp && _cachedIds != null)
				{
					return _cachedIds;
				}
			}
			HashSet<int> hashSet = new HashSet<int>();
			if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonArray jsonArray)
			{
				for (int i = 0; i < jsonArray.Count; i++)
				{
					if (!(jsonArray[i] is JsonObject jsonObject))
					{
						continue;
					}
					JsonNode jsonNode = jsonObject["ID"];
					if (jsonNode == null)
					{
						jsonNode = jsonObject["Id"];
					}
					if (jsonNode != null)
					{
						try
						{
							hashSet.Add(jsonNode.GetValue<int>());
						}
						catch
						{
						}
					}
				}
			}
			lock (Gate)
			{
				_cachedActivityStamp = text;
				_cachedIds = hashSet;
			}
			return hashSet;
		}
		catch
		{
			return null;
		}
	}
}
