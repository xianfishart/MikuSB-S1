using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlWeaponSkin_Change")]
public class GirlWeaponSkin_Change : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		GirlWeaponSkinParam req = JsonSerializer.Deserialize<GirlWeaponSkinParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "GirlWeaponSkin_Change", "{\"err\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		CharacterInfo cardData = player.CharacterManager.GetCharacterByGUID(req.CardId);
		if (cardData == null)
		{
			return;
		}
		if (req.SkinId != 0)
		{
			BaseGameItemInfo skinData = player.InventoryManager.GetNormalItem(req.SkinId);
			if (skinData == null)
			{
				await CallGSRouter.SendScript(connection, "GirlWeaponSkin_Change", "{\"err\":\"error.BadParam\"}");
				return;
			}
		}
		cardData.WeaponSkinId = req.SkinId;
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { cardData.ToProto() }
		};
		await CallGSRouter.SendScript(connection, "GirlWeaponSkin_Change", "null", sync);
	}
}
