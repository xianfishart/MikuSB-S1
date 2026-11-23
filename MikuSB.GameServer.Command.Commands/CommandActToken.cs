using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 活动商店代币（2026-11-11 新增，修复记录第 236 节）。
/// 网页面板「活动代币合集」按钮调 <c>/acttoken &lt;期号&gt; 99999 @uid</c>。
///
/// 代币口径来自 Resources/dlc/dlc_shop.json 的 ShopMoneyType 四元组
/// （= Resources/item/templates/suplies.json 的 Genre/Detail/Particular/Level）：
///   第 5 期 = 5/29/2/1、5/29/2/2   (supplies.dlc501 / dlc502)
///   第 6 期 = 5/30/2/3、5/30/2/4   (supplies.dlc601 / dlc602，唯一走 30 段的一期)
///   第 7 期 = 5/29/3/1、5/29/3/2   (supplies.dlc701 / dlc702)
///   其余各期 = 5/29/&lt;商店 Particular&gt;/1、/2
/// 追忆商店那批（401~404 用 Level 3/4）不在表里 —— 第 235 节已把追忆商店整体关掉。
/// 上限沿用 InventoryManager.GetSuppliesMaxCount（非 5/4 段一律 99999）。
/// </summary>
[CommandInfo("acttoken", "Grant one era's DLC activity shop tokens (default 9999 each).", "Usage: /acttoken <era> [amount]  (append @uid)  eras: 5,6,7,8,9,10,11,23,24,25,26,27,28,30,31,32,33,34,35,36", new string[]
{
	"atk"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandActToken : ICommands
{
	/// <summary>网页按钮默认一次 9999（2026-11-12 用户要求由 99999 下调）。背包单格上限仍是 99999。</summary>
	public const uint DefaultAmount = 9999u;

	/// <summary>期号 → 该期活动商店代币的 GDPL 四元组（本期限量 + 无限）。</summary>
	public static readonly Dictionary<int, uint[][]> EraTokens = new Dictionary<int, uint[][]>
	{
		[5] = new uint[][] { new uint[] { 5u, 29u, 2u, 1u }, new uint[] { 5u, 29u, 2u, 2u } },
		[6] = new uint[][] { new uint[] { 5u, 30u, 2u, 3u }, new uint[] { 5u, 30u, 2u, 4u } },
		[7] = new uint[][] { new uint[] { 5u, 29u, 3u, 1u }, new uint[] { 5u, 29u, 3u, 2u } },
		[8] = new uint[][] { new uint[] { 5u, 29u, 4u, 1u }, new uint[] { 5u, 29u, 4u, 2u } },
		[9] = new uint[][] { new uint[] { 5u, 29u, 5u, 1u }, new uint[] { 5u, 29u, 5u, 2u } },
		[10] = new uint[][] { new uint[] { 5u, 29u, 6u, 1u }, new uint[] { 5u, 29u, 6u, 2u } },
		[11] = new uint[][] { new uint[] { 5u, 29u, 7u, 1u }, new uint[] { 5u, 29u, 7u, 2u } },
		[23] = new uint[][] { new uint[] { 5u, 29u, 8u, 1u }, new uint[] { 5u, 29u, 8u, 2u } },
		[24] = new uint[][] { new uint[] { 5u, 29u, 9u, 1u }, new uint[] { 5u, 29u, 9u, 2u } },
		[25] = new uint[][] { new uint[] { 5u, 29u, 10u, 1u }, new uint[] { 5u, 29u, 10u, 2u } },
		[26] = new uint[][] { new uint[] { 5u, 29u, 11u, 1u }, new uint[] { 5u, 29u, 11u, 2u } },
		[27] = new uint[][] { new uint[] { 5u, 29u, 12u, 1u }, new uint[] { 5u, 29u, 12u, 2u } },
		[28] = new uint[][] { new uint[] { 5u, 29u, 13u, 1u }, new uint[] { 5u, 29u, 13u, 2u } },
		[30] = new uint[][] { new uint[] { 5u, 29u, 14u, 1u }, new uint[] { 5u, 29u, 14u, 2u } },
		[31] = new uint[][] { new uint[] { 5u, 29u, 15u, 1u }, new uint[] { 5u, 29u, 15u, 2u } },
		[32] = new uint[][] { new uint[] { 5u, 29u, 16u, 1u }, new uint[] { 5u, 29u, 16u, 2u } },
		[33] = new uint[][] { new uint[] { 5u, 29u, 17u, 1u }, new uint[] { 5u, 29u, 17u, 2u } },
		[34] = new uint[][] { new uint[] { 5u, 29u, 18u, 1u }, new uint[] { 5u, 29u, 18u, 2u } },
		[35] = new uint[][] { new uint[] { 5u, 29u, 19u, 1u }, new uint[] { 5u, 29u, 19u, 2u } },
		[36] = new uint[][] { new uint[] { 5u, 29u, 20u, 1u }, new uint[] { 5u, 29u, 20u, 2u } }
	};

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		int era = (int)arg.GetInt(0);
		long amount = arg.GetInt(1);
		if (amount <= 0)
		{
			amount = DefaultAmount;
		}
		if (!EraTokens.TryGetValue(era, out uint[][] tokens))
		{
			await arg.SendMsg("Usage: /acttoken <期号> [数量]  (期号: " + string.Join(", ", EraTokens.Keys) + ")");
			return;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		uint give = (uint)Math.Min(amount, uint.MaxValue);
		List<string> report = new List<string>();
		foreach (uint[] gdpl in tokens)
		{
			ulong templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
			if (!GameData.SuppliesByFullId.TryGetValue(templateId, out SuppliesExcel supplies) || supplies == null)
			{
				report.Add($"({gdpl[0]},{gdpl[1]},{gdpl[2]},{gdpl[3]}) 配置缺失");
				continue;
			}
			var item = await player.InventoryManager.AddSuppliesItem(supplies, give);
			string tag = $"{gdpl[0]}/{gdpl[1]}/{gdpl[2]}/{gdpl[3]}";
			report.Add((item != null) ? $"{tag}={item.ItemCount}" : $"{tag}=失败");
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"acttoken 第 {era} 期 +{give}: " + string.Join(" ", report) + $" (uid {player.Uid})");
	}
}
