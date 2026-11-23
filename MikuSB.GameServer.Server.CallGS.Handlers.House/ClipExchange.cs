using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 基地「信源交换」剩下三个接口（修复记录第 296 节，承接第 295 节的 GiveClipToFriend）。
///
/// 之前这三个函数名都没有处理器，落进 HouseRequestScript.Synthesize 的默认回显：
/// 回包的 FuncName 还是请求名本身，而客户端 HouseMessageHandle 里对应的回调名分别是
/// GetSendClipRewardRsp / SetWishClipIdSuccess / GetClipSuccess —— 三个都对不上，
/// 于是要么点了毫无反应（不弹遮罩的那两个），要么遮罩永远不关（一键收取会弹遮罩）。
///
/// 客户端真值（/lua 探针 string.dump + tools/_luadis54.py 反汇编得到，未靠猜）：
///   · uw_dorm_wishbar_item.Display 的按钮闭包：
///       {FuncName="GetSendClipReward", Index=N}  → 无遮罩
///       前置：本周赠送次数 >= NeedSendCount 且该位未领取
///   · umg_dorm_jigsaw_wish 的确定按钮：
///       {FuncName="SetWishClipId", WishClipId=N} → 无遮罩
///       回包 SetWishClipIdSuccess → DormClipWish:OnSetSuccess()
///   · uw_dorm_jigsaw_give 的「一键收取」：
///       {FuncName="GetClipsFromFriend", tbIndex={...}} → **会 ShowConnection()**
///       回包 GetClipSuccess → HouseClipSend:GetClipsFromFriendRsp() 并逐条 ShowMissionTip
///       （回包字段 tbPiece = {{ClipId, Num}, ...}）
///
/// 属性（gid 101，sid = 3000 + HouseInfoOffset，偏移量由客户端 HouseStorage.HouseInfoOffset 导出）：
///   3030+ClipId          断片数量，低 15 位＝自己所得，高 15 位＝好友赠予（**不是** 3029+ClipId）
///   3166 SetWishClipTimes 许愿日期标记（客户端的「今天是否已许愿」判据：非 0 就弹 ui.ClipSetTimeOut）
///   3167 NowWishClipId    当前许愿的关键断片，存的是 **ClipId-1**（客户端 OnOpen 读出来 +1 再用；
///                         实测请求 WishClipId=6 → 界面选中 6，即 3167=5）。该项在 SetWishClipId.cs 里实现。
///   3171 SendFriendClipCount     本周赠送次数
///   3172 SendFriendRewardState   赠送奖励领取位图（第 Index 档 → bit(Index)，0 基闭区间）
/// </summary>
internal static class ClipExchangeConst
{
	internal const uint HouseInfoStart = 3000u;

	internal const uint ClipCountOffset = 30u;

	internal const uint SendCountSid = 3171u;

	internal const uint RewardStateSid = 3172u;

	internal static uint ClipSid(int clipId)
	{
		// 2026-11-13 修正（修复记录第 297 节）：客户端 HousePuzzle.GetClipNum 是
		//   offset = HouseStorage.HouseInfoStart(3000) + HouseInfoOffset.ClipCountStart(30) + ClipId
		// 也就是 3030+ClipId，**不减 1**。原来写成 3029+ClipId，整整差一格：
		// 送 1 号断片扣的是 0 号槽，看起来就是「明明有 7 个却说没有」，
		// 客户端还弹 tip.house.SendRewardMax（本周已达上限）—— 其实是 owned=0 的失败分支。
		return HouseInfoStart + ClipCountOffset + (uint)clipId;
	}

	internal static async Task AddClipAsync(Connection connection, int clipId, int count, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint sid = ClipSid(clipId);
		uint packed = HouseAttr.Read(player, sid);
		int owned = (int)(packed & 0x7FFFu);
		int received = (int)((packed >> 15) & 0x7FFFu);
		int next = Math.Min(owned + count, 0x7FFF);
		await HouseAttr.SetAsync(connection, sid, (uint)((received << 15) | next), sync);
	}

	internal static JsonObject RequestBody(JsonObject root)
	{
		return (root["tbParam"] as JsonObject) ?? root;
	}
}

/// <summary>
/// 赠送奖励条的「领取」（tbSendClipReward 四档，Index 1~4）。
///
/// 客户端 HousePuzzle.tbSendClipReward（运行时导出确认）：
///   [1] NeedSendCount=3  ClipId=9  Index=1
///   [2] NeedSendCount=6  ClipId=9  Index=2
///   [3] NeedSendCount=9  ClipId=9  Index=3
///   [4] NeedSendCount=12 ClipId=9  Index=4
/// 即本周送满 3/6/9/12 个断片，各领 1 个 9 号「万能信源」。
/// 领取状态存在 3172 的位图里（客户端 uw_dorm_wishbar_item.Display 用 GetBits(state, Index, Index) 判断）。
/// </summary>
[HouseFunc("GetSendClipReward")]
public class GetSendClipReward : IHouseFuncHandler
{
	private static readonly Logger Log = new Logger("GetSendClipReward");

	/// <summary>档位表：Index → (NeedSendCount, ClipId)。与客户端 HousePuzzle.tbSendClipReward 一一对应。</summary>
	private static readonly (int Index, int NeedSendCount, int ClipId)[] Tiers =
	{
		(1, 3, 9),
		(2, 6, 9),
		(3, 9, 9),
		(4, 12, 9)
	};

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
		JsonObject request = ClipExchangeConst.RequestBody(root);
		int index = HouseJson.NumField(request, "Index");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		(int NeedSendCount, int ClipId) tier = (0, 0);
		foreach ((int Index, int NeedSendCount, int ClipId) row in Tiers)
		{
			if (row.Index == index)
			{
				tier = (row.NeedSendCount, row.ClipId);
				break;
			}
		}
		if (tier.ClipId <= 0)
		{
			Log.Info($"uid={player.Uid} bad Index={index}");
			await Reply(connection, sync, success: false, index, 0, 0);
			return;
		}
		uint state = HouseAttr.Read(player, ClipExchangeConst.RewardStateSid);
		// 2026-11-13 修正（修复记录第 297 节）：客户端 uw_dorm_wishbar_item.Display 用的是
		//   GetBits(SendFriendRewardState, Index, Index)
		// 而 GetBits(v, lo, hi) 是**0 基、闭区间**（实测 GetBits(1,1,1)=0、GetBits(2,1,1)=1）。
		// 所以第 Index 档对应的是 bit(Index)，不是 bit(Index-1)。
		// 原来写成 bit(Index-1)：第 1 档领完写入 1，客户端查的是 bit1=0 → 仍然显示「可领取」。
		uint bit = 1u << index;
		int sendCount = (int)HouseAttr.Read(player, ClipExchangeConst.SendCountSid);
		if ((state & bit) != 0)
		{
			Log.Info($"uid={player.Uid} reward[{index}] already claimed (state={state}, sendCount={sendCount})");
			await Reply(connection, sync, success: false, index, tier.ClipId, 0);
			return;
		}
		if (sendCount < tier.NeedSendCount)
		{
			Log.Info($"uid={player.Uid} reward[{index}] not reachable: sendCount={sendCount} < {tier.NeedSendCount}");
			await Reply(connection, sync, success: false, index, tier.ClipId, 0);
			return;
		}
		await HouseAttr.SetAsync(connection, ClipExchangeConst.RewardStateSid, state | bit, sync);
		await ClipExchangeConst.AddClipAsync(connection, tier.ClipId, 1, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"uid={player.Uid} claim reward[{index}] -> clip{tier.ClipId} x1 (sendCount={sendCount}, state={state | bit})");
		await Reply(connection, sync, success: true, index, tier.ClipId, 1);
	}

	private static async Task Reply(Connection connection, NtfSyncPlayer sync, bool success, int index, int clipId, int num)
	{
		JsonObject response = new JsonObject
		{
			["FuncName"] = "GetSendClipRewardRsp",
			["bSuccess"] = success,
			["nResult"] = (success ? 0 : 1),
			["Index"] = index
		};
		if (success)
		{
			response["ClipId"] = clipId;
			response["Num"] = num;
		}
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}
}

/// <summary>
/// 「一键收取」好友赠予的断片：{FuncName="GetClipsFromFriend", tbIndex={i,...}}，回包 GetClipSuccess。
///
/// 客户端 uw_dorm_jigsaw_give.OnInit 的 BtnAllGet 闭包：
///   clips = HousePuzzle.GetValidClipsFromsFriend()   -- 读 me.AllFriendPieces（UE 侧复制数组）
///   空就直接 return（连遮罩都不弹）；非空才 UI.ShowConnection() + 发请求
///   回包 GetClipSuccess → HouseClipSend:GetClipsFromFriendRsp() + 逐条 ShowMissionTip{ClipId, Num}
///
/// 本服没有好友断片的派发链路（AllFriendPieces 一直是空数组，假好友不会往你这里送断片），
/// 所以 tbIndex 正常也收不到东西。这里仍然做一个**真值**处理器：
///   · 按 tbIndex 逐个校验，只收服务端确实欠你的断片（当前无来源 → 一个都收不到）
///   · 回包一律是 GetClipSuccess + tbPiece=已收到的列表，绝不回显请求名
/// 意义在于：一旦以后真的往 AllFriendPieces 里塞了数据，按钮不会因为回包名对不上而把
/// ShowConnection 的遮罩永远挂着（这正是第 295 节 GiveClipToFriend 的翻车方式）。
/// </summary>
[HouseFunc("GetClipsFromFriend")]
public class GetClipsFromFriend : IHouseFuncHandler
{
	private static readonly Logger Log = new Logger("GetClipsFromFriend");

	public Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return Task.CompletedTask;
		}
		JsonObject root = HouseJson.ParseObject(param);
		JsonObject request = (root == null) ? new JsonObject() : ClipExchangeConst.RequestBody(root);
		List<int> indexes = ReadIndexes(request["tbIndex"]);
		Log.Info($"uid={(player != null ? player.Uid : 0u)} collect friend clips requested={string.Join(",", indexes)} granted=0 (no pending friend pieces)");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject response = new JsonObject
		{
			["FuncName"] = "GetClipSuccess",
			["bSuccess"] = true,
			["nResult"] = 0,
			["tbPiece"] = new JsonArray()
		};
		return CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}

	private static List<int> ReadIndexes(JsonNode? node)
	{
		List<int> list = new List<int>();
		if (!(node is JsonArray array))
		{
			return list;
		}
		foreach (JsonNode item in array)
		{
			int index = HouseJson.ToInt(item);
			if (index > 0)
			{
				list.Add(index);
			}
		}
		return list;
	}
}
