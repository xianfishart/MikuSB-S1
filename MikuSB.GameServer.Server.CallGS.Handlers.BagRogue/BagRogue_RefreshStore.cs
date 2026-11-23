using System;
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

[CallGSApi("BagRogue_RefreshStore")]
public class BagRogue_RefreshStore : CallGSHandler
{
	private static readonly Logger Log = new Logger("BagRogue_RefreshStore");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
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
		if (!(JsonNode.Parse(BagRogueService.ReadNodeTable(player)) is JsonObject jsonObject) || !(jsonObject[$"N{bagRogueNodeExcel.NodeId}"] is JsonObject jsonObject2) || BagRogueService.ReadUint(jsonObject2["T"]) != 3)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint num = ((bagRogueNodeExcel.StoreId == 0) ? 1u : bagRogueNodeExcel.StoreId);
		List<uint> list = BagRogueService.StoreRefreshCosts(num);
		uint value = player.Attributes.GetValue(142u, 20u);
		uint num2 = ((list.Count == 0) ? 10u : list[(int)Math.Min(value, list.Count - 1)]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (num2 != 0 && !PortCurrency.TrySpend(player, bagRogueScheduleExcel.CoinType, num2, sync, out string _))
		{
			Log.Info($"BagRogue_RefreshStore: coin not enough uid={player.Uid} type={bagRogueScheduleExcel.CoinType} cost={num2}");
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		uint seed = (uint)(((Environment.TickCount & 0x7FFFFFFF) ^ ((int)(value + 1) * -1640531535)) | 1);
		JsonArray jsonArray = new JsonArray((JsonNode)num);
		JsonObject jsonObject3 = new JsonObject();
		foreach (var (jigsawGoods, num3) in BagRogueService.StoreShelf(num, seed))
		{
			jsonArray.Add(jigsawGoods.Id);
			jsonObject3[$"J{jigsawGoods.Id}"] = ((num3 != 0) ? new JsonArray((JsonNode)num3) : new JsonArray());
		}
		jsonObject2["D"] = jsonArray;
		jsonObject2["JA"] = jsonObject3;
		jsonObject2["S"] = 0;
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync, attr);
		PlayerAttr attr2 = player.Attributes.Set(142u, 20u, value + 1);
		player.Attributes.SyncTo(sync, attr2);
		PortCurrency.Persist(player);
		CallGSRouter.SendScript(context.Connection, "GM", new JsonObject { ["code"] = "local okI, p = pcall(UI.GetUI, [[BackpackShop]])\nif okI and type(p) == [[table]] and p.__bOpen == true and type(p.RefreshShop) == [[function]] then\npcall(p.RefreshShop, p)\nend" }.ToJsonString());
		Log.Info($"BagRogue_RefreshStore: node={bagRogueNodeExcel.NodeId} store={num} cost={num2} times={value + 1} uid={player.Uid}");
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
