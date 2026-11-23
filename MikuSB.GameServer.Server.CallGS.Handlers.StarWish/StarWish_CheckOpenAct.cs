using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_CheckOpenAct")]
public class StarWish_CheckOpenAct : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			// 第 439 节：打开活动时顺手做一次 04:00 换日检查，跨天在线的号也能看到新任务。
			StarWishDailyService.EnsureReset(player, save: true, sync: sync);
			StarWishMainExcel act = StarWishAttr.Act();
			JsonObject rsp = new JsonObject { ["FuncName"] = "StarWish_CheckOpenAct" };
			if (act == null)
			{
				rsp["bOpen"] = false;
				await CallGSRouter.SendScript(connection, "StarWish_CheckOpenAct", rsp.ToJsonString(), sync);
				return;
			}
			StarWishAttr.Set(player, 1u, act.Id, sync);
			// 第 424 节：客户端 StarWishLogic.lua:954 的回包处理是
			//   local cfg = StarWishConfig:GetTimeConfig(rsp.nID)
			//   if not cfg then return UI.ShowTip(Text([[tip.congif_err]])) end
			// 只认 nID（时间配置行号），原来只回 nActId → GetTimeConfig(nil) 直接 nil
			// → 卡片点了弹「配置错误」，窗口根本打不开。这里把 nID 一并返回。
			rsp["bOpen"] = true;
			rsp["nID"] = (int)act.Id;
			rsp["nActId"] = (int)act.Id;
			rsp["nScore"] = (int)StarWishAttr.Score(player);
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			rsp["nStartTime"] = now - 3600L;
			rsp["nEndTime"] = now + 2592000L;
			rsp["nLeftTime"] = now + 2592000L;
			await CallGSRouter.SendScript(connection, "StarWish_CheckOpenAct", rsp.ToJsonString(), sync);
		}
	}
}
