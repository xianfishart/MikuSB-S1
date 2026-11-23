using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 关卡内角色/武器/技能选择。
/// 客户端把选择压进属性组 158、sid = 关卡 ID 的位域：
///   bit 0..13  ROLE   (14 位)
///   bit 14..17 WEAPON (4 位)
///   bit 18..21 SKILL  (4 位)
///   bit 22..25 FIRE   (4 位)
/// 读侧是 GirlRushLevelCfg:GetData() -> me:GetAttribute(158, nId)。
/// 以前这里只回 {}，属性永远是 0，进关卡后角色 ID = 0 -> GirlPath invalid
/// -> 关卡里没有生成玩家 -> GamePlayer 为 nil 报错、关卡卡死。
/// </summary>
[CallGSApi("GirlRush_SaveData")]
public class GirlRush_SaveData : ICallGSHandler
{
	internal const uint AttrGid = 158u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["nLeveId"]);
		uint roleId = ReadUint(request["nRoleId"]);
		uint weaponIdx = ReadUint(request["nWeaponIdx"]);
		uint skillIdx = ReadUint(request["nSkillIdx"]);
		uint fireModel = ReadUint(request["nFireModel"]);
		try
		{
			System.IO.File.AppendAllText(@"Config/Logs/girlrush_dbg.txt",
				$"[SaveData] raw={param} | level={levelId} role={roleId} wep={weaponIdx} skill={skillIdx} fire={fireModel} packed={Pack(roleId, weaponIdx, skillIdx, fireModel)}\n");
		}
		catch { }
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (levelId != 0)
		{
			uint packed = Pack(roleId, weaponIdx, skillIdx, fireModel);
			PlayerAttr attr = player.Attributes.GetOrCreate(AttrGid, levelId);
			attr.Val = packed;
			player.Attributes.SyncTo(sync, attr);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "GirlRush_SaveData", "{}", sync);
	}

	internal static uint Pack(uint roleId, uint weaponIdx, uint skillIdx, uint fireModel)
	{
		return (roleId & 0x3FFFu)
			| ((weaponIdx & 0xFu) << 14)
			| ((skillIdx & 0xFu) << 18)
			| ((fireModel & 0xFu) << 22);
	}

	internal static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue value && value.TryGetValue<uint>(out uint number))
		{
			return number;
		}
		return uint.TryParse(node?.ToString(), out uint parsed) ? parsed : 0u;
	}
}
