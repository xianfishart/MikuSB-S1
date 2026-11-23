using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_StoreBuyItem")]
public class BagRogue_StoreBuyItem : CallGSHandler<BagRogueStoreBuyParam>
{
	private static readonly Logger Log = new Logger("BagRogue_StoreBuyItem");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueStoreBuyParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		BagRogueNodeExcel bagRogueNodeExcel = BagRogueService.CurrentNode(player);
		if (bagRogueNodeExcel == null || bagRogueNodeExcel.EventType != 3)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		BagRogueScheduleExcel bagRogueScheduleExcel = BagRogueService.ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		if (!(JsonNode.Parse(BagRogueService.ReadNodeTable(player)) is JsonObject jsonObject) || !(jsonObject[$"N{bagRogueNodeExcel.NodeId}"] is JsonObject jsonObject2) || BagRogueService.ReadUint(jsonObject2["T"]) != 3 || !(jsonObject2["D"] is JsonArray jsonArray))
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint selectIdx = request.SelectIdx;
		if (selectIdx < 2 || selectIdx > jsonArray.Count)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num = BagRogueService.ReadUint(jsonObject2["S"]);
		if (((num >> (int)selectIdx) & 1) != 0)
		{
			Log.Info($"BagRogue_StoreBuyItem: slot {selectIdx} already sold uid={player.Uid}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num2 = BagRogueService.ReadUint(jsonArray[(int)(selectIdx - 1)]);
		if (num2 == 0)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num3 = BagRogueService.JigsawPrice(num2);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (num3 != 0 && !PortCurrency.TrySpend(player, bagRogueScheduleExcel.CoinType, num3, sync, out string _))
		{
			Log.Info($"BagRogue_StoreBuyItem: coin not enough uid={player.Uid} type={bagRogueScheduleExcel.CoinType} price={num3}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		jsonObject2["S"] = num | (uint)(1 << (int)selectIdx);
		List<uint> list = new List<uint>();
		if (jsonObject2["JA"] is JsonObject jsonObject3 && jsonObject3[$"J{num2}"] is JsonArray jsonArray2)
		{
			foreach (JsonNode item in jsonArray2)
			{
				uint num4 = BagRogueService.ReadUint(item);
				if (num4 != 0)
				{
					list.Add(num4);
				}
			}
		}
		BagRogueService.AddBagItem(player, num2, list, sync);
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync, attr);
		PortCurrency.Persist(player);
		CallGSRouter.SendScript(context.Connection, "GM", new JsonObject { ["code"] = "local okI, p = pcall(UI.GetUI, [[BackpackShop]])\nif okI and type(p) == [[table]] and p.__bOpen == true and type(p.RefreshShop) == [[function]] then\npcall(p.RefreshShop, p)\nend" }.ToJsonString());
		Log.Info($"BagRogue_StoreBuyItem: node={bagRogueNodeExcel.NodeId} slot={selectIdx} goods={num2} price={num3} uid={player.Uid}");
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
