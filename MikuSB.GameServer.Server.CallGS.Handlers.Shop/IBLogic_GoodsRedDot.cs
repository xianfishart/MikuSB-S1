using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("IBLogic_GoodsRedDot")]
public class IBLogic_GoodsRedDot : ICallGSHandler
{
	private const uint RedGroupId = 113u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		IbGoodsRedDotParam req = JsonSerializer.Deserialize<IbGoodsRedDotParam>(param);
		if (req?.GoodsIds == null || req.GoodsIds.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "IBLogic_GoodsRedDot", "null");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool changed = false;
		foreach (uint goodsId in req.GoodsIds.Where((uint x) => x != 0).Distinct())
		{
			PlayerAttr attr = GetOrCreateAttr(player, 113u, goodsId);
			if (attr.Val == 0)
			{
				attr.Val = 1u;
				SyncAttr(player, sync, attr);
				changed = true;
			}
		}
		if (changed)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "IBLogic_GoodsRedDot", "null", sync);
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint gid, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
