using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_ResetTalent")]
public class DreamCard_ResetTalent : ICallGSHandler
{
	private const uint TalentGid = 154u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonNode req = null;
		try
		{
			req = JsonNode.Parse(param);
		}
		catch
		{
		}
		int id = (req?["nID"]?.GetValue<int>()).GetValueOrDefault();
		NtfSyncPlayer sync = new NtfSyncPlayer();

		// 需要清零的天赋：nID 本身 + 依赖它的所有下级（talent.json 的 Condition 链）。
		// nID <= 0 视为「整棵树重置」，把 154 组里所有已解锁的都退掉。
		List<uint> targets = new List<uint>();
		if (id > 0)
		{
			CollectWithChildren((uint)id, targets);
		}
		else
		{
			foreach (PlayerAttr attr in player.Data.Attrs)
			{
				if (attr.Gid == TalentGid && attr.Val != 0)
				{
					targets.Add(attr.Sid);
				}
			}
		}

		int refund = 0;
		foreach (uint sid in targets)
		{
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == TalentGid && x.Sid == sid);
			if (attr == null || attr.Val == 0)
			{
				// 没解锁过：不退还，也不重复发同步。
				continue;
			}
			refund += DreamCardTalentTable.GetCost((int)sid);
			attr.Val = 0u;
			sync.Custom[player.ToPackedAttrKey(TalentGid, sid)] = 0u;
			sync.Custom[player.ToShiftedAttrKey(TalentGid, sid)] = 0u;
		}

		// 退还解锁时扣掉的潜质点（货币 52 → 属性 1/105）。
		DreamCardTalentTable.Refund(player, refund, sync);

		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "DreamCard_ResetTalent", "{}", sync);
	}

	private static void CollectWithChildren(uint talentId, List<uint> result)
	{
		if (result.Contains(talentId))
		{
			return;
		}
		result.Add(talentId);
		foreach (int child in DreamCardTalentTable.GetChildren((int)talentId))
		{
			CollectWithChildren((uint)child, result);
		}
	}
}
