using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace S1Admin;

public static class ConfigKeeper
{
	private static bool _started;

	private static string _lastPin = "";

	public static void Start()
	{
		if (!_started)
		{
			_started = true;
			Thread thread = new Thread(Loop);
			thread.IsBackground = true;
			thread.Name = "S1AdminConfig";
			thread.Start();
		}
	}

	private static void Loop()
	{
		for (int i = 0; i < 4; i++)
		{
			try
			{
				TryFix();
			}
			catch
			{
			}
			Thread.Sleep(500);
		}
		while (true)
		{
			try
			{
				TryFix();
			}
			catch
			{
			}
			Thread.Sleep(3000);
		}
	}

	[SpecialName]
	private static string get_ConfigPath()
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
	}

	[SpecialName]
	private static string get_SnapshotPath()
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.620.json");
	}

	private static void TryFix()
	{
		string configPath = get_ConfigPath();
		if (!File.Exists(configPath) || !(JsonNode.Parse(File.ReadAllText(configPath, Encoding.UTF8)) is JsonObject jsonObject))
		{
			return;
		}
		bool flag = false;
		JsonObject jsonObject2 = jsonObject["HttpServer"] as JsonObject;
		if (jsonObject2 == null)
		{
			JsonNode jsonNode = (jsonObject["HttpServer"] = new JsonObject());
			jsonObject2 = (JsonObject)jsonNode;
			flag = true;
		}
		if (Ensure(jsonObject2, "AdminPort", 21400))
		{
			flag = true;
		}
		string text2 = ConsoleHost.ReadTokenFile();
		if (text2.Length == 0)
		{
			text2 = "dev-admin-token";
		}
		if (Ensure(jsonObject2, "AdminToken", text2))
		{
			flag = true;
		}
		if (Ensure(jsonObject2, "AdminWebRoot", "Web"))
		{
			flag = true;
		}
		if (Ensure(jsonObject2, "OpenAdminPanel", true))
		{
			flag = true;
		}
		if (jsonObject["ServerOption"] is JsonObject jsonObject3)
		{
			JsonNode jsonNode3 = jsonObject3["PinDlcActivityId"];
			if (jsonNode3 != null)
			{
				string text = jsonNode3.ToString();
				if (text != _lastPin)
				{
					_lastPin = text;
					SaveSnapshot(text);
				}
			}
			else
			{
				int num = LoadSnapshotPin();
				jsonObject3["PinDlcActivityId"] = num;
				_lastPin = num.ToString();
				flag = true;
			}
		}
		if (flag)
		{
			File.WriteAllText(configPath, jsonObject.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
	}

	private static bool Ensure(JsonObject obj, string key, JsonNode value)
	{
		if (obj[key] != null)
		{
			return false;
		}
		obj[key] = value;
		return true;
	}

	private static void SaveSnapshot(string pin)
	{
		try
		{
			JsonObject jsonObject = new JsonObject();
			JsonObject jsonObject2 = new JsonObject();
			if (int.TryParse(pin, out var result))
			{
				jsonObject2["PinDlcActivityId"] = result;
			}
			jsonObject["ServerOption"] = jsonObject2;
			File.WriteAllText(get_SnapshotPath(), jsonObject.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	private static int LoadSnapshotPin()
	{
		try
		{
			if (!File.Exists(get_SnapshotPath()))
			{
				return 35;
			}
			if (!(JsonNode.Parse(File.ReadAllText(get_SnapshotPath(), Encoding.UTF8)) is JsonObject jsonObject))
			{
				return 35;
			}
			if (!(jsonObject["ServerOption"] is JsonObject jsonObject2))
			{
				return 35;
			}
			return jsonObject2["PinDlcActivityId"]?.GetValue<int>() ?? 35;
		}
		catch
		{
			return 35;
		}
	}
}
