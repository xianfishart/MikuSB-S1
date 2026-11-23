using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_ResetAffix")]
public class SupporterCard_ResetAffix : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SupporterCardIdParam req = JsonSerializer.Deserialize<SupporterCardIdParam>(param);
		GameSupportCardInfo card = ((req == null) ? null : connection.Player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid));
		SupportCardExcel excel = ((card == null) ? null : SupporterCardAffixShared.GetExcel(card));
		if (card == null || excel == null || excel.AffixCost.Count < 5 || !SupportAffixStateService.HasAffix(card, 3))
		{
			await SupporterCardAffixShared.SendResetResponse(connection);
			return;
		}
		IReadOnlyList<uint>[] costs = new IReadOnlyList<uint>[1] { excel.AffixCost };
		if (!SupporterCardAffixShared.HasEnoughItems(connection, costs))
		{
			await SupporterCardAffixShared.SendResetResponse(connection);
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(SupporterCardAffixShared.ConsumeCostItems(connection, costs));
		var (affixId, tier) = SupportAffixService.GenerateRandomAffix(excludedAffixIds: SupporterCardAffixShared.GetActiveAffixIds(card, 3), poolId: excel.AffixPool[2]);
		SupportAffixStateService.SetAffix(card, 4, affixId, tier);
		sync.Items.Add(card.ToProto());
		SupporterCardAffixShared.Save(connection);
		await SupporterCardAffixShared.SendResetResponse(connection, sync);
	}
}
