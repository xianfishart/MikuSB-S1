using System;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 货币（2026-11-11 新增，修复记录第 218/219/220 节）。
/// 网页面板原来的快捷按钮只能给物品，加货币没有入口，所以补一个 money 命令；
/// 网页上的「通用银 ×1000 万」按钮就是调 <c>money silver 10000000</c>。
///
/// moneyType 口径以 RewardManager.GrantUseableAsync 为准（others.json 的 LuaType）：
///   1 = 比特金 (money_box)   2 = 数据金 (gold_box)
///   3 = 通用银 (silver_box)  4 = 体力   (vigor_box，单独存 Data.Vigor)
/// 属性 Sid = type * 2 + 1（1→3、2→5、3→7）。
/// 2026-11-11 三次修正：先前误把 1 当通用银，结果加到比特金上（见第 220 节）。
/// </summary>
[CommandInfo("money", "Add / show currencies (1 比特金, 2 数据金, 3 通用银, 4 体力).", "Usage: /money silver [amount]  |  /money add <type> <amount>  |  /money set <type> <value>  |  /money list  (append @uid to target another player)", new string[] { "mn" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandMoney : ICommands
{
	/// <summary>通用银的 moneyType（silver_box）。注意不是 1，1 是比特金。</summary>
	public const uint SilverType = 3u;

	/// <summary>网页按钮默认一次给 1000 万。</summary>
	public const uint DefaultSilver = 10000000u;

	[CommandMethod("silver")]
	public async ValueTask AddSilver(CommandArg arg)
	{
		long amount = arg.GetInt(0);
		if (amount <= 0)
		{
			amount = DefaultSilver;
		}
		await Grant(arg, SilverType, amount);
	}

	[CommandMethod("add")]
	public async ValueTask Add(CommandArg arg)
	{
		long type = arg.GetInt(0);
		long amount = arg.GetInt(1);
		if (type <= 0)
		{
			type = SilverType;
		}
		if (amount <= 0)
		{
			await arg.SendMsg("Usage: /money add <type> <amount>  (1 比特金, 2 数据金, 3 通用银, 4 体力)");
			return;
		}
		await Grant(arg, (uint)type, amount);
	}

	[CommandMethod("list")]
	public async ValueTask List(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		StringBuilder sb = new StringBuilder();
		sb.Append("UID ").Append(player.Uid).Append(" money:");
		// 1~10 覆盖游戏里在用的货币段（1 比特金、2 数据金、3 通用银、4 体力走 Data.Vigor）。
		for (uint type = 1u; type <= 10u; type++)
		{
			uint value = PortCurrency.GetBalance(player, type);
			if (value != 0)
			{
				sb.Append(' ').Append(type).Append('(').Append(CurrencyName(type)).Append(")=").Append(value);
			}
		}
		await arg.SendMsg(sb.ToString());
	}

	[CommandMethod("set")]
	public async ValueTask Set(CommandArg arg)
	{
		uint type = (uint)Math.Max(0, arg.GetInt(0));
		long value = arg.GetInt(1);
		if (type == 0)
		{
			type = SilverType;
		}
		if (value < 0)
		{
			value = 0;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		string name4 = CurrencyName(type);
		if (type == 4)
		{
			// 体力单独存在 PlayerGameData.Vigor，不走属性表。
			player.Data.Vigor = (uint)Math.Min(uint.MaxValue, value);
			NtfSyncPlayer vigorSync = new NtfSyncPlayer();
			vigorSync.Core[2u] = player.Data.Vigor;
			await player.SendPacket(1004, vigorSync);
			await SyncAll(arg, vigorSync);
			await arg.SendMsg($"money 4({name4}) = {player.Data.Vigor} (uid {player.Uid}).");
			return;
		}
		uint sid = MoneySid(type);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttributes attributes = player.Attributes;
		attributes.SyncTo(sync, attributes.Set(1u, sid, (uint)Math.Min(uint.MaxValue, value)));
		if (type == 1)
		{
			// 只有比特金走 money 映射表，其它货币不写，免得把别的钱包字段带歪。
			foreach (var (key, money) in player.BuildMoneySync())
			{
				sync.Money[key] = money;
			}
		}
		await player.SendPacket(1004, sync);
		await SyncAll(arg, sync);
		await arg.SendMsg($"money {type}({name4}) set to {PortCurrency.GetBalance(player, type)} (uid {player.Uid}).");
	}

	[CommandDefault]
	public async ValueTask Show(CommandArg arg)
	{
		await arg.SendMsg("Usage: /money silver [amount]  |  /money add <type> <amount>  |  /money set <type> <value>  |  /money list  (append @uid)");
	}

	/// <summary>
	/// 真正的发放：AddCurrency 会按货币类型同步属性，moneyType=1 额外刷整套 Money 字段。
	/// 2026-11-11 二次修正：只发 1004 时服务端数值变了、客户端钱包数字不刷新
	/// （实测连点两次进账 2000 万，游戏里仍显示 30648）。补一条 1010 NtfCallScript
	/// 的 ExtraSync，和「领取好友体力」「商店购买」用的是同一条刷新路径。
	/// </summary>
	private static async ValueTask Grant(CommandArg arg, uint type, long amount)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		// AddCurrency 收 uint，单次上限按 uint 卡住，避免溢出成负。
		long capped = amount > uint.MaxValue ? uint.MaxValue : amount;
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		player.AddCurrency(type, (uint)capped, sync);
		await player.SendPacket(1004, sync);
		await SyncAll(arg, sync);
		await arg.SendMsg($"money {type}({CurrencyName(type)}) +{capped} -> now {PortCurrency.GetBalance(player, type)} (uid {player.Uid}).");
	}

	/// <summary>再发一条 1010 ExtraSync，客户端钱包 / 顶栏才会立刻重画。</summary>
	private static async Task SyncAll(CommandArg arg, NtfSyncPlayer sync)
	{
		if (arg.Target is Connection connection)
		{
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
	}

	/// <summary>货币号 → 属性 Sid。通用银 3 → 7；统一 2n+1（比特金 1→3、数据金 2→5），与 AttrIds.Currency 一致。</summary>
	private static uint MoneySid(uint type)
	{
		return type * 2 + 1;
	}

	/// <summary>货币中文名（只用于控制台回显，口径同 RewardManager 的 LuaType）。</summary>
	private static string CurrencyName(uint type)
	{
		return type switch
		{
			1u => "比特金", 
			2u => "数据金", 
			3u => "通用银", 
			4u => "体力", 
			_ => "货币" + type, 
		};
	}
}
