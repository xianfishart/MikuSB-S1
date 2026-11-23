using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 心动水弹：客户端上报任务完成（2026-11-12 新增，修复记录第 261 节）。
/// 客户端报文：{"FuncName":"CompleteAchievement","AchType":264}，只带类型，不带具体 ID。
/// 数据来源：Resources/achievement/activity_quest.json（GroupID_Quest=7，水弹条目 20101~20120，
/// Type 字段即客户端的 AchType）。旧版本服务端没有这个处理器，所有上报被丢弃，
/// 导致 Gid=7 的属性永远没有进度，任务奖励一领就是空。
/// 规则：把该 AchType 下所有 20100~20199 的条目写满进度（Val = (Val &amp; 1) | CheckValue &lt;&lt; 1），
/// 保留最低位的「已领」标记不动。
/// </summary>
[HouseFunc("CompleteAchievement")]
public class CompleteAchievement : IHouseFuncHandler
{
	private const uint QuestGid = 7u;

	private const int MinId = 20100;

	private const int MaxId = 20199;

	private static Dictionary<int, List<KeyValuePair<int, int>>>? _byType;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int achType = HouseJson.NumField(root, "AchType");
		if (achType <= 0)
		{
			achType = HouseJson.NumField(root, "nAchType");
		}
		PlayerInstance? player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray ids = new JsonArray();
		if (player != null && achType > 0 && Load().TryGetValue(achType, out List<KeyValuePair<int, int>>? rows))
		{
			foreach (KeyValuePair<int, int> row in rows)
			{
				int id = row.Key;
				uint check = (uint)Math.Max(1, row.Value);
				PlayerAttr? attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == QuestGid && x.Sid == (uint)id);
				if (attr == null)
				{
					attr = new PlayerAttr
					{
						Gid = QuestGid,
						Sid = (uint)id,
						Val = 0u
					};
					player.Data.Attrs.Add(attr);
				}
				attr.Val = (attr.Val & 1) | (check << 1);
				sync.Custom[player.ToPackedAttrKey(QuestGid, (uint)id)] = attr.Val;
				sync.Custom[player.ToShiftedAttrKey(QuestGid, (uint)id)] = attr.Val;
				ids.Add(id);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "CompleteAchievement",
			["AchType"] = achType,
			["tbAchId"] = ids
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	private static Dictionary<int, List<KeyValuePair<int, int>>> Load()
	{
		if (_byType != null)
		{
			return _byType;
		}
		Dictionary<int, List<KeyValuePair<int, int>>> map = new Dictionary<int, List<KeyValuePair<int, int>>>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (File.Exists(path))
			{
				JsonArray? rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (rows != null)
				{
					foreach (JsonObject obj in rows.OfType<JsonObject>())
					{
						int id = HouseJson.NumField(obj, "ID");
						int type = HouseJson.NumField(obj, "Type");
						if (id < MinId || id > MaxId || type <= 0)
						{
							continue;
						}
						int check = Math.Max(1, HouseJson.NumField(obj, "CheckValue"));
						if (!map.TryGetValue(type, out List<KeyValuePair<int, int>>? list))
						{
							list = new List<KeyValuePair<int, int>>();
							map[type] = list;
						}
						list.Add(new KeyValuePair<int, int>(id, check));
					}
				}
			}
		}
		catch
		{
		}
		_byType = map;
		return _byType;
	}
}
