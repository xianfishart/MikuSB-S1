using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

/// <summary>
/// 纷愿绮梦天赋表（Resources/dlc/DreamCard/talent.json）的只读缓存。
/// 每行形如 { "TalentID": 501, "MoonNum": 1, "Condition": 501, ... }，
/// MoonNum 是解锁该天赋要花的潜质点（货币 52，落属性 1/105）。
/// 解锁（DreamCard_UnlockTalent）与重置退还（DreamCard_ResetTalent）共用这一份，
/// 避免两边各读一遍文件、也保证「退多少」永远等于「扣多少」。
/// </summary>
internal static class DreamCardTalentTable
{
	private static Dictionary<int, int>? _costs;
	private static Dictionary<int, List<int>>? _children;
	private static readonly object _lock = new object();

	private static void Load(out Dictionary<int, int> costs, out Dictionary<int, List<int>> children)
	{
		lock (_lock)
		{
			if (_costs == null || _children == null)
			{
				Dictionary<int, int> costMap = new Dictionary<int, int>();
				Dictionary<int, List<int>> childMap = new Dictionary<int, List<int>>();
				try
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DreamCard", "talent.json");
					if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
					{
						foreach (JsonNode? node in rows)
						{
							if (node is not JsonObject row)
							{
								continue;
							}
							int talentId = row["TalentID"]?.GetValue<int>() ?? 0;
							if (talentId <= 0)
							{
								continue;
							}
							costMap[talentId] = row["MoonNum"]?.GetValue<int>() ?? 0;
							int parent = row["Condition"]?.GetValue<int>() ?? 0;
							if (parent > 0)
							{
								if (!childMap.TryGetValue(parent, out List<int>? list))
								{
									list = new List<int>();
									childMap[parent] = list;
								}
								list.Add(talentId);
							}
						}
					}
				}
				catch
				{
					costMap.Clear();
					childMap.Clear();
				}
				_costs = costMap;
				_children = childMap;
			}
			costs = _costs;
			children = _children;
		}
	}

	/// <summary>
	/// 取天赋解锁花费；表整张读失败时按 1 点兜底，能读到表但查不到该 ID 时算 0
	/// （解锁也就没扣钱，退还自然不该给钱）。
	/// </summary>
	public static int GetCost(int talentId)
	{
		Load(out Dictionary<int, int> costs, out _);
		if (costs.Count == 0)
		{
			return 1;
		}
		return costs.TryGetValue(talentId, out int cost) ? cost : 0;
	}

	/// <summary>天赋 ID → 直接依赖它的下一级天赋（talent.json 的 Condition 字段）。</summary>
	public static List<int> GetChildren(int talentId)
	{
		Load(out _, out Dictionary<int, List<int>> children);
		return children.TryGetValue(talentId, out List<int>? list) ? list : new List<int>();
	}

	/// <summary>
	/// 给玩家加潜质点，并按 UnlockTalent 的口径构造同步包。
	/// </summary>
	public static void Refund(PlayerInstance player, int amount, NtfSyncPlayer sync)
	{
		if (amount <= 0)
		{
			return;
		}
		const uint moneySid = 105u;
		PlayerAttr? money = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == 1 && x.Sid == moneySid);
		if (money == null)
		{
			money = new PlayerAttr
			{
				Gid = 1u,
				Sid = moneySid,
				Val = 0u
			};
			player.Data.Attrs.Add(money);
		}
		money.Val += (uint)amount;
		foreach (KeyValuePair<string, int> item in player.BuildMoneySync())
		{
			sync.Money[item.Key] = item.Value;
		}
		sync.Custom[player.ToPackedAttrKey(1u, moneySid)] = money.Val;
		sync.Custom[player.ToShiftedAttrKey(1u, moneySid)] = money.Val;
	}
}
