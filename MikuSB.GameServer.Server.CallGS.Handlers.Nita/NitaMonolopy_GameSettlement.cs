using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

[CallGSApi("NitaMonolopy_GameSettlement")]
public class NitaMonolopy_GameSettlement : CallGSHandler<NitaMonolopyGameSettlementParam>
{
	private static readonly Logger Log = new Logger("NitaMonolopy_GameSettlement");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyGameSettlementParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr playerAttr = player.Attributes.Add(159u, 1u, request.Money);
		player.Attributes.SyncTo(sync, playerAttr);
		PlayerAttr playerAttr2 = player.Attributes.Add(159u, 2u, 1u);
		player.Attributes.SyncTo(sync, playerAttr2);
		// 一局结束：清掉 GID=102 的棋盘存档（整盘 JSON + 各格数据），
		// 否则下次进入 RecoverGame 会把已经结算完的棋盘当成存档恢复出来。
		for (int i = player.Data.StrAttrs.Count - 1; i >= 0; i--)
		{
			if (player.Data.StrAttrs[i].Gid == 102u)
			{
				player.Data.StrAttrs.RemoveAt(i);
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"Nita settlement: money={request.Money}, throws={request.SumThrowCount}, cells={request.SumCellTriger} -> exp={playerAttr.Val}, rounds={playerAttr2.Val} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nMoney"] = request.Money,
			["nEX"] = playerAttr.Val
		}, sync));
	}
}
