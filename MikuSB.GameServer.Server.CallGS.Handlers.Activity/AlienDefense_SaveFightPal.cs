using System.Collections.Generic;
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
/// 2026-11-17（第 389 节）：星球守护「伙伴编队」选择不保存（与助战少女同一根因）。
///
/// 修复前空壳，收到的 tbPalIdx 被丢掉。客户端侧（反汇编 bc_SaveFightPal / bc_GetFightPal）：
///   · SaveFightPal(tb) 只发 {"tbPalIdx":[...]}；
///   · GetFightPal()    = GetBits(me:GetAttribute(GID=143, FightPal=5), 0, 15)
///                       , GetBits(..., 16, 31)  —— 两个 16 位槽位，值为 0 的槽直接丢弃。
/// 也就是伙伴编队打包在一条属性里：低 16 位 = 槽1，高 16 位 = 槽2。
/// </summary>
[CallGSApi("AlienDefense_SaveFightPal")]
public class AlienDefense_SaveFightPal : ICallGSHandler
{
	private const uint Gid = 143u;

	private const uint Sid = 5u;

	private static readonly Logger Log = new Logger("AlienDefense");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<uint> ids = ParseList(param, "tbPalIdx");
		uint slot1 = (ids.Count > 0) ? ids[0] : 0u;
		uint slot2 = (ids.Count > 1) ? ids[1] : 0u;
		uint packed = (slot1 & 0xFFFFu) | ((slot2 & 0xFFFFu) << 16);
		WriteAttr(player, sync, packed);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"AlienDefense fight pal saved: [{string.Join(",", ids)}] packed={packed}");
		await CallGSRouter.SendScript(connection, "AlienDefense_SaveFightPal", "{}", sync);
	}

	private static List<uint> ParseList(string param, string name)
	{
		List<uint> list = new List<uint>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (!doc.RootElement.TryGetProperty(name, out JsonElement node) || node.ValueKind != JsonValueKind.Array)
			{
				return list;
			}
			foreach (JsonElement item in node.EnumerateArray())
			{
				if (item.TryGetUInt32(out uint value))
				{
					list.Add(value);
				}
			}
		}
		catch
		{
		}
		return list;
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
