using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 基地「信源交换 — 赠送信源断片」（House_Request: GiveClipToFriend）。
///
/// 2026-11-13 新增（修复记录第 295 节）：原先这个函数名没有处理器，落进
/// HouseRequestScript.Synthesize 的默认回显：回包 FuncName 还是 "GiveClipToFriend"，
/// 而客户端回调表 HouseMessageHandle 里只有 **GiveClipSuccess**，
/// 于是 UI.ShowConnection() 打开的加载遮罩永远没人关 —— 表现就是赠送后卡加载。
///
/// 客户端侧（反汇编 uw_dorm_jigsaw_giveitem.UpdateAction / HouseClipSend:OnGiveSuccess 得到）：
///   请求：{FuncName="GiveClipToFriend", FriendId, ClipId, IsWish=(ClipId==好友需要的断片)}
///   回包：FuncName 必须是 "GiveClipSuccess"，字段只需要两个：
///     HasReward       —— 本次赠送是否拿到了奖励（false 时客户端弹 tip.house.SendRewardMax
///                        「本周通过赠送信源断片获得的奖励已达上限」）
///     WishClipReward  —— 本次关键断片奖励发到的是几号断片（Item.GainForDorm 的第 2 个参数）
///   断片数量本身不走回包，客户端 UpdateClipFunc 直接读 HouseStorage(101 组) 的属性，
///   所以这里必须真扣属性并塞进 NtfSyncPlayer.Custom 一起下发，界面数字才是真值。
///
/// 属性（gid 101，sid = 3000 + HouseInfoOffset）：
///   3030+ClipId  低 15 位＝采集/奖励所得（可赠送），高 15 位＝好友赠送所得（不可再赠送）
///   3170         本周赠送「关键断片」拿到奖励的次数（上限 5）
///   3171         本周赠送次数（每送 1 个 +1，前 20 次每次 1000 通用银）
///   3172         赠送奖励领取位图（按 tbSendClipReward 的 Index 置位，本类不碰）
///   999/3        周标记（yyyy*100+ISO 周，周一 04:00 换周），用于跨周清零上面三个
/// </summary>
[HouseFunc("GiveClipToFriend")]
public class GiveClipToFriend : IHouseFuncHandler
{
	private static readonly Logger Log = new Logger("GiveClipToFriend");

	private const uint Gid = 101u;

	private const uint HouseInfoStart = 3000u;

	private const uint ClipCountOffset = 30u;

	private const uint WishRewardTimesSid = 3170u;

	private const uint SendCountSid = 3171u;

	private const uint RewardStateSid = 3172u;

	private const uint WeeklyMarkerGid = 999u;

	private const uint WeeklyMarkerSid = 3u;

	private const int MaxClipId = 11;

	private const int SilverPerSend = 1000;

	private const int WeeklySilverMax = 20;

	private const int WeeklyWishRewardMax = 5;

	private const uint SilverMoneyType = 3u;

	private static List<(int ClipId, int Weight)>? _wishPool;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		JsonObject request = (root["tbParam"] as JsonObject) ?? root;
		int clipId = HouseJson.NumField(request, "ClipId");
		int friendId = HouseJson.NumField(request, "FriendId");
		bool isWish = ReadBool(request, "IsWish");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool newWeek = await EnsureWeekAsync(connection, sync);
		if (newWeek)
		{
			// 换周清零也要落库，否则服务端重启后本周计数会回到上周的值。
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		if (clipId < 1 || clipId > MaxClipId)
		{
			Log.Info($"uid={player.Uid} bad ClipId={clipId} (friend={friendId})");
			await Reply(connection, sync, success: false, friendId, clipId, isWish, hasReward: false, wishClip: 0);
			return;
		}
		// 2026-11-13 修正（修复记录第 297 节）：客户端 GetClipNum 的算法是
		//   HouseInfoStart(3000) + ClipCountStart(30) + ClipId = 3030+ClipId，不减 1。
		// 原来写成 3029+ClipId → 送 1 号断片扣的是 0 号槽，等于永远 owned=0，
		// 客户端就弹「本周通过赠送信源断片获得的奖励已达上限」（tip.house.SendRewardMax），
		// 而界面上明明显示有 7 个。
		uint clipSid = HouseInfoStart + ClipCountOffset + (uint)clipId;
		uint packed = HouseAttr.Read(player, clipSid);
		int owned = (int)(packed & 0x7FFFu);
		int received = (int)((packed >> 15) & 0x7FFFu);
		if (owned <= 0)
		{
			Log.Info($"uid={player.Uid} give clip{clipId} but owned=0 (received={received}, friend={friendId})");
			await Reply(connection, sync, success: false, friendId, clipId, isWish, hasReward: false, wishClip: 0);
			return;
		}
		// 真扣：只扣低 15 位（自己采集/奖励来的那些），好友赠送来的高 15 位按规则不能再送。
		await HouseAttr.SetAsync(connection, clipSid, (uint)((received << 15) | (owned - 1)), sync);
		int sendCount = (int)HouseAttr.Read(player, SendCountSid) + 1;
		await HouseAttr.SetAsync(connection, SendCountSid, (uint)sendCount, sync);
		bool silver = sendCount <= WeeklySilverMax;
		if (silver)
		{
			PortCurrency.Add(player, SilverMoneyType, SilverPerSend, sync);
		}
		int wishClip = 0;
		if (isWish)
		{
			int wishTimes = (int)HouseAttr.Read(player, WishRewardTimesSid);
			if (wishTimes < WeeklyWishRewardMax)
			{
				await HouseAttr.SetAsync(connection, WishRewardTimesSid, (uint)(wishTimes + 1), sync);
				wishClip = PickWishClip();
				if (wishClip > 0)
				{
					await AddClipAsync(connection, wishClip, 1, sync);
				}
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		bool hasReward = silver || wishClip > 0;
		Log.Info($"uid={player.Uid} give clip{clipId} -> friend {friendId} wish={isWish} "
			+ $"left={owned - 1} sendCount={sendCount} silver={(silver ? SilverPerSend : 0)} wishRewardClip={wishClip}");
		await Reply(connection, sync, success: true, friendId, clipId, isWish, hasReward, wishClip);
	}

	/// <summary>跨周（周一 04:00 起算）清零本周赠送计数，返回是否刚换周。</summary>
	private static async Task<bool> EnsureWeekAsync(Connection connection, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint week = WeekKey(DateTime.Now);
		if (player.GetAttr(WeeklyMarkerGid, WeeklyMarkerSid) == week)
		{
			return false;
		}
		player.SetAttr(WeeklyMarkerGid, WeeklyMarkerSid, week);
		await HouseAttr.SetAsync(connection, SendCountSid, 0u, sync, deleteIfZero: true);
		await HouseAttr.SetAsync(connection, WishRewardTimesSid, 0u, sync, deleteIfZero: true);
		await HouseAttr.SetAsync(connection, RewardStateSid, 0u, sync, deleteIfZero: true);
		Log.Info($"uid={player.Uid} week reset -> {week}");
		return true;
	}

	private static uint WeekKey(DateTime now)
	{
		// 游戏刷新点是周一 04:00，先把时间挪 4 小时再取 ISO 周，避免周一凌晨算到上一周。
		DateTime t = now.AddHours(-4.0);
		return (uint)(t.Year * 100 + ISOWeek.GetWeekOfYear(t));
	}

	private static async Task AddClipAsync(Connection connection, int clipId, int count, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint sid = HouseInfoStart + ClipCountOffset + (uint)clipId;
		uint packed = HouseAttr.Read(player, sid);
		int owned = (int)(packed & 0x7FFFu);
		int received = (int)((packed >> 15) & 0x7FFFu);
		int next = Math.Min(owned + count, 0x7FFF);
		await HouseAttr.SetAsync(connection, sid, (uint)((received << 15) | next), sync);
	}

	private static async Task Reply(Connection connection, NtfSyncPlayer sync, bool success, int friendId,
		int clipId, bool isWish, bool hasReward, int wishClip)
	{
		JsonObject response = new JsonObject
		{
			["FuncName"] = "GiveClipSuccess",
			["bSuccess"] = success,
			["nResult"] = (success ? 0 : 1),
			["FriendId"] = friendId,
			["ClipId"] = clipId,
			["IsWish"] = isWish,
			["HasReward"] = hasReward
		};
		if (wishClip > 0)
		{
			response["WishClipReward"] = wishClip;
		}
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}

	private static bool ReadBool(JsonObject obj, string name)
	{
		JsonNode node = obj[name];
		if (node is JsonValue value)
		{
			if (value.TryGetValue<bool>(out bool b))
			{
				return b;
			}
			if (value.TryGetValue<int>(out int n))
			{
				return n != 0;
			}
		}
		return false;
	}

	private static int PickWishClip()
	{
		List<(int ClipId, int Weight)> pool = LoadWishPool();
		if (pool.Count == 0)
		{
			return 0;
		}
		int total = pool.Sum((ValueTuple<int, int> x) => Math.Max(1, x.Item2));
		int roll = Random.Shared.Next(total);
		foreach ((int clipId, int weight) in pool)
		{
			roll -= Math.Max(1, weight);
			if (roll < 0)
			{
				return clipId;
			}
		}
		return pool[0].ClipId;
	}

	/// <summary>
	/// 关键断片奖励池：clips.json 里 CanGetFromSendWish=1 的断片，按 SendWishDropWeight 加权。
	/// 当前配置是 1~7、9~11 各权重 4，8 号（特异信源）不参与。
	/// </summary>
	private static List<(int ClipId, int Weight)> LoadWishPool()
	{
		if (_wishPool != null)
		{
			return _wishPool;
		}
		_wishPool = new List<(int, int)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "puzzle", "clips.json");
			JsonArray array = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (array != null)
			{
				Dictionary<int, int> weight = new Dictionary<int, int>();
				foreach (JsonNode item in array)
				{
					if (!(item is JsonObject row) || HouseJson.NumField(row, "CanGetFromSendWish") != 1)
					{
						continue;
					}
					int clipId = HouseJson.NumField(row, "ClipId");
					if (clipId <= 0)
					{
						continue;
					}
					int w = HouseJson.NumField(row, "SendWishDropWeight");
					if (w <= 0)
					{
						w = 1;
					}
					weight.TryGetValue(clipId, out int now);
					weight[clipId] = now + w;
				}
				foreach (KeyValuePair<int, int> pair in weight)
				{
					_wishPool.Add((pair.Key, pair.Value));
				}
			}
		}
		catch
		{
		}
		return _wishPool;
	}
}
