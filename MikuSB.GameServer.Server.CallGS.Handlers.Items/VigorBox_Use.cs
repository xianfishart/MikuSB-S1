using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

[CallGSApi("VigorBox_Use")]
public class VigorBox_Use : CallGSHandler<VigorBoxUseParam>
{
	private static readonly Logger Log = new Logger("VigorBox_Use");

	private const int MaxUseCount = 99;

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, VigorBoxUseParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (request.TbList == null || request.TbList.Count == 0 || request.TbList.Count > 99)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		InventoryData inventoryData = player.InventoryManager.InventoryData;
		List<(BaseGameItemInfo, uint)> list = new List<(BaseGameItemInfo, uint)>();
		foreach (uint tb in request.TbList)
		{
			if (!inventoryData.Items.TryGetValue(tb, out BaseGameItemInfo value) || value.ItemCount == 0)
			{
				Log.Warn($"VigorBox_Use uid={player.Uid}: row {tb} is missing or empty; nothing granted.");
				return Task.FromResult(CallGSResult.Error("tip.NotEnoughItem"));
			}
			if (!ItemTemplateInfo.TryResolve(value.TemplateId, out var info) || !info.IsVigorBox)
			{
				Log.Warn($"VigorBox_Use uid={player.Uid}: row {tb} (template={value.TemplateId}) is not a " + "vigor_box; nothing granted.");
				return Task.FromResult(CallGSResult.Error("error.BadParam"));
			}
			list.Add((value, info.Param1));
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		uint num = 0u;
		foreach (var (baseGameItemInfo, num2) in list)
		{
			baseGameItemInfo.ItemCount--;
			ntfSyncPlayer.Items.Add(ItemTemplateInfo.ToSyncProto(baseGameItemInfo));
			num += num2;
			Log.Info($"VigorBox_Use uid={player.Uid} used row {baseGameItemInfo.UniqueId} (template={baseGameItemInfo.TemplateId}) for {num2} vigor, row left {baseGameItemInfo.ItemCount}.");
		}
		PortVigor.Add(player, num, ntfSyncPlayer);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(inventoryData);
		JsonObject jsonObject = new JsonObject
		{
			["tbAward"] = new JsonArray(),
			["nVigor"] = (int)player.Data.Vigor
		};
		Log.Info($"VigorBox_Use uid={player.Uid} used {list.Count} potion(s) for {num} vigor, vigor is now {player.Data.Vigor}.");
		return Task.FromResult(CallGSResult.Ok(jsonObject.ToJsonString(), ntfSyncPlayer));
	}
}
