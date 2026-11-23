using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 465 节：DonoDuel_Reset —— 客户端/调试通道直接触发多诺整体重置。
/// </summary>
[CallGSApi("DonoDuel_Reset")]
public class DonoDuelResetHandler : CallGSHandler
{
	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int cleared = DonoDuelReset.Run(player, sync);
		await player.SendPacket(1004, sync);
		return CallGSResult.Ok(new JsonObject
		{
			["nCleared"] = cleared,
			["nSeason"] = player.Attributes.GetValue(DonoDuelReset.BagGid, DonoDuelReset.SeasonSid),
			["nCards"] = player.Attributes.GetValue(DonoDuelReset.BagGid, 2u)
		}.ToJsonString());
	}
}
