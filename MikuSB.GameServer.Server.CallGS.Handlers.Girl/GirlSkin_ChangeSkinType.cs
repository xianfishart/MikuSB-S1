using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSkin_ChangeSkinType")]
public class GirlSkin_ChangeSkinType : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		ChangeSkinTypeParam req = JsonSerializer.Deserialize<ChangeSkinTypeParam>(param);
		uint skinType = ClampClientSkinType(req?.Type ?? 0);
		JsonObject response = new JsonObject
		{
			["nType"] = skinType,
			["nSkinId"] = req?.SkinId
		};
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString());
			return;
		}
		PlayerInstance player = connection.Player;
		GameSkinInfo skinData = GetOrCreateSkinItem(player, req.SkinId);
		if (skinData != null)
		{
			skinData.SkinType = skinType;
		}
		InventoryData inventoryData = player.InventoryManager.InventoryData;
		if (inventoryData.SkinTypesBySkinId == null)
		{
			inventoryData.SkinTypesBySkinId = new Dictionary<uint, uint>();
		}
		player.InventoryManager.InventoryData.SkinTypesBySkinId[req.SkinId] = skinType;
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		if (skinData == null)
		{
			await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString());
			return;
		}
		await CallGSRouter.SendScript(extra: new NtfSyncPlayer
		{
			Items = { skinData.ToProto() }
		}, connection: connection, api: "GirlSkin_ChangeSkinType", arg: response.ToJsonString());
	}

	internal static uint ClampClientSkinType(uint skinType)
	{
		return Math.Min(skinType, 1u);
	}

	internal static GameSkinInfo? GetOrCreateSkinItem(PlayerInstance player, uint skinId)
	{
		InventoryData inventoryData = player.InventoryManager.InventoryData;
		if (inventoryData.Skins.TryGetValue(skinId, out GameSkinInfo value))
		{
			return value;
		}
		if (!GameData.CardSkinData.TryGetValue(skinId, out CardSkinExcel value2))
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl(value2.Genre, value2.Detail, value2.Particular, value2.Level);
		value = player.InventoryManager.GetSkinItemByTemplateId(templateId);
		if (value != null)
		{
			inventoryData.Skins.Remove(value.UniqueId);
			value.UniqueId = skinId;
		}
		else
		{
			value = new GameSkinInfo
			{
				UniqueId = skinId,
				TemplateId = templateId,
				ItemType = ItemTypeEnum.TYPE_CARD_SKIN,
				ItemCount = 1u
			};
		}
		inventoryData.Skins[skinId] = value;
		return value;
	}
}
