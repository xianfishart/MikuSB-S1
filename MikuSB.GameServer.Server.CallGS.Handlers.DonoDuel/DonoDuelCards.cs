using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

internal static class DonoDuelCards
{
	public const uint GroupId = 185u;

	public const uint NextUniqueSid = 2u;

	public const uint CountSid = 3u;

	private const uint BagStart = 1001u;

	private const uint BagStep = 10u;

	private const uint MaxUnique = 350u;

	private static Dictionary<uint, (uint Atk, uint Def, uint Hp)>? _baseStats;

	/// <summary>正式学习一（101）：卡在接取时由 DonoDuelStarterDeck 用 Reward3 发过，领奖不再重复发。</summary>
	public const uint StarterMissionId = 101u;

	/// <summary>
	/// 第 483 节（追加）：支线任务（201~505）把卡牌奖励全部写在 Reward3，
	/// 格式是 {{模板ID, 星级, 品质}, …}，Reward2 反而是空的。
	/// 领奖时 Reward2 发不出东西就回落到 Reward3，否则「养成之路1」这类任务
	/// 只会弹个卡面、背包里一张都不进。
	/// </summary>
	public static int GrantReward3(PlayerInstance player, JToken? reward3, NtfSyncPlayer sync)
	{
		List<(uint, uint, uint)> rows = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelBag.ReadCardRows(reward3);
		if (rows.Count == 0)
		{
			return 0;
		}
		return MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelBag.GrantCards(player, rows, sync).Count;
	}

	/// <summary>
	/// 第 472 节：任务领奖时按 Reward2 发卡，但 101 要跳过（见 GrantForTask）。
	/// </summary>
	public static int GrantForTask(PlayerInstance player, uint missionId, JToken? reward2, NtfSyncPlayer sync)
	{
		if (missionId == StarterMissionId)
		{
			return 0;
		}
		return Grant(player, reward2, sync);
	}

	/// <summary>
	/// 第 472 节：修两处老 bug。
	///   1) 卡位越界覆盖：原写法用「已有卡数」当新卡的 uid（block = 1001 + (count-1)*10），
	///      和 DonoDuelBag.GrantCards 的「先 +1 再用」差一位，导致新卡覆盖掉最后一张旧卡，
	///      同时 185/2 还 +1 —— 正式学习一领奖时把烈火炮手（uid3）盖成了沉堤石人，
	///      卡数变成 6 但只有 5 张，界面显示「沉堤石人×2、拉力侠×2」。
	///   2) 未清技能位：只写 5 个槽，1051+5/6 的技能位可能是别的卡残留。
	/// </summary>
	public static int Grant(PlayerInstance player, JToken? reward2, NtfSyncPlayer sync)
	{
		if (!(reward2 is JArray jArray))
		{
			return 0;
		}
		// 第 488 节：统一走 DonoDuelBag.GrantCards —— 那里已经改成「优先复用被回收的空槽」。
		// 原来的实现在这里自己算 uid（count++ → block = 1001 + (count-1)*10），
		// 回收得越多 uid 越堵，到 350 之后一张都发不出去。
		List<(uint, uint, uint)> rows = new List<(uint, uint, uint)>();
		foreach (JToken item in jArray)
		{
			uint templateId = (uint.TryParse(item?.ToString(), out var result) ? result : 0u);
			if (templateId != 0)
			{
				rows.Add((templateId, 1u, 1u));
			}
		}
		if (rows.Count == 0)
		{
			return 0;
		}
		return MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelBag.GrantCards(player, rows, sync).Count;
	}

	private static void SetSlot(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(GroupId, sid);
		orCreate.Val = value;
		player.Attributes.SyncTo(sync, orCreate);
	}

	private static Dictionary<uint, (uint Atk, uint Def, uint Hp)> BaseStats()
	{
		if (_baseStats != null)
		{
			return _baseStats;
		}
		_baseStats = new Dictionary<uint, (uint, uint, uint)>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "DonoDuel";
			buffer[4] = "donoduel_herocard.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _baseStats;
			}
			foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
			{
				uint valueOrDefault = item.Value<uint?>("ID").GetValueOrDefault();
				if (valueOrDefault != 0)
				{
					_baseStats[valueOrDefault] = (item.Value<uint?>("AttackInitial").GetValueOrDefault(), item.Value<uint?>("DefenseInitial").GetValueOrDefault(), item.Value<uint?>("HealthInitial").GetValueOrDefault());
				}
			}
		}
		catch
		{
		}
		return _baseStats;
	}
}
