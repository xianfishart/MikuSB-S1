using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher）20 条成就一次性写满进度
/// （2026-11-13 新增，修复记录第 269 节）。
///
/// 只写进度，**不发奖**：奖励仍旧在推币成就页按「领取」走 ArcadeGameGetAchReward，
/// 与冰趣蜜弹的 watergunach 一个路子。
///
/// 写的位置是 Gid 7 / Sid = 成就ID，Val 的 bit1..31 为进度、bit0 为已领标记
/// （客户端 Achievement.GetProgres 读 bit1..31、IsReceive 读 bit0，已反汇编确认）。
/// 目标值取 Resources/achievement/activity_quest.json 的 CheckValue。
///
/// 用途：388~396 里有两类（391 图鉴全解锁、395 一局 5 位少女秘宝）依赖客户端图鉴/摆位
/// 数据，服务端暂时判不出来，留这条命令兜底；其余成就会在结算时自动涨。
/// </summary>
[CommandInfo("arcadeach", "Fill all CoinPusher achievement progress (24001~24020).", "Usage: /arcadeach  (append @uid; rewards still need to be claimed in game)", new string[]
{
	"coinach",
	"arcadeachievement",
	"coinpusherach"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandArcadeAch : ICommands
{
	private const uint QuestGid = 7u;

	private const int AchIdStart = 24001;

	private const int AchIdEnd = 24020;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		Dictionary<int, int> checks = LoadChecks();
		if (checks.Count == 0)
		{
			await arg.SendMsg("arcadeach: Resources/achievement/activity_quest.json 缺失或没有 24001~24020，无法取目标值。");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int changed = 0;
		foreach (KeyValuePair<int, int> kv in checks.OrderBy((KeyValuePair<int, int> x) => x.Key))
		{
			PlayerAttr? attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == QuestGid && x.Sid == (uint)kv.Key);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = QuestGid,
					Sid = (uint)kv.Key,
					Val = 0u
				};
				player.Data.Attrs.Add(attr);
			}
			uint target = (uint)Math.Max(1, kv.Value) << 1;
			if ((attr.Val & 0xFFFFFFFEu) == target)
			{
				continue;
			}
			attr.Val = (attr.Val & 1u) | target;
			sync.Custom[player.ToPackedAttrKey(QuestGid, (uint)kv.Key)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(QuestGid, (uint)kv.Key)] = attr.Val;
			changed++;
		}
		if (changed == 0)
		{
			await arg.SendMsg($"arcadeach: 推币大赢家 20 条成就进度已经是满的，无需改动。uid {player.Uid}。");
			return;
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"arcadeach: 推币大赢家成就进度已写满 {changed} 条（共 {checks.Count} 条，24001~24020），已领取的不会重复发。重开推币成就页即可一键领取。uid {player.Uid}。");
	}

	/// <summary>24001~24020 的 ID → CheckValue（进度目标）。</summary>
	private static Dictionary<int, int> LoadChecks()
	{
		Dictionary<int, int> map = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path) || !(JsonNode.Parse(File.ReadAllText(path)) is JsonArray list))
			{
				return map;
			}
			foreach (JsonNode node in list)
			{
				if (!(node is JsonObject obj))
				{
					continue;
				}
				int id = HouseJson.NumField(obj, "ID");
				if (id < AchIdStart || id > AchIdEnd)
				{
					continue;
				}
				map[id] = Math.Max(1, HouseJson.NumField(obj, "CheckValue"));
			}
		}
		catch
		{
		}
		return map;
	}
}
