using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_UnlockTalent")]
public class DreamCard_UnlockTalent : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int id = 0;
		try
		{
			id = (JsonNode.Parse(param)?["nID"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (id <= 0)
		{
			await CallGSRouter.SendScript(connection, "DreamCard_UnlockTalent", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 154 && x.Sid == (uint)id);
		if (attr != null && attr.Val != 0)
		{
			await CallGSRouter.SendScript(connection, "DreamCard_UnlockTalent", "{}");
			return;
		}
		// 花费统一从 talent.json 缓存取，保证「扣多少」与重置时「退多少」完全同一份数据。
		int cost = DreamCardTalentTable.GetCost(id);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (cost > 0)
		{
			int moneySid = 105;
			PlayerAttr money = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == (uint)moneySid);
			if (money == null || money.Val < cost)
			{
				await CallGSRouter.SendScript(connection, "DreamCard_UnlockTalent", "{\"sErr\":\"ui.TxtLucky.talentNum\"}");
				return;
			}
			money.Val -= (uint)cost;
			foreach (var (key, value) in player.BuildMoneySync())
			{
				sync.Money[key] = value;
			}
			sync.Custom[player.ToPackedAttrKey(1u, (uint)moneySid)] = money.Val;
			sync.Custom[player.ToShiftedAttrKey(1u, (uint)moneySid)] = money.Val;
		}
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 154u,
				Sid = (uint)id,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = 1u;
		sync.Custom[player.ToPackedAttrKey(154u, (uint)id)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(154u, (uint)id)] = attr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "DreamCard_UnlockTalent", "{}", sync);
	}
}
