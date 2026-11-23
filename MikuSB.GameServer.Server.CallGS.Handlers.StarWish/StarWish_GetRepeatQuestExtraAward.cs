using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

/// <summary>
/// 星祈心愿「希冀点缀」单个宝箱领取 —— 第 440 节重写。
///
/// 客户端（StarWishLogic.GetRepeatQuestExtraAward）点某个宝箱时发的是
///   me:CallGS('StarWish_GetRepeatQuestExtraAward', json.encode({ nPos = 1..3 }))
/// 宝箱「是否已领取」由 StarWishLogic.GetRepeatQuestExtraAwardState(i) 决定，
/// 它等价于 GetBits(me:GetAttribute(180, 4), i) —— 注意这里是**从 0 数**的位：
/// 属性 180/4 = 7 时，界面只把 30、30 两个箱子打勾，第三档 40 仍是亮的（第 439 节现象）。
/// 所以宝箱 nPos 对应 180/4 的 bit nPos，而旧实现用 bit (nPos-1) 判重，
/// 恰好把「第三档」当成「已领过」，点 40 就没有任何反应。
///
/// 现在的口径：
///   · 宝箱 i 的解锁条件 = 今天已提交（组 7 / 任务 ID 的 bit0）的任务数 ≥ i；
///   · 领取时只发第 i 档（main.json RepeatQuestAward[i-1]），并置 180/4 的 bit i；
///   · 已领过 / 没解锁再点只把当前位推一次，不重复发奖。
/// </summary>
[CallGSApi("StarWish_GetRepeatQuestExtraAward")]
public class StarWish_GetRepeatQuestExtraAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 跨 4 点先换日，避免凌晨点箱子吃到昨天的进度。
		StarWishDailyService.EnsureReset(player, save: true, sync: sync);

		JsonArray granted = new JsonArray();
		StarWishMainExcel act = StarWishAttr.Act();
		if (act != null)
		{
			int pos = 0;
			try
			{
				pos = (int)StarWishQuests.ReadId(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nPos"]);
			}
			catch
			{
			}
			uint got = StarWishAttr.Read(player, StarWishDailyService.ExtraAwardSid);
			uint boxBit = (pos > 0 && pos < 32) ? (1u << pos) : 0u;
			int done = StarWishQuests.ClaimedToday(player);
			if (pos > 0 && pos <= act.RepeatQuestAwards.Count && boxBit != 0 && (got & boxBit) == 0 && done >= pos)
			{
				List<uint> row = act.RepeatQuestAwards[pos - 1];
				MikuSB.Util.Logger.GetByClassName().Info($"[SW] box claim pos={pos} row=[{string.Join(",", row)}] done={done}");
				foreach (JsonNode item in await StarWishQuests.GrantAllAsync(player, new List<uint>[1] { row }, sync))
				{
					granted.Add(item?.DeepClone());
				}
				got |= boxBit;
				StarWishAttr.Set(player, StarWishDailyService.ExtraAwardSid, got, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
			else
			{
				// 未解锁 / 已领过：把当前位再推一次，客户端据此刷新勾选状态。
				StarWishAttr.Set(player, StarWishDailyService.ExtraAwardSid, got, sync);
			}
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetRepeatQuestExtraAward",
			["tbAward"] = granted
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetRepeatQuestExtraAward", rsp.ToJsonString(), sync);
	}
}
