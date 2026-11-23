using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSkinParts_Update")]
public class GirlSkinParts_Update : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		GirlSkinPartsUpdateParam req = JsonSerializer.Deserialize<GirlSkinPartsUpdateParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "GirlSkinParts_Update", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		List<GameSkinInfo> data = new List<GameSkinInfo>();
		foreach (uint partId in req.PartsId)
		{
			BaseGameItemInfo partData = player.InventoryManager.GetNormalItem(partId);
			if (partData == null)
			{
				continue;
			}
			CardSkinPartsExcel partExcel = GameData.CardSkinPartsData.Values.FirstOrDefault((CardSkinPartsExcel x) => x.TemplateId == partData.TemplateId);
			if (partExcel != null)
			{
				GameSkinInfo skinData = player.InventoryManager.GetSkinItem(req.SkinId);
				if (skinData != null)
				{
					skinData.PartSlots[partExcel.Detail] = partData.UniqueId;
					data.Add(skinData);
				}
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { data.Select((GameSkinInfo x) => x.ToProto()) }
		};
		await CallGSRouter.SendScript(connection, "GirlSkinParts_Update", "{}", sync);
	}
}
