using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 2026-11-17（第 392 节）：星球守护「伙伴技能无法购买」。
///
/// 修复前是空壳（只回 "{}"），收到的 {"nPalIdx":7,"nSkillIdx":1..3} 被直接丢掉，
/// 所以点「购买」既不扣守护纪念章、也不写技能等级，界面永远停在未购买。
///
/// 客户端链路（运行时 dump + 反汇编确认）：
///   uw_mow_skillitem:UpgradeSkill → AlienDefense.UpgradeSkill(nPalIdx, nSkillIdx)
///     me:CallGS("AlienDefense_UpgradeSkill", {nPalIdx, nSkillIdx})   ← 两个参数，不带回调
///   回包由框架路由到 umg_mow_skill:OnRspUpgradeSkill(rsp)：
///     读 rsp.nPalIdx / rsp.nSkillIdx（缺任一直接 return）
///     EventSystem.TriggerTarget(self, "OnUpgrade")  ← 让该卡片重刷
///     ShowNewUnlock(tbCostSkill[nSkillIdx].tbInfo[1][1])
///     UpdateLevel()                                 ← 重新读属性
///
/// 技能等级存在位图属性 (143, 301 + nPalIdx)，每个技能占 4 位，按 nSkillIdx 排：
///   等级 = GetBits(GetAttribute(143, 301+nPalIdx), (nSkillIdx-1)*4, 4)
///   0 = 未购买（界面显示「购买」），1/2 = 已购买（界面显示已拥有，并解锁对应被动）
/// 这也正是 GetPalPassiveSkill 往战斗里塞被动的来源，写完等级后被动自动生效。
///
/// 费用口径（客户端 tbCostSkill，来自 aliendefense_pal_main.txt 的 ExtraPassive0xCost）：
///   nSkillIdx=1 → ExtraPassive02：单级，100 守护纪念章
///   nSkillIdx=2 → ExtraPassive03：两级，100 / 150
///   nSkillIdx=3 → ExtraPassive04：两级，100 / 150
/// 服务端 json 把每级费用合并成了一个最大值（{"53":150}），首级的 100 拿不到，
/// 所以金额按上面的固定口径算；货币号仍是配置里的 53（守护纪念章，属性 (1,107)）。
/// </summary>
[CallGSApi("AlienDefense_UpgradeSkill")]
public class AlienDefense_UpgradeSkill : ICallGSHandler
{
	/// <summary>星球守护属性组 GID。</summary>
	private const uint Gid = 143u;

	/// <summary>守护纪念章的货币号（客户端 Cash 里是 53，属性落 (1, 107)）。</summary>
	private const uint CoinType = 53u;

	/// <summary>伙伴技能等级位图的起始 Sid：(301 + nPalIdx)。</summary>
	private const uint CostSkillSidStart = 301u;

	/// <summary>本期可购买技能数（ExtraPassive02 / 03 / 04 三档）。</summary>
	private const uint MaxSkillIdx = 3u;

	/// <summary>每个技能占用的位数。</summary>
	private const int BitsPerSkill = 4;

	private static readonly Logger Log = new Logger("AlienDefense.Skill");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint palIdx = ParseUInt(param, "nPalIdx");
		uint skillIdx = ParseUInt(param, "nSkillIdx");
		if (palIdx == 0u || skillIdx == 0u || skillIdx > MaxSkillIdx)
		{
			Log.Info($"UpgradeSkill bad param pal={palIdx} skill={skillIdx}");
			await Send(connection, player, 0u, 0u, null);
			return;
		}
		uint sid = CostSkillSidStart + palIdx;
		PlayerAttr attr = GetOrCreateAttr(player, sid);
		int shift = (int)((skillIdx - 1u) * (uint)BitsPerSkill);
		uint level = (attr.Val >> shift) & 0xFu;
		uint maxLevel = MaxLevel(skillIdx);
		if (level >= maxLevel)
		{
			// 已满级：只回显，客户端刷新一次即可（按钮本身此时也会隐藏）。
			Log.Info($"UpgradeSkill already max pal={palIdx} skill={skillIdx} lv={level}");
			await Send(connection, player, palIdx, skillIdx, null);
			return;
		}

		uint cost = CostFor(level + 1u);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!PortCurrency.TrySpend(player, CoinType, cost, sync, out string error))
		{
			Log.Info($"UpgradeSkill no coin pal={palIdx} skill={skillIdx} cost={cost} err={error}");
			await Send(connection, player, 0u, 0u, null);
			return;
		}
		uint nextLevel = level + 1u;
		uint mask = 0xFu << shift;
		attr.Val = (attr.Val & ~mask) | (nextLevel << shift);
		sync.Custom[player.ToPackedAttrKey(Gid, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(Gid, sid)] = attr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"UpgradeSkill pal={palIdx} skill={skillIdx} {level}->{nextLevel} cost={cost} sid={sid} raw={attr.Val}");
		await Send(connection, player, palIdx, skillIdx, sync);
	}

	/// <summary>各技能的最高等级：第一个技能单级，后两个技能两级。</summary>
	private static uint MaxLevel(uint skillIdx)
	{
		return (skillIdx >= 2u) ? 2u : 1u;
	}

	/// <summary>升到第 level 级的费用（守护纪念章）：首级 100，第二级 150。</summary>
	private static uint CostFor(uint level)
	{
		return (level >= 2u) ? 150u : 100u;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = Gid,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		return attr;
	}

	private static uint ParseUInt(string param, string name)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty(name, out JsonElement node) && node.TryGetUInt32(out uint value))
			{
				return value;
			}
		}
		catch
		{
		}
		return 0u;
	}

	/// <summary>回包必须带 nPalIdx / nSkillIdx，客户端 OnRspUpgradeSkill 靠它刷新卡片。</summary>
	private static Task Send(Connection connection, PlayerInstance player, uint palIdx, uint skillIdx, NtfSyncPlayer sync)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("{\"nPalIdx\":").Append(palIdx).Append(",\"nSkillIdx\":").Append(skillIdx).Append('}');
		return CallGSRouter.SendScript(connection, "AlienDefense_UpgradeSkill", sb.ToString(), sync);
	}
}
