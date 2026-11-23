using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_EnterLevel")]
public class DreamCard_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	private static readonly Lazy<DreamCardLevelIndex?> LevelIndex = new Lazy<DreamCardLevelIndex>(LoadLevelIndex);

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		DreamCardEnterLevelParam req = JsonSerializer.Deserialize<DreamCardEnterLevelParam>(param);
		bool flag = req == null || req.LevelId <= 0 || req.Diff <= 0;
		bool flag2 = flag;
		if (!flag2)
		{
			int type = req.Type;
			bool flag3 = ((type < 1 || type > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			await CallGSRouter.SendScript(connection, "DreamCard_EnterLevel", "null");
			return;
		}
		DateTime now = DateTime.Now;
		if (!IsAllowed(req, now))
		{
			await CallGSRouter.SendScript(connection, "DreamCard_EnterLevel", "null");
			return;
		}
		JsonObject response = new JsonObject
		{
			["nSeed"] = Random.Next(1, 1000000000),
			["nID"] = req.LevelId,
			["nDiff"] = req.Diff,
			["nType"] = req.Type
		};
		await CallGSRouter.SendScript(connection, "DreamCard_EnterLevel", response.ToJsonString());
	}

	private static bool IsAllowed(DreamCardEnterLevelParam req, DateTime now)
	{
		DreamCardLevelIndex value = LevelIndex.Value;
		if (value == null)
		{
			return true;
		}
		int type = req.Type;
		if (1 == 0)
		{
		}
		bool result = type switch
		{
			1 => value.OpenOrdinaryLevelIds(now).Contains((uint)req.LevelId), 
			2 => value.IsChallengeOpen((uint)req.LevelId, now), 
			3 => value.IsEndlessOpen((uint)req.LevelId, now), 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static DreamCardLevelIndex? LoadLevelIndex()
	{
		try
		{
			string resourcePath = ConfigManager.Config.Path.ResourcePath;
			string path = Path.Combine(resourcePath, "dlc", "DreamCard");
			List<DreamCardOrdinaryLevelEntry> ordinaryLevels = LoadJson<List<DreamCardOrdinaryLevelEntry>>(Path.Combine(path, "levellist.json")) ?? new List<DreamCardOrdinaryLevelEntry>();
			List<DreamCardChallengeLevelEntry> challengeLevels = LoadJson<List<DreamCardChallengeLevelEntry>>(Path.Combine(path, "challenge.json")) ?? new List<DreamCardChallengeLevelEntry>();
			List<DreamCardEndlessLevelEntry> endlessLevels = LoadJson<List<DreamCardEndlessLevelEntry>>(Path.Combine(path, "endless.json")) ?? new List<DreamCardEndlessLevelEntry>();
			return new DreamCardLevelIndex(ordinaryLevels, challengeLevels, endlessLevels);
		}
		catch
		{
			return null;
		}
	}

	private static T? LoadJson<T>(string path)
	{
		if (!File.Exists(path))
		{
			return default;
		}
		return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
	}
}
