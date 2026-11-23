using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

/// <summary>
/// 后勤基础特长重修（特长1 / 特长2）。
///
/// 第 302 节：官方规则是「特长1、2 只能重修其中 1 条」，但本服不做这条限制——
/// 两个词条都可以反复重修。服务端这里本来就按 Index 1/2 对称处理，
/// 不需要额外放宽；本次只补日志，方便实测两个词条都能走通。
/// </summary>
[CallGSApi("SupporterCard_ResetInitialAffix")]
public class SupporterCard_ResetInitialAffix : ICallGSHandler
{
	private static readonly Logger Log = new Logger("SupportRebuild");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await Reset(connection, param, fixedMode: false);
	}

	internal static async Task<bool> Reset(Connection connection, string param, bool fixedMode)
	{
		SupporterCardResetInitialParam req = JsonSerializer.Deserialize<SupporterCardResetInitialParam>(param);
		GameSupportCardInfo card = ((req == null) ? null : connection.Player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid));
		SupportCardExcel excel = ((card == null) ? null : SupporterCardAffixShared.GetExcel(card));
		bool flag = req == null || card == null || excel == null;
		bool flag2 = flag;
		if (!flag2)
		{
			int index = req.Index;
			bool flag3 = ((index < 1 || index > 2) ? true : false);
			flag2 = flag3;
		}
		if (flag2 || excel.AffixPool.Count < req.Index)
		{
			Log.Warn($"reject(notfound/param) uid={connection.Player.Uid} req={param}");
			await SupporterCardAffixShared.SendResetResponse(connection);
			return false;
		}
		Log.Info($"enter uid={connection.Player.Uid} card={card.UniqueId} index={req.Index} fixedMode={fixedMode} fixedId={req.FixedId} affixs=[{string.Join(",", card.Affixs)}] affixId={card.AffixId}");
		IReadOnlyList<IReadOnlyList<uint>> readOnlyList;
		if (!fixedMode)
		{
			readOnlyList = excel.InitialAffixCost;
		}
		else
		{
			IReadOnlyList<IReadOnlyList<uint>> readOnlyList2 = new IReadOnlyList<uint>[1] { excel.FixedAffixCost };
			readOnlyList = readOnlyList2;
		}
		IReadOnlyList<IReadOnlyList<uint>> costs = readOnlyList;
		if (!costs.Any() || !SupporterCardAffixShared.HasEnoughItems(connection, costs))
		{
			Log.Info($"reject(cost) uid={connection.Player.Uid} card={card.UniqueId} index={req.Index} rows={costs.Count}");
			await SupporterCardAffixShared.SendResetResponse(connection);
			return false;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(SupporterCardAffixShared.ConsumeCostItems(connection, costs));
		var (oldId, oldTier) = SupportAffixStateService.GetAffix(card, req.Index);
		uint affixId;
		uint tier;
		if (!fixedMode || req.FixedId == 0)
		{
			(affixId, tier) = SupportAffixService.GenerateRandomAffix(excludedAffixIds: SupporterCardAffixShared.GetActiveAffixIds(card, req.Index), poolId: excel.AffixPool[req.Index - 1]);
		}
		else
		{
			affixId = req.FixedId;
			tier = SupportAffixService.GenerateTier(affixId);
		}
		SupportAffixStateService.SetAffix(card, 5, affixId, tier);
		card.AffixId = (uint)req.Index;
		PlayerAttr attr = SupporterCardAffixShared.GetOrCreateAttr(connection.Player.Data, 150u, 1u);
		attr.Val++;
		SupporterCardAffixShared.SetAttr(connection, sync, 150u, 1u, attr.Val);
		sync.Items.Add(card.ToProto());
		SupporterCardAffixShared.Save(connection);
		Log.Info($"ok uid={connection.Player.Uid} card={card.UniqueId} index={req.Index} {oldId}/{oldTier} -> pending {affixId}/{tier} resetTimes={attr.Val}");
		await SupporterCardAffixShared.SendResetResponse(connection, sync);
		return true;
	}
}
