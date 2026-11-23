using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 第 488 节：给目标玩家发放「多诺卡」（DLC 局外卡背包，属性组 185，每张卡占 10 个 sid）。
///
/// 用法：
///   /donocard &lt;模板ID&gt; &lt;数量&gt; [星级] [品质]      （控制台要带 @uid）
///   例：/donocard 1 60        发 60 张沉堤石人（1 星 1 品质）
///       /donocard 16 60 1 5   发 60 张披风游侠（1 星 5 品质）
///
/// 背包上限已从 350 放宽到 1200（DonoDuelBag.MaxUniqueId），uid 单调递增、不复用。
/// </summary>
[CommandInfo("donocard", "Grant DonoDuel hero cards to the target player.", "Usage: /donocard <templateId> <count> [star] [quality]  (append @uid)", new string[]
{
	"donocards"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDonoCard : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int template = arg.GetInt(0);
		int count = arg.GetInt(1);
		int star = arg.GetInt(2);
		int quality = arg.GetInt(3);
		if (template <= 0 || count <= 0)
		{
			await arg.SendMsg("donocard: usage /donocard <templateId> <count> [star] [quality] @uid");
			return;
		}
		if (count > 1200)
		{
			count = 1200;
		}
		uint useStar = (uint)((star <= 0) ? 1 : Math.Min(star, 5));
		uint useQuality = (uint)((quality <= 0) ? 1 : quality);
		List<(uint, uint, uint)> rows = new List<(uint, uint, uint)>();
		for (int i = 0; i < count; i++)
		{
			rows.Add(((uint)template, useStar, useQuality));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int before = (int)player.Attributes.GetValue(185u, 2u);
		int stock = (int)player.Attributes.GetValue(185u, 3u);
		List<uint> granted = DonoDuelBag.GrantCards(player, rows, sync);
		await CallGSRouter.SendScript(arg.Target, "GM_AttrSync", "{}", sync);
		DonoDuelBag.Save(player);
		await arg.SendMsg($"donocard: template={template} star={useStar} quality={useQuality} requested={count} granted={granted.Count} uid {before}->{(int)player.Attributes.GetValue(185u, 2u)} stock {stock}->{(int)player.Attributes.GetValue(185u, 3u)}");
	}
}
