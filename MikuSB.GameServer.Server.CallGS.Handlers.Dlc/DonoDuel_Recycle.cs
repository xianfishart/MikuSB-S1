using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_Recycle")]
public class DonoDuel_Recycle : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_Recycle");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		List<uint> requested = DonoDuelGrowth.ReadUintList(DonoDuelGrowth.ParseObject(param)?["tbRecycle"]);
		if (requested.Count == 0)
		{
			return DonoDuelGrowth.Bad();
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray recycled = new JsonArray();
		(uint, uint) tuple = DonoDuelConfig.RecycleAttr();
		uint perAttr = tuple.Item1;
		uint rewardPerAttr = tuple.Item2;
		uint currency = DonoDuelGrowth.SeasonCurrency(player);
		uint total = 0u;
		foreach (uint uniqueId in requested)
		{
			if (!DonoDuelBag.HasCard(player, uniqueId))
			{
				continue;
			}
			if (DonoDuelBag.IsLocked(player, uniqueId) || DonoDuelBag.IsInAnyFormation(player, uniqueId))
			{
				Log.Info($"Donoduel card {uniqueId} is locked or deployed; not recycled (uid={player.Uid}).");
				continue;
			}
			uint star = DonoDuelBag.GetStar(player, uniqueId);
			uint quality = DonoDuelBag.GetQuality(player, uniqueId);
			uint attributes = DonoDuelBag.GetAttr(player, uniqueId, 1u) + DonoDuelBag.GetAttr(player, uniqueId, 2u) + DonoDuelBag.GetAttr(player, uniqueId, 3u);
			uint value = DonoDuelConfig.RecycleStar(star) + DonoDuelConfig.RecycleQuality(quality);
			if (perAttr != 0)
			{
				value += attributes / perAttr * rewardPerAttr;
			}
			total += value;
			DonoDuelBag.DestroyCard(player, uniqueId, sync);
			recycled.Add(uniqueId);
			Log.Info($"Donoduel card {uniqueId} recycled for {value} (star {star}, quality {quality}) by uid={player.Uid}.");
		}
		if (recycled.Count == 0)
		{
			return DonoDuelGrowth.Bad();
		}
		if (total != 0)
		{
			player.AddCurrency(currency, total, sync);
		}
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel recycle by uid={player.Uid}: {recycled.Count} card(s), {total} currency {currency}.");
		return CallGSResult.Ok(new JsonObject
		{
			["tbRecycle"] = recycled,
			["tbRecycleAward"] = new JsonArray { currency, total }
		});
	}
}
