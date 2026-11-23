using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 冰趣蜜弹（ThrowGame / 基地投掷小游戏）等级设置
/// （2026-11-13 新增，修复记录第 267 节）。
/// 网页面板「一键满级冰趣蜜弹」按钮调 <c>/throwlevel @uid</c>，不带参数即满级。
///
/// 等级口径（ThrowGameSettlement 已实现，客户端 UMG.House.HouseThrow 同源）：
///   101/17003 = 当前等级
///   101/17004 = 当前等级内的经验（升一级扣一次 Exp，扣到 0 停）
///   101/17002 = 连战最高积分（不动）
///   101/17020+等级 = 该级奖励已领标记（不动，奖励仍走界面正常领取）
/// 满级取 Resources/house/Throw/ThrowAward.json 的最大 Level（当前 15）。
/// </summary>
[CommandInfo("throwlevel", "Set 冰趣蜜弹 (ThrowGame) level; no arg = max level.", "Usage: /throwlevel [level]  (append @uid; no value = max)", new string[]
{
	"throwmax",
	"honeypop",
	"throwmaxlevel"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandThrowLevel : ICommands
{
	internal const uint LevelSid = 17003u;

	internal const uint ExpSid = 17004u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int max = MaxLevel();
		if (max <= 0)
		{
			await arg.SendMsg("throwlevel: Resources/house/Throw/ThrowAward.json 缺失或为空，取不到满级等级。");
			return;
		}
		int level = arg.GetInt(0);
		if (level <= 0 || level > max)
		{
			level = max;
		}
		int oldLevel = Math.Max(1, (int)HouseAttr.Read(player, LevelSid));
		int oldExp = Math.Max(0, (int)HouseAttr.Read(player, ExpSid));
		if (oldLevel == level && oldExp == 0)
		{
			await arg.SendMsg($"throwlevel: 冰趣蜜弹已是 Lv{oldLevel}/{max}（级内经验 0），无需改动。uid {player.Uid}。");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await HouseAttr.SetAsync(arg.Target, LevelSid, (uint)level, sync);
		await HouseAttr.SetAsync(arg.Target, ExpSid, 0u, sync);
		await player.SendPacket(1004, sync);
		// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 watergun / money 的做法）。
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		// 属性改在内存里，必须显式落库，否则服务器重启会退回旧等级。
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"throwlevel: 冰趣蜜弹 Lv{oldLevel}(exp {oldExp}) -> Lv{level}(exp 0)，满级 Lv{max}。uid {player.Uid}。重开冰趣蜜弹界面即可生效；等级奖励仍走界面正常领取（1~{level} 级都能领）。");
	}

	/// <summary>ThrowAward.json 里最大的 Level。</summary>
	internal static int MaxLevel()
	{
		int max = 0;
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "Throw", "ThrowAward.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray list)
			{
				foreach (JsonNode item in list)
				{
					int level = HouseJson.NumField(item as JsonObject, "Level");
					if (level > max)
					{
						max = level;
					}
				}
			}
		}
		catch
		{
		}
		return max;
	}
}
