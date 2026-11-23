using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ThrowGameSettlement")]
public class ThrowGameSettlement : IHouseFuncHandler
{
	private const uint HouseBeachInfoStart = 17000u;

	private const uint CurLevelOffset = 3u;

	private const uint CurExpOffset = 4u;

	private const uint ThrowGameChallengePointsOffset = 2u;

	private const uint ThrowGameUnlockDrinkStartOffset = 50u;

	private const int ThrowGameModeChallenge = 2;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		PlayerInstance player = connection.Player;
		int modeType = HouseJson.NumField(root, "nModeType");
		int score = Math.Max(0, HouseJson.NumField(root, "nScore"));
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonNode jsonNode = root["tbUnlockDrink"];
		JsonArray drinks = default;
		int num;
		if (jsonNode is JsonObject unlockDrink)
		{
			jsonNode = unlockDrink["nDrinkIDs"];
			drinks = jsonNode as JsonArray;
			num = ((drinks != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			foreach (JsonNode drinkNode in drinks)
			{
				int drinkId = HouseJson.ToInt(drinkNode);
				if (drinkId > 0)
				{
					uint sid = (uint)(17050 + drinkId);
					uint prev = HouseAttr.Read(player, sid);
					await HouseAttr.SetAsync(connection, sid, prev + 1, sync);
				}
			}
		}
		if (modeType == 2 && score > 0)
		{
			uint sid2 = 17002u;
			uint prev2 = HouseAttr.Read(player, sid2);
			if ((uint)score > prev2)
			{
				await HouseAttr.SetAsync(connection, sid2, (uint)score, sync);
			}
		}
		int addExp = CalculateExp(root, score);
		int level = Math.Max(1, (int)HouseAttr.Read(player, 17003u));
		int exp = (int)HouseAttr.Read(player, 17004u) + addExp;
		List<int> thresholds;
		for (thresholds = LoadThresholds(); level < thresholds.Count && exp >= thresholds[level]; level++)
		{
			exp -= thresholds[level];
		}
		if (level >= thresholds.Count)
		{
			level = thresholds.Count;
			exp = 0;
		}
		await HouseAttr.SetAsync(connection, 17003u, (uint)level, sync);
		jsonNode = root["tbFinishAchievId"];
		if (jsonNode is JsonArray finishAchiev)
		{
			Dictionary<int, int> checks = LoadAchievementChecks();
			foreach (JsonNode node in finishAchiev)
			{
				int questId = HouseJson.ToInt(node);
				if (questId > 0)
				{
					int check = ((!checks.TryGetValue(questId, out var value)) ? 1 : value);
					PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)questId);
					if (attr == null)
					{
						attr = new PlayerAttr
						{
							Gid = 7u,
							Sid = (uint)questId,
							Val = 0u
						};
						player.Data.Attrs.Add(attr);
					}
					attr.Val = (attr.Val & 1) | (uint)(Math.Max(1, check) << 1);
					sync.Custom[player.ToPackedAttrKey(7u, (uint)questId)] = attr.Val;
					sync.Custom[player.ToShiftedAttrKey(7u, (uint)questId)] = attr.Val;
				}
			}
		}
		await HouseAttr.SetAsync(connection, 17004u, (uint)Math.Max(0, exp), sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		// 2026-11-12 追加（修复记录第 266 节）：连战模式完成一关就结束。
		// 客户端的结算回调 ThrowGameSettlementRsp 只读 nAddExp，随后把整个回包当作
		// tbParam 交给 GmaeOverRep -> UI.Open("HouseThrowSettlement", tbParam)。
		// 结算界面靠 tbParam 里的 nFinishType / nScore / bFinishGame / tbStarInfo /
		// nChallengeRemainingTime 等字段判断"本关继续挑战"还是"整局结束"。
		// 旧回包只带 4 个经验字段，这些字段全是 nil，界面当成挑战结束，
		// 于是 EndGame 里换完关也直接 SkipToSelect 回主界面。
		// 这里像 HouseRequestScript.Synthesize 一样把请求字段原样回显，再覆盖服务端算出的值。
		JsonObject rsp = new JsonObject();
		foreach (KeyValuePair<string, JsonNode?> kv in root)
		{
			if (!(kv.Key == "FuncName"))
			{
				rsp[kv.Key] = kv.Value?.DeepClone();
			}
		}
		rsp["nAddExp"] = addExp;
		rsp["nLevel"] = level;
		rsp["nCurExp"] = exp;
		rsp["nMaxExp"] = ((level < thresholds.Count) ? thresholds[level] : 0);
		rsp["FuncName"] = "ThrowGameSettlement";
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	private static Dictionary<int, int> LoadAchievementChecks()
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return dictionary;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
			{
				if (item is JsonObject obj)
				{
					int num = HouseJson.NumField(obj, "ID");
					int val = HouseJson.NumField(obj, "CheckValue");
					if (num > 0)
					{
						dictionary[num] = Math.Max(1, val);
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static int CalculateExp(JsonObject root, int score)
	{
		int num = 50;
		int num2 = 100;
		int num3 = 150;
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "Throw";
			buffer[4] = "ThrowExp.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
				{
					if (!(item is JsonObject obj) || HouseJson.NumField(obj, "ID") != 1)
					{
						continue;
					}
					num = HouseJson.NumField(obj, "DefExp");
					num2 = HouseJson.NumField(obj, "MakeExp");
					num3 = HouseJson.NumField(obj, "MakeOrderExp");
					break;
				}
			}
		}
		catch
		{
		}
		int num4 = 0;
		int num5 = 0;
		if (root["ExpResult"] is JsonObject jsonObject)
		{
			if (jsonObject["RoundsIngredient"] is JsonArray source)
			{
				num4 = Enumerable.Count(source, (JsonNode x) => HouseJson.ToInt(x) == 1);
			}
			if (jsonObject["bOrderDrink"] is JsonArray source2)
			{
				num5 = Enumerable.Count(source2, (JsonNode x) => x?.GetValue<bool>() ?? false);
			}
		}
		int num6 = num + num4 * num2 + num5 * num3;
		if (score > num6)
		{
			num6 = score;
		}
		return Math.Clamp(num6, 1, 5000);
	}

	private static List<int> LoadThresholds()
	{
		List<int> list = new List<int> { 0, 200 };
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "Throw";
			buffer[4] = "ThrowAward.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path)).AsArray();
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "Level");
						int val = HouseJson.NumField(obj, "Exp");
						while (list.Count <= num)
						{
							list.Add(1);
						}
						list[num] = Math.Max(1, val);
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
