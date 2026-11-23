using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 2026-11-17（第 389 节）：星球守护「助战少女」选择不保存。
///
/// 修复前是空壳（直接 Task.CompletedTask），收到的 nGirlId 被丢掉。
/// 客户端侧（反汇编 bc_SaveFightGirl / bc_GetFightGirl）：
///   · SaveFightGirl(id) 只把 {"nGirlId":id} 发给服务端，不写任何本地状态；
///   · GetFightGirl()    = me:GetAttribute(AlienDefense.GID=143, AlienDefense.FightGirl=7)。
/// 于是勾选后返回界面重新读属性恒为 0 → 勾选被清空；随后客户端还会把读到的 0
/// 再回写一次（日志里 3 后面紧跟 0 就是这条链），状态彻底丢失。
///
/// 口径：把 nGirlId 原样写进 (143,7)，0 表示未选择（允许清空）。
/// 落库 + 走 NtfSyncPlayer 同步，客户端 GetAttribute 立刻能读到新值。
/// </summary>
[CallGSApi("AlienDefense_SaveFightGirl")]
public class AlienDefense_SaveFightGirl : ICallGSHandler
{
	private const uint Gid = 143u;

	private const uint Sid = 7u;

	private static readonly Logger Log = new Logger("AlienDefense");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint girlId = ParseUInt(param, "nGirlId");
		WriteAttr(player, sync, girlId);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"AlienDefense fight girl saved: nGirlId={girlId}");
		await CallGSRouter.SendScript(connection, "AlienDefense_SaveFightGirl", "{}", sync);
	}

	private static uint ParseUInt(string param, string name)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty(name, out JsonElement node) && node.TryGetUInt32(out uint value))
			{
				return value;
			}
		}
		catch
		{
		}
		return 0u;
	}

	private static void WriteAttr(PlayerInstance player, NtfSyncPlayer sync, uint value)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == Sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = Gid,
				Sid = Sid,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = value;
		sync.Custom[player.ToPackedAttrKey(Gid, Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(Gid, Sid)] = attr.Val;
	}
}
