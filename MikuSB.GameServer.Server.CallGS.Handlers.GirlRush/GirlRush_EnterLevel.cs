using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 进关卡。除了回 nSeed，还要把属性组 158 里存的「角色/武器/技能选择」同步给客户端，
/// 客户端在地图加载后靠 GirlRushLevelCfg:GetData() 读这份位域来生成玩家；
/// 同步缺失时读到 0 -> GirlPath invalid -> 关卡里没有玩家、GamePlayer 为 nil -> 卡死。
/// 属性不存在时按关卡表的 ReferChar 兜底写一份默认值，保证任何入口进来都能生成玩家。
///
/// 调试开关：Config/girlrush_override.json 可按关卡强制覆盖编队，例如
///   { "8656": { "role": 1, "weapon": 1, "skill": 0, "fire": 1 } }
/// 文件不存在或该关卡没配则走正常流程。
/// </summary>
[CallGSApi("GirlRush_EnterLevel")]
public class GirlRush_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = GirlRush_SaveData.ReadUint(request["nID"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (levelId != 0)
		{
			PlayerAttr? attr = player.Attributes.Get(GirlRush_SaveData.AttrGid, levelId);
			if (attr == null || attr.Val == 0)
			{
				uint packed = GirlRush_SaveData.Pack(GirlRushStore.DefaultRoleId(levelId), 0u, 0u, 0u);
				attr = player.Attributes.GetOrCreate(GirlRush_SaveData.AttrGid, levelId);
				attr.Val = packed;
				DatabaseHelper.SaveDatabaseType(player.Data);
			}

			uint overridden = ApplyOverride(levelId, attr.Val, out bool didOverride);
			if (didOverride)
			{
				attr.Val = overridden;
				DatabaseHelper.SaveDatabaseType(player.Data);
			}

			player.Attributes.SyncTo(sync, attr);
			GirlRushStore.MarkEnter(player.Uid, levelId);
			try
			{
				File.AppendAllText(@"Config/Logs/girlrush_dbg.txt",
					$"[EnterLevel] raw={param} | level={levelId} attrVal={attr.Val} override={didOverride} defaultRole={GirlRushStore.DefaultRoleId(levelId)}\n");
			}
			catch { }
		}
		await CallGSRouter.SendScript(connection, "GirlRush_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}", sync);
		_ = DlcClientPatches.Push(connection, "girlrush-enter");
	}

	private static uint ApplyOverride(uint levelId, uint current, out bool applied)
	{
		applied = false;
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "girlrush_override.json");
			if (!File.Exists(path))
			{
				return current;
			}
			JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
			if (root is not JsonObject obj)
			{
				return current;
			}
			if (obj[levelId.ToString()] is not JsonObject row)
			{
				return current;
			}
			uint role = GirlRush_SaveData.ReadUint(row["role"]);
			uint weapon = GirlRush_SaveData.ReadUint(row["weapon"]);
			uint skill = GirlRush_SaveData.ReadUint(row["skill"]);
			uint fire = GirlRush_SaveData.ReadUint(row["fire"]);
			applied = true;
			return GirlRush_SaveData.Pack(role, weapon, skill, fire);
		}
		catch
		{
			return current;
		}
	}
}
