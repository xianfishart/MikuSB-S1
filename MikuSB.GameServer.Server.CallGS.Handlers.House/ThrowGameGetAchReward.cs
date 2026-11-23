using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 冰趣蜜弹（投掷游戏）成就 / 任务奖励领取。
/// 客户端 umg_throw_task 的「领取」按钮走 HouseThrowSeverMessage:GetAchReward(nId)
/// → 发 ThrowGameGetAchReward { FuncName, nAchId }；nAchId 为 -1 表示“全部领取”。
/// 成就清单来自 Resources/house/Throw/ThrowAchiev.json（20930~20949），
/// 完成条件与奖励来自 Resources/achievement/activity_quest.json；
/// 进度存在 Gid 7 属性里：Val 第 0 位 = 已领取，高位 = 进度。
/// 旧实现只回了一个空的 ThrowGameGetReward，客户端因此一直停在“可领取”。
/// </summary>
[HouseFunc("ThrowGameGetAchReward")]
public class ThrowGameGetAchReward : IHouseFuncHandler
{
	private const uint AchGid = 7u;

	private static HashSet<int>? _throwAchIds;

	private static Dictionary<int, JsonObject>? _questConfig;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		int achId = HouseJson.NumField(root, "nAchId");
		HashSet<int> throwIds = LoadThrowAchIds();
		List<int> targets = new List<int>();
		if (achId > 0)
		{
			if (throwIds.Count == 0 || throwIds.Contains(achId))
			{
				targets.Add(achId);
			}
		}
		else
		{
			targets.AddRange(throwIds.OrderBy((int x) => x));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		bool claimedAny = false;
		foreach (int id in targets)
		{
			JsonObject cfg = FindQuest(id);
			if (cfg == null)
			{
				continue;
			}
			int need = Math.Max(1, HouseJson.NumField(cfg, "CheckValue"));
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == AchGid && x.Sid == (uint)id);
			if (attr == null)
			{
				continue;
			}
			if (((attr.Val >> 1) & 0x7FFFFFFF) < (uint)need)
			{
				continue;
			}
			if ((attr.Val & 1u) != 0)
			{
				continue;
			}
			if (cfg["Rewards"] is JsonArray rows)
			{
				foreach (JsonNode node in rows)
				{
					if (!(node is JsonArray arr) || arr.Count < 5)
					{
						continue;
					}
					List<uint> values = arr.Take(5).Select(ReadUInt).ToList();
					if (values.Count < 5 || values[0] == 0)
					{
						continue;
					}
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
					granted.Add(arr.DeepClone());
				}
			}
			attr.Val |= 1u;
			sync.Custom[player.ToPackedAttrKey(AchGid, (uint)id)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(AchGid, (uint)id)] = attr.Val;
			claimedAny = true;
		}
		if (claimedAny)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ThrowGameGetReward",
			["nAchId"] = achId,
			["bReceived"] = claimedAny,
			["tbReward"] = granted.DeepClone(),
			["tbRewards"] = granted.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	private static uint ReadUInt(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}

	private static HashSet<int> LoadThrowAchIds()
	{
		if (_throwAchIds != null)
		{
			return _throwAchIds;
		}
		HashSet<int> set = new HashSet<int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "Throw", "ThrowAchiev.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray list)
			{
				foreach (JsonNode item in list)
				{
					int id = HouseJson.NumField(item as JsonObject, "ID");
					if (id > 0)
					{
						set.Add(id);
					}
				}
			}
		}
		catch
		{
		}
		_throwAchIds = set;
		return set;
	}

	private static JsonObject FindQuest(int id)
	{
		if (_questConfig == null)
		{
			Dictionary<int, JsonObject> map = new Dictionary<int, JsonObject>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray list)
				{
					foreach (JsonNode item in list)
					{
						if (item is JsonObject obj)
						{
							int qid = HouseJson.NumField(obj, "ID");
							if (qid > 0)
							{
								map[qid] = obj;
							}
						}
					}
				}
			}
			catch
			{
			}
			_questConfig = map;
		}
		JsonObject cfg;
		return _questConfig.TryGetValue(id, out cfg) ? cfg : null;
	}
}
