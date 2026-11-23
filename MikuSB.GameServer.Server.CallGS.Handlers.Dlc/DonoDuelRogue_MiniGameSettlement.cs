using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuelRogue_MiniGameSettlement")]
public class DonoDuelRogue_MiniGameSettlement : CallGSHandler<DonoDuelRogueMiniGameParam>
{
	private static readonly Logger Log = new Logger("DonoDuelRogue_MiniGameSettlement");

	private const uint TurntableConfigId = 17u;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueMiniGameParam request)
	{
		PlayerInstance player = context.Connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		IReadOnlyList<uint> config = GameData.DonoDuelConfigData.GetValueOrDefault(17u)?.Flat();
		uint totalPlays = ((config != null && config.Count > 0) ? config[0] : 0u);
		uint price = ((config != null && config.Count > 1) ? config[1] : 0u);
		if (totalPlays == 0)
		{
			Log.Warn("donoduel_config.json has no ConfigID 17 row; the turntable has no play budget.");
			return DonoDuelGrowth.Bad();
		}
		uint used = player.Attributes.GetValue(185u, 504u);
		if (used >= totalPlays)
		{
			return DonoDuelGrowth.Bad("error.condition_limit");
		}
		DonoDuelCatchRoomMiniGameExcel row = Roll();
		if (row == null)
		{
			Log.Warn("The donoduel turntable pool is empty.");
			return DonoDuelGrowth.Bad();
		}
		if (price != 0 && !PortCurrency.TrySpend(player, DonoDuelGrowth.SeasonCurrency(player), price, sync, out string error))
		{
			return DonoDuelGrowth.Bad(error);
		}
		JsonObject response = new JsonObject { ["nAwardId"] = row.Sort };
		if (row.RewardType == 2 && row.Reward2.Count >= 3)
		{
			List<uint> granted = DonoDuelBag.GrantCards(player, new _003C_003Ez__ReadOnlySingleElementList<(uint, uint, uint)>((row.Reward2[0], row.Reward2[1], row.Reward2[2])), sync);
			if (granted.Count > 0)
			{
				response["nUniqueId"] = granted[0];
			}
			else
			{
				Log.Warn($"The bag is full; turntable card {row.Reward2[0]} was dropped for uid={player.Uid}.");
			}
		}
		else
		{
			if (row.Reward1.Count < 5)
			{
				Log.Warn($"Turntable row {row.Id} carries no usable reward payload (type {row.RewardType}).");
				return DonoDuelGrowth.Bad();
			}
			await player.RewardManager.GrantConfiguredRewardsAsync(new _003C_003Ez__ReadOnlySingleElementList<IReadOnlyList<uint>>(row.Reward1), sync);
		}
		DonoDuelBag.SetSlot(player, 504u, Math.Min(used + 1, totalPlays), sync);
		DonoDuelBag.Save(player);
		Log.Info($"Donoduel turntable in room {request.RoomId} (seed {request.Seed}) rolled slot {row.Sort} (row {row.Id}, type {row.RewardType}) for uid={player.Uid}; plays {used + 1}/{totalPlays}, paid {price} season currency.");
		return CallGSResult.Ok(response, sync);
	}

	private static DonoDuelCatchRoomMiniGameExcel? Roll()
	{
		List<DonoDuelCatchRoomMiniGameExcel> list = new List<DonoDuelCatchRoomMiniGameExcel>();
		ulong num = 0uL;
		foreach (DonoDuelCatchRoomMiniGameExcel value in GameData.DonoDuelMiniGameData.Values)
		{
			if (value.Weight != 0)
			{
				list.Add(value);
				num += value.Weight;
			}
		}
		if (list.Count == 0 || num == 0)
		{
			return null;
		}
		ulong num2 = (ulong)Random.Shared.NextInt64((long)num);
		foreach (DonoDuelCatchRoomMiniGameExcel item in list)
		{
			if (num2 < item.Weight)
			{
				return item;
			}
			num2 -= item.Weight;
		}
		return list[list.Count - 1];
	}
}
