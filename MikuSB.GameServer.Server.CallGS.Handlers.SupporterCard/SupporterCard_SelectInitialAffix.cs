using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_SelectInitialAffix")]
public class SupporterCard_SelectInitialAffix : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SupporterCardSelectInitialParam req = JsonSerializer.Deserialize<SupporterCardSelectInitialParam>(param);
		GameSupportCardInfo card = ((req == null) ? null : connection.Player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid));
		bool flag = req == null || card == null;
		bool flag2 = flag;
		if (!flag2)
		{
			int index = req.Index;
			bool flag3 = ((index < 1 || index > 2) ? true : false);
			flag2 = flag3;
		}
		if (flag2 || card.AffixId != req.Index || !SupportAffixStateService.HasAffix(card, 5))
		{
			await SupporterCardAffixShared.SendSelectResponse(connection);
			return;
		}
		if (req.SelectNew)
		{
			SupportAffixStateService.CopyAffix(card, 5, req.Index);
		}
		SupportAffixStateService.ClearAffix(card, 5);
		card.AffixId = 0u;
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { card.ToProto() }
		};
		SupporterCardAffixShared.Save(connection);
		await SupporterCardAffixShared.SendSelectResponse(connection, sync);
	}
}
