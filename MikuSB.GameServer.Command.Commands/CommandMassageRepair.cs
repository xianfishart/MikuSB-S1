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
/// 神经调理（疗愈中心）存档修复 / 一键满级
/// （2026-11-13 新增，修复记录第 272 节）。
///
/// /massagerepair  — 幂等修复：把「疗愈评级」补写进 24+难度 的低 3 位（客户端判定五星奖励就靠这里）、
///                   补发已达成五星的达成奖励、按历史次数补算一次经验。
/// /massagemax [n] — 直接把调理等级写满（默认 15 级），经验清零，并把 15 档等级奖励全部发放。
/// </summary>
[CommandInfo("massagerepair", "Repair massage stars/exp and grant reached 5-star rewards.", "Usage: /massagerepair  (append @uid)", new string[]
{
	"massagefix",
	"massagestar",
	"massagestars"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandMassageRepair : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		string result = await HouseMassageHelper.RepairAsync(arg.Target, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"massagerepair: {result}（uid {player.Uid}）。重开疗愈中心即可看到星级奖励已领、经验条已补。");
	}
}

/// <summary>神经调理一键满级：等级写满、经验清零、15 档等级奖励全部发放（修复记录第 272 节）。</summary>
[CommandInfo("massagemax", "Set massage level to max (15) and grant all level rewards.", "Usage: /massagemax [level=15]  (append @uid)", new string[]
{
	"massagelevelmax",
	"massagelvmax"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandMassageMax : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int target = HouseMassageHelper.MaxLevel;
		int want = arg.GetInt(0);
		if (want >= 1 && want <= HouseMassageHelper.MaxLevel)
		{
			target = want;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		player.Attributes.Set(101u, HouseMassageHelper.LevelSid, (uint)target);
		player.Attributes.SyncTo(sync, 101u, HouseMassageHelper.LevelSid, (uint)target);
		player.Attributes.Set(101u, HouseMassageHelper.ExpSid, 0u);
		player.Attributes.SyncTo(sync, 101u, HouseMassageHelper.ExpSid, 0u);
		// 把 1~target 的等级奖励全部发放并置位（客户端按 10003 的第 level 位判断已领）
		int granted = 0;
		for (int level = 1; level <= target; level++)
		{
			List<List<uint>> items = HouseMassageHelper.LevelRewardItems(level);
			if (items.Count == 0)
			{
				continue;
			}
			uint sid = HouseMassageHelper.LevelRewardStartSid(level);
			int bit = level % 30;
			uint value = player.Attributes.GetValue(101u, sid);
			if (((value >> bit) & 1) == 1)
			{
				continue;
			}
			value |= (uint)(1 << bit);
			player.Attributes.Set(101u, sid, value);
			player.Attributes.SyncTo(sync, 101u, sid, value);
			sync.Custom[player.ToPackedAttrKey(101u, sid)] = value;
			sync.Custom[player.ToShiftedAttrKey(101u, sid)] = value;
			foreach (List<uint> item in items)
			{
				await MikuSB.GameServer.Server.CallGS.Handlers.Shop.IBLogic_BuyGoods.GrantRewardAsync(player, sync, item);
			}
			granted++;
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"massagemax: 调理等级已设为 {target}，经验清零，发放 {granted} 档等级奖励（uid {player.Uid}）。");
	}
}
