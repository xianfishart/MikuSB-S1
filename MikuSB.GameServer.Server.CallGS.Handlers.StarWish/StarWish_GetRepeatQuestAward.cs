using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

/// <summary>
/// 星祈心愿「希冀点缀」提交任务 + 领取全部奖励 —— 第 439 节重写。
///
/// 客户端（StarWishLogic.GetRepeatQuestAward）发的是
///   me:CallGS('StarWish_GetRepeatQuestAward', json.encode({ tbTaskID = {...} }))
/// 也就是把「本轮已完成的任务 ID 列表」交上来。
///
/// 原实现完全忽略 tbTaskID，只看属性 180/6 的位：
///   · 点一次就把 3 档奖励全发（30+30+40 = 100 心愿能量），却只把 180/6 置 1 位；
///   · 从不写 组7/任务ID 的 bit0，所以界面上任务永远不会变成「已领取」。
///
/// 现在的口径（官方：每天任选 3 个任务完成即可领取全部奖励）：
///   1. 校验 tbTaskID 里的任务确实已完成（组7 进度 ≥ CheckValue），把 bit0 置 1（= 已提交/已领取）；
///   2. 统计今天一共提交了几个（bit0 的个数），满 3 个且今天还没发过奖（180/6 bit0 = 0）→
///      一次性发放 main.json 的 RepeatQuestAward 全部档位，并置 180/6 bit0 防重复；
///   3. 换日（04:00）由 StarWishDailyService 清零。
/// </summary>
[CallGSApi("StarWish_GetRepeatQuestAward")]
public class StarWish_GetRepeatQuestAward : ICallGSHandler
{
	private const uint ClaimedBit = 1u;

	private const uint ProgressShift = 1u;

	private const int NeedTasksPerDay = 3;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 跨 4 点先换日清空，避免凌晨上线直接吃到昨天的进度。
		StarWishDailyService.EnsureReset(player, save: true, sync: sync);

		JsonArray granted = new JsonArray();
		JsonArray got = new JsonArray();
		StarWishMainExcel act = StarWishAttr.Act();
		if (act != null)
		{
			bool changed = false;

			// 1) 把本次提交的任务标记成「已领取」。
			foreach (uint questId in ParseTaskIds(param))
			{
				if (act.RepeatQuests == null || !act.RepeatQuests.Contains(questId))
				{
					continue;
				}
				if (!GameData.ActivityQuestData.TryGetValue(questId, out ActivityQuestExcel quest))
				{
					continue;
				}
				uint need = (quest.CheckValue == 0) ? 1u : quest.CheckValue;
				PlayerAttr attr = player.Attributes.GetOrCreate(ActivityQuestService.QuestGid, questId);
				uint progress = (attr.Val >> (int)ProgressShift) & 0x7FFFFFFFu;
				if (progress < need)
				{
					// 没做完就交上来 → 不认。
					continue;
				}
				if ((attr.Val & ClaimedBit) == 0)
				{
					attr.Val |= ClaimedBit;
					player.Attributes.SyncTo(sync, attr);
					changed = true;
				}
				got.Add((int)questId);
			}

			// 2) 数今天一共提交了几个任务。
			int claimedCount = 0;
			if (act.RepeatQuests != null)
			{
				foreach (uint questId in act.RepeatQuests)
				{
					PlayerAttr? attr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == ActivityQuestService.QuestGid && x.Sid == questId);
					if (attr != null && (attr.Val & ClaimedBit) != 0)
					{
						claimedCount++;
					}
				}
			}

			// 3) 满 3 个且今天的宝箱还没全领 → 一次性补齐全部档位。
			//    第 440 节：判重看「宝箱位是否铺满」，而不是只看 180/6 的 bit0 ——
			//    玩家逐个点宝箱领完之后 180/6 仍是 0，若只判 flag 会再发一遍 30+30+40。
			uint flag = StarWishAttr.Read(player, StarWishDailyService.RepeatGotSid);
			uint allBoxes = StarWishQuests.AllBoxMask(act.RepeatQuestAwards.Count);
			uint oldBoxes = StarWishAttr.Read(player, StarWishDailyService.ExtraAwardSid);
			bool boxesFull = allBoxes != 0 && (oldBoxes & allBoxes) == allBoxes;
			if (claimedCount >= NeedTasksPerDay && !boxesFull)
			{
				int pos = 0;
				uint boxes = oldBoxes;
				foreach (List<uint> row in act.RepeatQuestAwards)
				{
					pos++;
					// 已经单独领过的档位跳过，只补没领的那几档。
					if ((boxes & (1u << pos)) != 0)
					{
						continue;
					}
					MikuSB.Util.Logger.GetByClassName().Info($"[SW] award row=[{string.Join(",", row)}] claimed={claimedCount}");
					foreach (JsonNode item in await StarWishQuests.GrantAllAsync(player, new List<uint>[1] { row }, sync))
					{
						granted.Add(item?.DeepClone());
					}
					boxes |= 1u << pos;
				}
				StarWishAttr.Set(player, StarWishDailyService.RepeatGotSid, flag | ClaimedBit, sync);
				// 三档宝箱在客户端看的是 180/4 的位（GetRepeatQuestExtraAwardState），一起置上，
				// 否则会出现「奖励已经发了、宝箱却还是暗的」。
				StarWishAttr.Set(player, StarWishDailyService.ExtraAwardSid, boxes | allBoxes, sync);
				changed = true;
			}
			else if (boxesFull && (flag & ClaimedBit) == 0)
			{
				// 逐个点宝箱领完了，但 180/6 没打标记 → 补上，避免下次提交再走一遍整包发放。
				StarWishAttr.Set(player, StarWishDailyService.RepeatGotSid, flag | ClaimedBit, sync);
				changed = true;
			}
			else if ((flag & ClaimedBit) != 0)
			{
				// 第 440 节自愈：今天已经整包领过，但 180/4 的位没铺满
				// （第 439 节按 bit(nPos-1) 写的 7，界面只勾了两个 30），这里补齐。
				if (!boxesFull)
				{
					StarWishAttr.Set(player, StarWishDailyService.ExtraAwardSid, oldBoxes | allBoxes, sync);
					changed = true;
				}
			}

			if (changed)
			{
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}

		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetRepeatQuestAward",
			["tbAward"] = granted,
			["tbID"] = got
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetRepeatQuestAward", rsp.ToJsonString(), sync);
	}

	/// <summary>解析 param 里的 tbTaskID 数组。</summary>
	private static List<uint> ParseTaskIds(string param)
	{
		List<uint> ids = new List<uint>();
		if (string.IsNullOrWhiteSpace(param))
		{
			return ids;
		}
		try
		{
			JsonNode? node = JsonNode.Parse(param);
			if (node?["tbTaskID"] is not JsonArray arr)
			{
				return ids;
			}
			foreach (JsonNode item in arr)
			{
				uint id = StarWishQuests.ReadId(item);
				if (id != 0 && !ids.Contains(id))
				{
					ids.Add(id);
				}
			}
		}
		catch (Exception)
		{
			// 参数异常按空处理。
		}
		return ids;
	}
}
