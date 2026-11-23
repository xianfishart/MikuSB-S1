using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_ReceiveFixedItem")]
public class SupporterCard_ReceiveFixedItem : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (!GameData.SupportFixedData.TryGetValue(1, out SupportFixedExcel fixedCfg) || fixedCfg.Item.Count < 5 || fixedCfg.Num <= 0)
		{
			await CallGSRouter.SendScript(connection, "SupporterCard_ReceiveFixedItem", "{}");
			return;
		}
		PlayerAttr attr = SupporterCardAffixShared.GetOrCreateAttr(player.Data, 150u, 1u);
		uint claimCount = attr.Val / (uint)fixedCfg.Num;
		if (claimCount == 0)
		{
			await CallGSRouter.SendScript(connection, "SupporterCard_ReceiveFixedItem", "{}");
			return;
		}
		attr.Val %= (uint)fixedCfg.Num;
		SuppliesExcel rewardItem = CollectionExtensions.GetValueOrDefault(key: (uint)GameResourceTemplateId.FromGdpl(fixedCfg.Item), dictionary: GameData.SuppliesData);
		if (rewardItem == null)
		{
			await CallGSRouter.SendScript(connection, "SupporterCard_ReceiveFixedItem", "{}");
			return;
		}
		BaseGameItemInfo granted = await player.InventoryManager.AddSuppliesItem(rewardItem, claimCount * fixedCfg.Item[4], sendPacket: false);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (granted != null)
		{
			sync.Items.Add(granted.ToProto());
		}
		SupporterCardAffixShared.SetAttr(connection, sync, 150u, 1u, attr.Val);
		SupporterCardAffixShared.Save(connection);
		JsonObject jsonObject = new JsonObject();
		InlineArray5<JsonNode> buffer = default;
		buffer[0] = (int)fixedCfg.Item[0];
		buffer[1] = (int)fixedCfg.Item[1];
		buffer[2] = (int)fixedCfg.Item[2];
		buffer[3] = (int)fixedCfg.Item[3];
		buffer[4] = (int)(claimCount * fixedCfg.Item[4]);
		jsonObject["tbRewards"] = new JsonArray(buffer);
		string arg = jsonObject.ToJsonString();
		await CallGSRouter.SendScript(connection, "SupporterCard_ReceiveFixedItem", arg, sync);
	}
}
