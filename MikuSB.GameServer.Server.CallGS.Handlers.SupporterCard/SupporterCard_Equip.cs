using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_Equip")]
public class SupporterCard_Equip : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SupporterCardEquipParam req = JsonSerializer.Deserialize<SupporterCardEquipParam>(param);
		if (req == null || req.CardId == 0 || req.SupportCardUid == 0)
		{
			await CallGSRouter.SendScript(connection, "Logistics_Equip", "{}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "Logistics_Equip", "{}");
			return;
		}
		uint slot = GetTeamIndex(teamIndex: card.SupportTeamIndex, slot: (uint)req.EqSlot);
		if (!req.Force && req.CurrentEquippedUid != 0 && card.SupportSlots.TryGetValue(slot, out var existing) && existing != 0)
		{
			await CallGSRouter.SendScript(connection, "Logistics_Confirm", "{}");
			return;
		}
		card.SupportSlots[slot] = (uint)req.SupportCardUid;
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { card.ToProto() }
		};
		string responseApi = (string.IsNullOrEmpty(req.Model) ? "Logistics_Change" : "Logistics_Equip");
		await CallGSRouter.SendScript(connection, responseApi, "{}", sync);
	}

	private uint GetTeamIndex(uint slot, uint teamIndex)
	{
		return teamIndex switch
		{
			1u => slot, 
			2u => slot + 7, 
			3u => slot + 10, 
			_ => slot, 
		};
	}
}
