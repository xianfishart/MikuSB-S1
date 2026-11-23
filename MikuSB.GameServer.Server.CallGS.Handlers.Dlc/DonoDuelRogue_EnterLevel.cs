using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuelRogue_EnterLevel")]
public class DonoDuelRogue_EnterLevel : CallGSHandler<DonoDuelRogueEnterLevelParam>
{
	private static readonly Logger Log = new Logger("DonoDuelRogue_EnterLevel");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueEnterLevelParam request)
	{
		int value = context.Connection.Player?.Uid ?? 0;
		if (request.LevelId == 0)
		{
			Log.Warn($"Donoduel rogue entry without a room id requested by uid={value}.");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
		Log.Info($"Donoduel rogue room {request.LevelId} entered by uid={value} (seed={num}).");
		// 第 463 节：进关卡前再兜一次底——局内背包 InitFightBag 读的是肉鸽编队
		// （185/10~13），编队为空就会「暂无多诺卡」。登录时已经补过一次，
		// 这里覆盖「登录后手动清空编队 / 新号刚接 101」的情况。
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance? player = context.Connection.Player;
		if (player != null)
		{
			DonoDuelStarterDeck.Ensure(player, sync);
		}
		JsonObject jsonObject = new JsonObject { ["nSeed"] = num };
		return Task.FromResult(CallGSResult.Ok(jsonObject.ToJsonString(), sync));
	}
}
