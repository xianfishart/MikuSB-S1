using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuelRogue_SelectBondTag")]
public class DonoDuelRogue_SelectBondTag : CallGSHandler<DonoDuelRogueSelectBondTagParam>
{
	private static readonly Logger Log = new Logger("DonoDuelRogue_SelectBondTag");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueSelectBondTagParam request)
	{
		PlayerInstance? player = context.Connection.Player;
		Log.Info($"Donoduel rogue bond tag {request.BondTag} selected by uid={player?.Uid ?? 0}.");
		// 第 480 节：幸运转转机（心跳转转转）的次数存在 185/504，之前只在
		// DonoDuelRogue_MiniGameSettlement 里 +1，从来没有重置的地方，
		// 于是第一局把 2 次玩完，第二局开局客户端就显示 0/2、按钮点不动。
		// donoduel_config 的 ConfigID 17 = [2, 4000] 是「每局可玩次数 / 单次消耗」，
		// 属于局内资源，所以每局开局选羁绊（本接口一局只调一次）时清零。
		if (player != null)
		{
			if (player.Attributes.GetValue(185u, 504u) != 0)
			{
				NtfSyncPlayer sync = new NtfSyncPlayer();
				DonoDuelBag.SetSlot(player, 504u, 0u, sync);
				DonoDuelBag.Save(player);
				Log.Info($"Donoduel turntable plays reset to 0 for the new run, uid={player.Uid}.");
				return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
			}
			return Task.FromResult(CallGSResult.Ok(new JsonObject()));
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject()));
	}
}
