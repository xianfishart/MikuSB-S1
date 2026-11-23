using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_ChangeAccountShowCard")]
public class PlayerSetting_ChangeAccountShowCard : ICallGSHandler
{
	private static readonly int[] CardSlots = new int[5] { 1, 2, 3, 7, 8 };

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ChangeAccountShowParam req = JsonSerializer.Deserialize<ChangeAccountShowParam>(param);
		if (req == null || req.Index < 1 || req.Index > CardSlots.Length)
		{
			return;
		}
		if (req.ItemId < 0)
		{
			player.SetShowItem(CardSlots[req.Index - 1], 0uL);
		}
		else
		{
			CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.ItemId);
			if (card == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeAccountShowCard", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(CardSlots[req.Index - 1], card.Guid);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.ShowItems.AddRange(player.Data.ShowItems);
		await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeAccountShowCard", "null", sync);
	}
}
