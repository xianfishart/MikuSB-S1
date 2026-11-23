using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 新星开拓「伙伴技能证书」四档一套（初级 / 中级 / 高级 / 特级），默认每样 10000 个
/// （2026-11-15 新增，配合伙伴编队「技能升阶」消耗）。
///
/// 物品口径取自 Resources/item/templates/suplies.json，全部落在 [5, 25, 2, Level]：
///   Level 1  临时技能证书I  supplies.vircapture.skillexp1  Color 1  —— 不在本命令范围
///   Level 2  初级技能证书    supplies.vircapture.skillexp2  Color 2
///   Level 3  中级技能证书    supplies.vircapture.skillexp3  Color 3
///   Level 4  高级技能证书    supplies.vircapture.skillexp4  Color 4
///   Level 5  特级技能证书    supplies.vircapture.skillexp5  Color 5
///   Level 6  好伙伴券        supplies.vircapture.token2    —— 不是证书
///
/// 2026-11-15 22:25 修正：实测发现原实现（四档共用一个 sync 连续 GrantRewardAsync）
/// 只有特级真正入包，初/中/高三档静默失败。改为每档独立结算并回读内存条目，
/// 回执直接给出 L2=ok(10000) 这种逐档结果，失败一眼可见。
/// </summary>
[CommandInfo("skillcert", "Give VirCapture partner skill certificates (junior/mid/senior/special).", "Usage: /skillcert [count=10000]  (append @uid)", new string[]
{
	"vccert",
	"jinengzhengshu"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandSkillCert : ICommands
{
	/// <summary>初级 / 中级 / 高级 / 特级 的 [G, D, P, L]。</summary>
	private static readonly uint[][] Items = new uint[4][]
	{
		new uint[4] { 5u, 25u, 2u, 2u },
		new uint[4] { 5u, 25u, 2u, 3u },
		new uint[4] { 5u, 25u, 2u, 4u },
		new uint[4] { 5u, 25u, 2u, 5u }
	};

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int count = arg.GetInt(0);
		if (count <= 0)
		{
			count = 10000;
		}
		if (count > 999999)
		{
			count = 999999;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		StringBuilder sb = new StringBuilder();
		int ok = 0;
		foreach (uint[] row in Items)
		{
			ulong templateId = GameResourceTemplateId.FromGdpl(row[0], row[1], row[2], row[3]);
			uint before = CountOf(player, templateId);
			List<uint> reward = new List<uint>
			{
				row[0],
				row[1],
				row[2],
				row[3],
				(uint)count
			};
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			uint after = CountOf(player, templateId);
			bool granted = after > before;
			if (granted)
			{
				ok++;
			}
			if (sb.Length > 0)
			{
				sb.Append('；');
			}
			sb.Append('L').Append(row[3]).Append(granted ? "=ok" : "=FAIL").Append('(').Append(before).Append("->").Append(after).Append(')');
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"skillcert: {ok}/4 成功，各 {count} 个。明细 {sb}（uid {player.Uid}）");
	}

	/// <summary>回读背包里指定模板的现有数量（没有则 0）。</summary>
	private static uint CountOf(PlayerInstance player, ulong templateId)
	{
		foreach (var item in player.InventoryManager.InventoryData.Items.Values)
		{
			if (item.TemplateId == templateId)
			{
				return item.ItemCount;
			}
		}
		return 0u;
	}
}
