using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Dlc;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichmanLogic_CheckOpenAct")]
public class RichmanLogic_CheckOpenAct : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		// 心动派对的棋类活动按版本分了好几期：1=远古、2=2023-11、3=第 9 期（2024-07）、
		// 4=第 30 期（2025-07）。原来这里写死只认 2/4，第 9 期永远拿到第 30 期的 id，
		// 客户端按第 3 号活动去开就白屏（第 242 节）。这里按当前 pin 的期号给对应 id。
		uint era = DlcRuntimeOverrides.EffectiveDlcId;
		uint want = ((era == 9u) ? 3u : ((era == 30u) ? 4u : 0u));
		if (want != 0u && ChessActivityService.Row(want) != null)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject
			{
				["bOpen"] = true,
				["nId"] = want,
				["nBeginTime"] = num - 3600,
				["nEndTime"] = num + 2592000
			}));
		}
		ChessActivityExcel chessActivityExcel = ChessActivityService.SelectActivity(DateTime.Now, 4u, 2u);
		bool flag = chessActivityExcel == null;
		bool flag2 = flag;
		if (!flag2)
		{
			uint id = chessActivityExcel.Id;
			bool flag3 = ((id == 2 || id == 4) ? true : false);
			flag2 = !flag3;
		}
		if (flag2)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bOpen"] = false }));
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["bOpen"] = true,
			["nId"] = chessActivityExcel.Id,
			["nBeginTime"] = num - 3600,
			["nEndTime"] = num + 2592000
		}));
	}
}
