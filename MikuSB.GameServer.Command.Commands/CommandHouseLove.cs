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
/// 好感度测试/修复命令（2026-11-13 新增，修复记录第 283 节）。
///
/// /loveadd &lt;少女ID&gt; &lt;点数&gt; — 直接给指定少女加好感（走与送礼完全相同的 HouseFavor.Award）。
/// /loveadd &lt;点数&gt;            — 只给一个参数时按其所在房间猜不到少女，需两端都给。
/// /houselove &lt;点数&gt;           — 直接加基地氛围值（3100），走 HouseFavor.AwardHouse。
///
/// 用途：不用反复送礼就能验证「好感度到底有没有写进去、客户端是否显示」。
/// </summary>
[CommandInfo("loveadd", "Add girl favor points (HouseFavor.Award).", "Usage: /loveadd <girlId> <points>  (append @uid)", new string[]
{
	"girlfavor",
	"addlove",
	"loveaddpt"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandLoveAdd : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int girlId = arg.GetInt(0);
		int points = arg.GetInt(1);
		if (girlId <= 0 || points <= 0)
		{
			await arg.SendMsg("用法：/loveadd <少女ID> <点数>，例：/loveadd 13 9000");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		(uint Added, int PreLevel, int NewLevel) result = await HouseFavor.Award(arg.Target, girlId, (uint)points, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		uint raw = HouseAttr.Read(player, (uint)(girlId * 50));
		await arg.SendMsg($"loveadd: 少女 {girlId} 好感 +{result.Added}，等级 {result.PreLevel} → {result.NewLevel}，属性值 {raw}（低11位等级 {(raw & 0x7FF)} / 高位点数 {(raw >> 11)}）。");
	}
}

/// <summary>基地氛围值（房屋好感）测试命令：/houselove [点数=4000]。</summary>
[CommandInfo("houselove", "Add house atmosphere (love) points.", "Usage: /houselove [points=4000]  (append @uid)", new string[]
{
	"housefavor",
	"atmosphere",
	"houseatmo"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandHouseLove : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int points = arg.GetInt(0);
		if (points <= 0)
		{
			points = 4000;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		(uint Added, int PreLevel, int NewLevel) result = await HouseFavor.AwardHouse(arg.Target, (uint)points, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		uint raw = HouseAttr.Read(player, HouseFavor.HouseLoveSid);
		await arg.SendMsg($"houselove: 氛围值 +{result.Added}，等级 {result.PreLevel} → {result.NewLevel}，属性值 {raw}（低11位等级 {(raw & 0x7FF)} / 高位点数 {(raw >> 11)}）。");
	}
}
