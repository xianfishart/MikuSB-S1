using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher）成就奖励领取
/// （2026-11-13 重写，修复记录第 267 节）。
///
/// 客户端链路：umg_arcade_task / umg_arcade_reward 的「领取」
///   → HouseArcadeToSeverMsg:GetAchReward(nId) → 发 ArcadeGameGetAchReward { FuncName, nAchId }
///   → 回包由 GetAchRewardRsp 处理：sErr 提示、tbReward 交 Item.Gain、再刷新奖励页。
///
/// 旧实现走 TaskProgressService.Claim，而它查的是 Resources/achievement/achievement.json，
/// 里面根本没有 24001~24020 这些小游戏成就（它们只在 activity_quest.json 里），
/// 于是每次领取都直接 return 空数组 —— 这就是「点了没反应」的原因。
/// 改用 HouseAchievementClaim.ClaimAsync：按 activity_quest.json 的 CheckValue / Rewards 判定，
/// 进度与已领标记都落在 Gid 7 / Sid = 成就ID（Val 第 0 位 = 已领，高位 = 进度），
/// 与客户端 Achievement.GetProgres / IsReceive 读的是同一份数据。
/// nAchId = -1 视为「全部领取」（奖励页那个一键领取按钮）。
/// </summary>
[HouseFunc("ArcadeGameGetAchReward")]
public class ArcadeGameGetAchReward : IHouseFuncHandler
{
	private static HashSet<int>? _achIds;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int achId = HouseJson.NumField(root, "nAchId");
		if (achId == 0)
		{
			achId = HouseJson.NumField(root, "nId");
		}
		HashSet<int> ids = LoadAchIds();
		List<int> targets = new List<int>();
		if (achId > 0)
		{
			if (ids.Count == 0 || ids.Contains(achId))
			{
				targets.Add(achId);
			}
		}
		else
		{
			targets.AddRange(ids.OrderBy((int x) => x));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (int id in targets)
		{
			foreach (JsonNode node in await HouseAchievementClaim.ClaimAsync(connection, id, sync))
			{
				granted.Add(node?.DeepClone());
			}
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ArcadeGameGetAchReward",
			["nAchId"] = achId,
			["bReceived"] = granted.Count > 0,
			["tbReward"] = granted.DeepClone(),
			["tbRewards"] = granted.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	/// <summary>成就清单取 Resources/house/coinpusher/coinpusher_achv.json（24001~24020）。</summary>
	private static HashSet<int> LoadAchIds()
	{
		if (_achIds != null)
		{
			return _achIds;
		}
		HashSet<int> set = new HashSet<int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "coinpusher", "coinpusher_achv.json");
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
		_achIds = set;
		return set;
	}
}
