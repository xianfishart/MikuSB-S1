using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_LevelSettlement")]
public class DreamCard_LevelSettlement : ICallGSHandler
{
	private const uint LevelGroupId = 152u;

	private const uint LevelSubNum = 10u;

	private const int OrdinaryType = 1;

	private const int ChallengeType = 2;

	private const int EndlessType = 3;

	private static readonly Lazy<DreamCardSettlementIndex?> SettlementIndex = new Lazy<DreamCardSettlementIndex>(LoadIndex);

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonObject, NtfSyncPlayer) tuple = HandleSettlement(connection.Player, JsonNode.Parse(param));
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "DreamCard_LevelSettlement", arg: response.ToJsonString());
	}

	public static (JsonObject Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
	{
		DreamCardLevelSettlementParam dreamCardLevelSettlementParam = ((tbParam != null) ? tbParam.Deserialize<DreamCardLevelSettlementParam>() : null);
		bool flag = dreamCardLevelSettlementParam == null || dreamCardLevelSettlementParam.LevelId <= 0 || dreamCardLevelSettlementParam.Diff <= 0;
		bool flag2 = flag;
		if (!flag2)
		{
			int type = dreamCardLevelSettlementParam.Type;
			bool flag3 = ((type < 1 || type > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		JsonObject jsonObject = new JsonObject
		{
			["nID"] = dreamCardLevelSettlementParam.LevelId,
			["nDiff"] = dreamCardLevelSettlementParam.Diff,
			["nType"] = dreamCardLevelSettlementParam.Type
		};
		switch (dreamCardLevelSettlementParam.Type)
		{
		case 1:
			HandleOrdinary(player, ntfSyncPlayer, jsonObject, dreamCardLevelSettlementParam);
			break;
		case 2:
			HandleChallenge(player, ntfSyncPlayer, jsonObject, dreamCardLevelSettlementParam);
			break;
		case 3:
			HandleEndless(jsonObject, dreamCardLevelSettlementParam);
			break;
		}
		// 第 494 节：奖励页「心流奇境」的活动成就（组 7 / 20201~20241）按本局统计推进。
		// 客户端结算包里本来就带了棋子/礼物/销毁/层数/漫溯等级/流派能量，此前服务端直接丢掉，
		// 于是整页恒为 0/N、领取按钮永远灰。
		try
		{
			MikuSB.GameServer.Game.Activity.DreamCardAchievementService.AdvanceBySettlement(
				player,
				dreamCardLevelSettlementParam.Type,
				dreamCardLevelSettlementParam.Stage,
				dreamCardLevelSettlementParam.Level,
				dreamCardLevelSettlementParam.Score,
				dreamCardLevelSettlementParam.CardStarCount,
				dreamCardLevelSettlementParam.TreStarCount,
				dreamCardLevelSettlementParam.DestroyCount,
				dreamCardLevelSettlementParam.BuildEnergy,
				ntfSyncPlayer);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("[DreamCard] achievement advance failed: " + ex.Message);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return (Response: jsonObject, Sync: ntfSyncPlayer);
	}

	private static void HandleOrdinary(PlayerInstance player, NtfSyncPlayer sync, JsonObject response, DreamCardLevelSettlementParam req)
	{
		uint num = (uint)(10uL * (ulong)req.LevelId);
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 152u, num + 1);
		orCreateAttr.Val++;
		SyncAttr(sync, player, orCreateAttr);
		PlayerAttr orCreateAttr2 = GetOrCreateAttr(player.Data, 152u, num + 2);
		orCreateAttr2.Val = Math.Max(orCreateAttr2.Val, (uint)req.Diff);
		SyncAttr(sync, player, orCreateAttr2);
		PlayerAttr orCreateAttr3 = GetOrCreateAttr(player.Data, 152u, num + 3);
		orCreateAttr3.Val = MergeDifficultyBits(orCreateAttr3.Val, req.Diff, req.StarValue);
		SyncAttr(sync, player, orCreateAttr3);
		if (TryGetOrdinaryRewardId((uint)req.LevelId, (uint)req.Diff, out var rewardId) && rewardId != 0)
		{
			response["nRewardID"] = rewardId;
			// 第 495 节：nRewardID 只是让客户端画奖励图标，真正发放得服务端自己做。
			// level_reward.json 的 MoonNum = [货币类型 52 潜质点, 数量]。
			DreamCardLevelRewardTable.GrantOnClear(player, sync, rewardId);
		}
	}

	/// <summary>供登录补发使用：按 (关卡号, 难度) 查 levellist 的 RewardID。</summary>
	public static bool TryGetOrdinaryReward(uint levelId, uint diff, out uint rewardId)
	{
		rewardId = 0u;
		return SettlementIndex.Value?.TryGetOrdinaryRewardId(levelId, diff, out rewardId) ?? false;
	}

	private static void HandleChallenge(PlayerInstance player, NtfSyncPlayer sync, JsonObject response, DreamCardLevelSettlementParam req)
	{
		uint num = (uint)(10uL * (ulong)req.LevelId);
		uint sid = (uint)((int)num + req.Diff + 4);
		uint num2 = (uint)Math.Max(0, req.Score);
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 152u, sid);
		bool flag = num2 > orCreateAttr.Val;
		orCreateAttr.Val = Math.Max(orCreateAttr.Val, num2);
		SyncAttr(sync, player, orCreateAttr);
		uint num3 = ResolveCurrentChallengePeriodId(DateTime.Now);
		if (num3 != 0)
		{
			PlayerAttr orCreateAttr2 = GetOrCreateAttr(player.Data, 152u, 0u);
			orCreateAttr2.Val = num3;
			SyncAttr(sync, player, orCreateAttr2);
		}
		response["NewRecord"] = flag;
	}

	private static void HandleEndless(JsonObject response, DreamCardLevelSettlementParam req)
	{
		response["NewRecord"] = false;
	}

	private static uint MergeDifficultyBits(uint currentValue, int diff, int starMask)
	{
		int num = Math.Max(0, diff - 1) * 3;
		uint num2 = currentValue;
		for (int i = 0; i < 3; i++)
		{
			if (((starMask >> i) & 1) != 0)
			{
				num2 |= (uint)(1 << num + i);
			}
		}
		return num2;
	}

	private static bool TryGetOrdinaryRewardId(uint levelId, uint diff, out uint rewardId)
	{
		rewardId = 0u;
		return SettlementIndex.Value?.TryGetOrdinaryRewardId(levelId, diff, out rewardId) ?? false;
	}

	private static uint ResolveCurrentChallengePeriodId(DateTime now)
	{
		return SettlementIndex.Value?.ResolveCurrentChallengePeriodId(now) ?? 0;
	}

	private static DreamCardSettlementIndex? LoadIndex()
	{
		try
		{
			string path = Path.Combine(ConfigManager.Config.Path.ResourcePath, "dlc", "DreamCard");
			List<DreamCardOrdinarySettlementEntry> ordinaryLevels = LoadJson<List<DreamCardOrdinarySettlementEntry>>(Path.Combine(path, "levellist.json")) ?? new List<DreamCardOrdinarySettlementEntry>();
			List<DreamCardChallengeTimeEntry> challengeTimes = LoadJson<List<DreamCardChallengeTimeEntry>>(Path.Combine(path, "chall_time.json")) ?? new List<DreamCardChallengeTimeEntry>();
			return new DreamCardSettlementIndex(ordinaryLevels, challengeTimes);
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

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
