using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_SelectAffix")]
public class SupporterCard_SelectAffix : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SupporterCardSelectParam req = JsonSerializer.Deserialize<SupporterCardSelectParam>(param);
		GameSupportCardInfo card = ((req == null) ? null : connection.Player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid));
		if (card == null || !SupportAffixStateService.HasAffix(card, 4))
		{
			await SupporterCardAffixShared.SendSelectResponse(connection);
			return;
		}
		if (req.SelectNew)
		{
			SupportAffixStateService.CopyAffix(card, 4, 3);
		}
		SupportAffixStateService.ClearAffix(card, 4);
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { card.ToProto() }
		};
		SupporterCardAffixShared.Save(connection);
		await SupporterCardAffixShared.SendSelectResponse(connection, sync);
	}
}
