using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSkin_Change")]
public class GirlSkin_Change : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ChangeSkinParam girlSkinData = JsonSerializer.Deserialize<ChangeSkinParam>(param);
		CharacterInfo cardData = player.CharacterManager.GetCharacterByGUID((uint)girlSkinData.CardId);
		if (cardData != null)
		{
			uint skinId = (cardData.SkinId = (uint)girlSkinData.Id);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			NtfSyncPlayer sync = new NtfSyncPlayer
			{
				Items = { cardData.ToProto() }
			};
			GameSkinInfo skinItem = player.InventoryManager.GetSkinItem(skinId);
			if (skinItem != null)
			{
				sync.Items.Add(skinItem.ToProto());
			}
			var response = new
			{
				nResult = 0,
				sErr = "",
				nSkinId = skinId,
				SkinId = skinId,
				nId = skinId,
				Id = skinId
			};
			string json = JsonSerializer.Serialize(response);
			await CallGSRouter.SendScript(connection, "GirlSkin_Change", json, sync);
		}
	}
}
