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
/// 基地「信源回收」（修复记录第 370 节）。
///
/// 症状：在卡罗琳处打开「信源回收」，选好断片点确定后一直转圈（卡加载）。
///
/// 根因：客户端 uw_dorm_jigsaw_sellout.OnInit 的 BtnOK 闭包发的是
///   {FuncName="ExchangeClip", tbIndex={{ClipId,Num},...}}
/// 并且发送前先 UI.ShowConnection()。HouseMessageHandle 里只有 ExchangeClipSuccess
/// 这一个回调，服务端既没有 ExchangeClip 处理器、HouseRequestScript.FuncNameMap 也没映射，
/// 于是走默认回显 —— 回包 FuncName 还是 "ExchangeClip"，客户端匹配不到回调，
/// 遮罩永远不关（与第 295/296 节 GiveClipToFriend、GetClipsFromFriend 同一翻车方式）。
///
/// 客户端真值（运行时 string.dump + _lua54dis.py 反汇编）：
///   · 回调 ExchangeClipSuccess(rsp)：
///       UI.ShowTip(tip.ExchangeClipSuccess)
///       UI.GetUI("HouseClipSell"):OnOpen()          -- 重开面板，数量走属性同步刷新
///       HouseLogic.ShowMissionTip{Type=?, nPieceId=9, nNum=rsp.GiveNum}
///     即回包只需要 bSuccess/nResult + GiveNum（发放的 9 号万能断片数量）。
///   · 面板 TxtNum = 已选总数，TxtAll = max(5, ceil(总数/5)*5)，BtnOK 只校验总数 >= 5。
///   · 断片表 house/puzzle/clips.txt：ClipId 1~8 的 CanGC=1，9（万能）/10/11 的 CanGC=0，
///     所以可回收范围就是 1~8。
///   · 数量存 (101, 3030+ClipId)：低 15 位＝自己所得（可回收），高 15 位＝好友赠予。
///     面板 HousePuzzle:GetClipNum(id) 不带第二个参数时只读低 15 位，与这里扣减一致。
///
/// 兑换规则按界面提示 HoldClipsTip「5 个信源断片可以兑换 1 个 9 号万能断片」：
///   give = 总数 / 5，只消耗 give*5 个（余数留在背包，避免选多时白扣）。
/// </summary>
[HouseFunc("ExchangeClip")]
public class ExchangeClip : IHouseFuncHandler
{
	private static readonly Logger Log = new Logger("ExchangeClip");

	/// <summary>5 个断片换 1 个 9 号万能断片。</summary>
	private const int CostPerReward = 5;

	/// <summary>9 号万能信源断片。</summary>
	private const int UniversalClipId = 9;

	/// <summary>clips.txt 里 CanGC=1 的最大 ClipId（1~8 可回收，9~11 不可）。</summary>
	private const int MaxRecyclableClipId = 8;

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
		List<(int ClipId, int Num)> items = new List<(int, int)>();
		int total = 0;
		if (request["tbIndex"] is JsonArray tbIndex)
		{
			foreach (JsonNode node in tbIndex)
			{
				if (!(node is JsonObject row))
				{
					continue;
				}
				int clipId = HouseJson.NumField(row, "ClipId");
				int num = HouseJson.NumField(row, "Num");
				if (clipId < 1 || clipId > MaxRecyclableClipId || num <= 0)
				{
					continue;
				}
				items.Add((clipId, num));
				total += num;
			}
		}
		if (total < CostPerReward)
		{
			Log.Info($"uid={player.Uid} reject: total={total} < {CostPerReward}");
			await Reply(connection, new NtfSyncPlayer(), success: false, 0);
			return;
		}
		foreach ((int ClipId, int Num) item in items)
		{
			int owned = OwnedCount(player, item.ClipId);
			if (owned < item.Num)
			{
				Log.Info($"uid={player.Uid} reject: clip{item.ClipId} owned={owned} < {item.Num}");
				await Reply(connection, new NtfSyncPlayer(), success: false, 0);
				return;
			}
		}
		int give = total / CostPerReward;
		int need = give * CostPerReward;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach ((int ClipId, int Num) item in items)
		{
			if (need <= 0)
			{
				break;
			}
			int take = Math.Min(item.Num, need);
			await TakeAsync(connection, item.ClipId, take, sync);
			need -= take;
		}
		await ClipExchangeConst.AddClipAsync(connection, UniversalClipId, give, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"uid={player.Uid} exchange total={total} -> clip{UniversalClipId} x{give}");
		await Reply(connection, sync, success: true, give);
	}

	/// <summary>低 15 位的「自己所得」数量（面板 GetClipNum 不带参数时读的就是这一段）。</summary>
	private static int OwnedCount(PlayerInstance player, int clipId)
	{
		uint packed = HouseAttr.Read(player, ClipExchangeConst.ClipSid(clipId));
		return (int)(packed & 0x7FFF);
	}

	/// <summary>从低 15 位扣减，高 15 位（好友赠予）原样保留。</summary>
	private static async Task TakeAsync(Connection connection, int clipId, int num, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint sid = ClipExchangeConst.ClipSid(clipId);
		uint packed = HouseAttr.Read(player, sid);
		uint owned = packed & 0x7FFF;
		uint received = (packed >> 15) & 0x7FFF;
		uint left = owned - Math.Min(owned, (uint)num);
		await HouseAttr.SetAsync(connection, sid, (received << 15) | left, sync);
	}

	private static Task Reply(Connection connection, NtfSyncPlayer sync, bool success, int giveNum)
	{
		JsonObject response = new JsonObject
		{
			["FuncName"] = "ExchangeClipSuccess",
			["bSuccess"] = success,
			["nResult"] = (success ? 0 : 1),
			["GiveNum"] = giveNum
		};
		return CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}
}
