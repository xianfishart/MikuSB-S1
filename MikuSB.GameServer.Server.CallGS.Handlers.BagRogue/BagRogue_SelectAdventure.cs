using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SelectAdventure")]
public class BagRogue_SelectAdventure : CallGSHandler<BagRogueAdventureParam>
{
	private static readonly Logger Log = new Logger("BagRogue_SelectAdventure");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueAdventureParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		BagRogueNodeExcel bagRogueNodeExcel = BagRogueService.CurrentNode(player);
		if (bagRogueNodeExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		List<uint> list = BagRogueService.NodeOptionIds(player, bagRogueNodeExcel.NodeId, bagRogueNodeExcel);
		if (request.SelectIdx < 1 || request.SelectIdx > list.Count)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (GameData.BagRogueAdventureData.TryGetValue(list[request.SelectIdx - 1], out BagRogueAdventureExcel value) && value.OptionType == 4)
		{
			PlayerAttr attr = player.Attributes.Set(142u, 4u, 0u);
			player.Attributes.SyncTo(sync, attr);
		}
		else
		{
			if (value != null)
			{
				GrantAdventureReward(player, value, sync);
			}
			BagRogueService.MarkCurrentNodeDone(player, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}

	private static void GrantAdventureReward(PlayerInstance player, BagRogueAdventureExcel option, NtfSyncPlayer sync)
	{
		if (option.Options.Count == 0)
		{
			return;
		}
		uint num = option.Options[0];
		if (num == 0)
		{
			return;
		}
		switch (option.OptionType)
		{
		case 2u:
		{
			BagRogueScheduleExcel bagRogueScheduleExcel = BagRogueService.ScheduleOf(player);
			if (bagRogueScheduleExcel != null)
			{
				PortCurrency.Add(player, bagRogueScheduleExcel.CoinType, num, sync);
				Log.Info($"BagRogue adventure {option.Id}: +{num} coin(type {bagRogueScheduleExcel.CoinType}) uid={player.Uid}.");
			}
			break;
		}
		case 3u:
		{
			PlayerAttr attr = player.Attributes.Add(142u, 3u, num);
			player.Attributes.SyncTo(sync, attr);
			Log.Info($"BagRogue adventure {option.Id}: bag size +{num} uid={player.Uid}.");
			break;
		}
		case 9u:
			GrantRandomRelic(player, sync);
			break;
		case 10u:
			if (option.Id >= 12001 && option.Id <= 13999)
			{
				GrantPickedJigsaw(player, num, sync);
			}
			else
			{
				GrantPickedRelic(player, num, sync);
			}
			break;
		case 11u:
			GrantPickedJigsaw(player, num, sync);
			break;
		}
	}

	private static void GrantPickedRelic(PlayerInstance player, uint relicId, NtfSyncPlayer sync)
	{
		BagRogueScheduleExcel bagRogueScheduleExcel = BagRogueService.ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			return;
		}
		int num = -1;
		for (int i = 0; i < 3; i++)
		{
			if (player.Attributes.GetValue(142u, BagRogueService.RelicSid(i)) == 0)
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			PortCurrency.Add(player, bagRogueScheduleExcel.CoinType, 200u, sync);
			BagRogueService.SetRelicRikiFlag(player, relicId, sync);
			Log.Info($"BagRogue relic pick: relic {relicId} slots full -> fallback +{200u} coin(type {bagRogueScheduleExcel.CoinType}) uid={player.Uid}.");
			BagRogueService.RestoreNodeStandard(player, sync);
		}
		else
		{
			Log.Info($"BagRogue relic pick: relic {relicId} into slot {num + 1} uid={player.Uid}.");
			uint value = (uint)(num + 1 << 16) | relicId;
			PlayerAttr attr = player.Attributes.Set(142u, BagRogueService.RelicSid(num), value);
			player.Attributes.SyncTo(sync, attr);
			BagRogueService.SetRelicRikiFlag(player, relicId, sync);
			BagRogueService.RestoreNodeStandard(player, sync);
		}
	}

	private static void GrantPickedJigsaw(PlayerInstance player, uint pieceId, NtfSyncPlayer sync)
	{
		List<uint> list = new List<uint>();
		BagRogueNodeExcel bagRogueNodeExcel = BagRogueService.CurrentNode(player);
		if (bagRogueNodeExcel != null && JsonNode.Parse(BagRogueService.ReadNodeTable(player)) is JsonObject jsonObject && jsonObject[$"N{bagRogueNodeExcel.NodeId}"] is JsonObject jsonObject2 && jsonObject2["JA"] is JsonObject jsonObject3 && jsonObject3[$"J{pieceId}"] is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				uint num = BagRogueService.ReadUint(item);
				if (num != 0)
				{
					list.Add(num);
				}
			}
		}
		BagRogueService.AddBagItem(player, pieceId, list, sync);
		BagRogueService.SetJigsawRikiFlag(player, pieceId, sync);
		Log.Info($"BagRogue jigsaw pick: piece {pieceId} affixes [{string.Join(',', list)}] into bag uid={player.Uid}.");
		BagRogueService.RestoreNodeStandard(player, sync);
	}

	private static void GrantRandomRelic(PlayerInstance player, NtfSyncPlayer sync)
	{
		BagRogueScheduleExcel bagRogueScheduleExcel = BagRogueService.ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			return;
		}
		HashSet<uint> equipped = BagRogueService.EquippedRelics(player).ToHashSet();
		List<uint> list = (from id in BagRogueService.UnlockedRelics(player)
			where !equipped.Contains(id)
			select id).ToList();
		int num = -1;
		for (int num2 = 0; num2 < 3; num2++)
		{
			if (player.Attributes.GetValue(142u, BagRogueService.RelicSid(num2)) == 0)
			{
				num = num2;
				break;
			}
		}
		if (list.Count == 0 || num < 0)
		{
			PortCurrency.Add(player, bagRogueScheduleExcel.CoinType, 200u, sync);
			Log.Info($"BagRogue adventure relic card: pool/candidates={list.Count}, freeSlot={num >= 0}, fallback +{200u} coin uid={player.Uid}.");
		}
		else
		{
			uint num3 = player.Attributes.GetValue(142u, 2u) & 0xFFFF;
			uint value = player.Attributes.GetValue(142u, 7u);
			uint num4 = list[(int)((uint)(player.Uid + (int)(num3 * 3) + (int)value) % (uint)list.Count)];
			uint value2 = (uint)(num + 1 << 16) | num4;
			PlayerAttr attr = player.Attributes.Set(142u, BagRogueService.RelicSid(num), value2);
			player.Attributes.SyncTo(sync, attr);
			BagRogueService.SetRelicRikiFlag(player, num4, sync);
			Log.Info($"BagRogue adventure relic card: relic {num4} into slot {num + 1} uid={player.Uid}.");
		}
	}
}
