using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Chess;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_CheckOpenAct")]
public class RichManLoveLogic_CheckOpenAct : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		ChessActivityExcel chessActivityExcel = ChessActivityService.Row(3u);
		if (chessActivityExcel == null)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["bOpen"] = true,
			["nId"] = 3u,
			["nBeginTime"] = num - 3600,
			["nEndTime"] = num + 2592000
		}));
	}
}
