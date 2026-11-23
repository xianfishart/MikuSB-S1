using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("ChessLogic_CheckOpenAct")]
public class ChessLogic_CheckOpenAct : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		ChessActivityExcel chessActivityExcel = ChessActivityService.SelectActivity(DateTime.Now, 4u, 2u, 1u);
		if (chessActivityExcel == null)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));
		}
		// 第 366 节（2026-11-16）：期号属性必须随回包一起下发。
		// 客户端 ChessLogic.GetOpenID() 读的是 me:GetAttribute(12, 0)，
		// 原来这里只 new 了一个 NtfSyncPlayer 却没交给 CallGSResult，
		// 属性永远到不了客户端 —— 打开「捞金派对」时 actId=0，
		// 章节全灰、标题和倒计时空白，看着像界面坏了。
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (player != null && player.Attributes.GetValue(12u, 0u) != chessActivityExcel.Id)
		{
			PlayerAttr attr = player.Attributes.Set(12u, 0u, chessActivityExcel.Id);
			player.Attributes.SyncTo(sync, attr);
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["bOpen"] = true,
			["nId"] = chessActivityExcel.Id,
			["nBeginTime"] = num - 3600,
			["nEndTime"] = num + 2592000
		}, sync));
	}
}
