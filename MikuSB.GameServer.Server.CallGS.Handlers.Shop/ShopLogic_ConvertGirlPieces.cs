using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_ConvertGirlPieces")]
public class ShopLogic_ConvertGirlPieces : CallGSHandler
{
	private readonly record struct PieceRequest(uint[] PieceGdpl, uint Count);

	private static readonly Logger Log = new Logger("ShopLogic_ConvertGirlPieces");

	private const string AwardApi = "Item_ConvertGirlPieces";

	private const uint SuppliesGenre = 5u;

	private const uint PieceDetail = 4u;

	private const uint CutDetail = 19u;

	private const uint CutLevel = 1u;

	private const uint CutParticularLow = 1u;

	private const uint CutParticularHigh = 2u;

	private const uint CutPerPiece = 10u;

	private const uint MaxPieceCount = 999999u;

	private const int MaxEntries = 200;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonArray body;
		try
		{
			body = (string.IsNullOrWhiteSpace(param) ? null : (JsonNode.Parse(param) as JsonArray));
		}
		catch (JsonException)
		{
			body = null;
		}
		if (body == null || body.Count == 0 || body.Count > 200)
		{
			return Reject(player, $"body is not a usable array of {200} entries or fewer ({param.Length} chars: {Truncate(param)})");
		}
		List<PieceRequest> requests = new List<PieceRequest>(body.Count);
		Dictionary<ulong, uint> wantedPieces = new Dictionary<ulong, uint>();
		Dictionary<uint, uint> awardTotals = new Dictionary<uint, uint>();
		List<uint> awardOrder = new List<uint>();
		uint value;
		foreach (JsonNode node in body)
		{
			if (!(node is JsonObject entry))
			{
				return Reject(player, "element is not an object");
			}
			uint[] pieceGdpl = ReadGdpl(entry["sPieceGDPLN"], 5);
			uint[] girlGdpl = ReadGdpl(entry["sGirlGDPL"], 4);
			if (pieceGdpl == null || girlGdpl == null)
			{
				return Reject(player, "element has no usable sGirlGDPL/sPieceGDPLN");
			}
			if (pieceGdpl[0] != 5 || pieceGdpl[1] != 4)
			{
				return Reject(player, "piece " + Describe(pieceGdpl) + " is not a character piece");
			}
			if (pieceGdpl[1] != girlGdpl[1] || pieceGdpl[2] != girlGdpl[2])
			{
				return Reject(player, "piece " + Describe(pieceGdpl) + " does not belong to card " + Describe(girlGdpl));
			}
			uint count = pieceGdpl[4];
			if (count == 0 || count > 999999)
			{
				return Reject(player, $"piece count {count} is out of range");
			}
			ulong pieceTemplate = GameResourceTemplateId.FromGdpl(pieceGdpl[0], pieceGdpl[1], pieceGdpl[2], pieceGdpl[3]);
			if (!GameData.SuppliesByFullId.TryGetValue(pieceTemplate, out SuppliesExcel piece))
			{
				return Reject(player, "piece " + Describe(pieceGdpl) + " has no supply template");
			}
			int color = piece.Color;
			if (1 == 0)
			{
			}
			switch (color)
			{
			case 3:
			case 4:
				value = 1u;
				break;
			case 5:
				value = 2u;
				break;
			default:
				value = 0u;
				break;
			}
			if (1 == 0)
			{
			}
			uint cutParticular = value;
			if (cutParticular == 0)
			{
				return Reject(player, $"piece {Describe(pieceGdpl)} has unconvertible color {piece.Color}");
			}
			if (!GameData.SuppliesByFullId.ContainsKey(GameResourceTemplateId.FromGdpl(5u, 19u, cutParticular, 1u)))
			{
				return Reject(player, $"converted supply {5u}-{19u}-{cutParticular}-{1u} has no template");
			}
			requests.Add(new PieceRequest(pieceGdpl, count));
			wantedPieces[pieceTemplate] = wantedPieces.GetValueOrDefault(pieceTemplate) + count;
			if (!awardTotals.ContainsKey(cutParticular))
			{
				awardOrder.Add(cutParticular);
			}
			awardTotals[cutParticular] = awardTotals.GetValueOrDefault(cutParticular) + count * 10;
			piece = null;
		}
		List<BaseGameItemInfo> wallets = new List<BaseGameItemInfo>(wantedPieces.Count);
		foreach (KeyValuePair<ulong, uint> item in wantedPieces)
		{
			item.Deconstruct(out var key, out value);
			ulong pieceTemplate2 = key;
			uint wanted = value;
			BaseGameItemInfo wallet = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == pieceTemplate2);
			if (wallet == null || wallet.ItemCount < wanted)
			{
				Log.Info($"Conversion refused, uid={player.Uid}: wants {wanted}x template {pieceTemplate2}, owns {wallet?.ItemCount ?? 0}");
				return CallGSResult.Error("tip.NotEnoughItem");
			}
			wallets.Add(wallet);
		}
		foreach (PieceRequest request in requests)
		{
			if (!PortCurrency.TrySpendItem(player, request.PieceGdpl, request.Count, out string spendError))
			{
				Log.Warn($"Charge failed after the affordability pass, uid={player.Uid} {request.Count}x {Describe(request.PieceGdpl)}: {spendError}");
				return CallGSResult.Error(spendError);
			}
			spendError = null;
		}
		await player.SendPacket(new PacketNtfCallScript(wallets));
		List<uint[]> rewardRows = new List<uint[]>(awardOrder.Count);
		foreach (uint cutParticular2 in awardOrder)
		{
			rewardRows.Add(new uint[5]
			{
				5u,
				19u,
				cutParticular2,
				1u,
				awardTotals[cutParticular2]
			});
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray award = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
		PortCurrency.Persist(player);
		uint spentPieces = wantedPieces.Values.Aggregate(0u, (uint total, uint num) => total + num);
		Log.Info($"Conversion, uid={player.Uid}: {spentPieces} piece(s) into {string.Join(" + ", rewardRows.Select(Describe))}");
		return CallGSResult.Ok(new JsonObject { ["tbAward"] = award }, sync, "Item_ConvertGirlPieces");
	}

	private static CallGSResult Reject(PlayerInstance player, string reason)
	{
		Log.Warn($"Rejected ShopLogic_ConvertGirlPieces request, uid={player.Uid}: {reason}");
		return CallGSResult.Error("error.BadParam");
	}

	private static uint[]? ReadGdpl(JsonNode? node, int parts)
	{
		try
		{
			if (!(node is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value) || string.IsNullOrWhiteSpace(value))
			{
				return null;
			}
			string[] array = value.Split('-');
			if (array.Length != parts)
			{
				return null;
			}
			uint[] array2 = new uint[parts];
			for (int i = 0; i < parts; i++)
			{
				if (!uint.TryParse(array[i].Trim(), out array2[i]))
				{
					return null;
				}
			}
			return array2;
		}
		catch
		{
			return null;
		}
	}

	private static string Describe(uint[] gdpl)
	{
		return string.Join('-', gdpl);
	}

	private static string Truncate(string text)
	{
		return (text.Length <= 200) ? text : (text.Substring(0, 200) + "...");
	}
}
