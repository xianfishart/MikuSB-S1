using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_UpdateShop")]
public class ShopLogic_UpdateShop : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int shopId = 0;
		try
		{
			shopId = (JsonNode.Parse(param)?["nShopId"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (shopId <= 0)
		{
			await CallGSRouter.SendScript(connection, "ShopLogic_GetGoodsList", "{\"shopId\":0,\"goodsList\":[]}");
			return;
		}
		PlayerInstance player = connection.Player;
		(int Columns, int RefreshLimits, int Version, int ListVersion) info = ShopLogic_GetGoodsList.LoadShopInfo(shopId);
		JsonObject oldData = ShopLogic_GetGoodsList.ReadShopData(player, shopId);
		int refreshNum = ShopLogic_GetGoodsList.AsInt(oldData["refreshnum"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray goods;
		JsonObject shopData;
		if (info.RefreshLimits > 0 && refreshNum >= info.RefreshLimits)
		{
			// 第 354 节：存档里的 tbgoods 现在只存 goodsid+discountindex（瘦身让客户端
			// GetOnLineGoodsInfo 的重复 decode 变快），回给客户端前要从配置表重建整行。
			goods = ShopLogic_GetGoodsList.RebuildFromStock(shopId, (oldData["tbgoods"] as JsonArray) ?? new JsonArray());
			shopData = oldData;
		}
		else
		{
			shopData = ShopLogic_GetGoodsList.GetOrCreateStock(player, shopId, forceNew: true, sync, out goods);
			refreshNum = ShopLogic_GetGoodsList.AsInt(shopData["refreshnum"]);
		}
		sync.CustomStr[player.ToShiftedAttrKey(1u, (uint)shopId)] = shopData.ToJsonString();
		sync.CustomStr[player.ToPackedAttrKey(1u, (uint)shopId)] = shopData.ToJsonString();
		JsonObject rsp = new JsonObject
		{
			["nShopId"] = shopId,
			["shopId"] = shopId,
			["goodsList"] = goods.DeepClone(),
			["refreshnum"] = refreshNum
		};
		await CallGSRouter.SendScript(connection, "ShopLogic_GetGoodsList", rsp.ToJsonString(), sync);
	}
}
