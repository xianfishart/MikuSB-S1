using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace S1Admin;

/// <summary>
/// 心意寄语（Dorm 玩法）当期期号播种（第 248 节）。
///
/// 客户端 RoleFestivalMessage.GetActId() 反汇编后实际是
///     me:GetAttribute(72, 1)
/// 服务端一直没人写这个属性，于是客户端拿到 0，
/// GetCurConf() = tbActConf[tonumber(0)] = tbActConf[0] = nil，
/// 心意寄语界面拿不到配置整屏白（入口本身能点开，看起来像白屏卡死）。
///
/// 这里按 pin 期号那一行 DlcFuncConfId 里的 Dorm 值写入属性 72/1，
/// 并把该期的寄语全部置为已开放（RoleFestivalService.OpenMessages）。
/// </summary>
public static class DormActSeed
{
	private static readonly Logger Logger = new Logger("DormSeed");

	public static void Apply(PlayerInstance player)
	{
		uint era = DlcPin.Override();
		if (DlcPin.IsDefault(era))
		{
			return;
		}
		if (!GameData.DlcActivityData.TryGetValue(era, out DlcActivityExcel row))
		{
			return;
		}
		uint actId = DlcWindowPush.ResolveFunc(row.DlcFuncConfId, "Dorm");
		if (actId == 0)
		{
			// 第 249 节补：dlc_activities.json 里 ID=24 的 DlcFuncConfId 已按
			// 「四十一」条目的回滚要求注释掉（改名为 _DlcFuncConfId），
			// ResolveFunc 因此拿不到 Dorm。这里按「四十一」条目的期号考证兜底：
			// house/MessageActivity/ActivityTime.json 的 ActivityID 1 正是
			// 20241231~20250114，与 DLC24 窗口（20241219~20250116）吻合，
			// 且 30 期 Dorm=2、35 期 Dorm=3 连续可对照，所以 24 期 Dorm=1。
			// 属性本身会落存档，一旦存档里的 72/1 丢失又没人补，心意寄语就会白屏，
			// 所以这里不能只靠残留值。
			actId = FallbackDorm(era);
			if (actId != 0)
			{
				Logger.Info($"第 {era} 期 DlcFuncConfId 里没有 Dorm，按内置表兜底 act={actId}。");
			}
		}
		if (actId == 0)
		{
			return;
		}
		if (!GameData.RoleFestivalMessageData.ContainsKey(actId))
		{
			Logger.Warn($"第 {era} 期 Dorm={actId} 在 house/MessageActivity 里没有对应行，跳过属性播种。");
			return;
		}
		// 第 418 节：走 Attributes.GetOrCreate，保证内存字典与 data.Attrs 一致。
		player.Attributes.GetOrCreate(72u, 1u).Val = actId;
		int num = RoleFestivalService.OpenMessages(player, actId);
		Logger.Info($"第 {era} 期 心意寄语 act={actId} 写入属性 72/1，开放 {num} 条寄语。");
	}

	/// <summary>
	/// 第 249 节补：DlcFuncConfId 被注释掉后的兜底表。
	/// 只登记已经过「时间窗对齐 + 相邻期连续性」双验证的期号，不做任何猜测，
	/// 查不到就返回 0，让调用方按原逻辑跳过。
	/// </summary>
	private static uint FallbackDorm(uint era)
	{
		return era switch
		{
			24u => 1u,
			_ => 0u
		};
	}
}
