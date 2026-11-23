using System.Text.Json.Nodes;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

internal static class DonoDuelFormation
{
	public const uint GroupId = 185u;

	public static (uint Start, uint End) Range(uint type)
	{
		if (1 == 0)
		{
		}
		(uint, uint) result = type switch
		{
			1u => (10u, 13u), 
			2u => (14u, 17u), 
			3u => (18u, 23u), 
			_ => (10u, 13u), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}

	/// <summary>
	/// 第 489 节：判断某个「出阵」阵容里有没有指定模板的卡。
	///
	/// 阵容槽位（185/10~23）每格存 (uid &lt;&lt; 16) | 位置，
	/// 客户端 DonoDuel.GetArenaFormation 读的是 ArenaFormationStart=14 / End=17，
	/// 也就是 <see cref="Range"/> 的 type=2（巅峰对决出战队伍）。
	///
	/// 剧情任务的 Objectives=9 要求「出阵【某张卡】」，所以必须按阵容判，
	/// 不能只看背包（DonooduelBag.HasTemplate 是整包）。
	/// </summary>
	public static bool FormationHasTemplate(PlayerInstance player, uint type, uint templateId)
	{
		if (templateId == 0)
		{
			return false;
		}
		(uint Start, uint End) range = Range(type);
		for (uint sid = range.Start; sid <= range.End; sid++)
		{
			uint val = player.Attributes.GetValue(GroupId, sid);
			uint uid = val >> 16;
			if (uid != 0 && DonoDuelBag.GetTemplateId(player, uid) == templateId)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>出战阵容是否为空（用来区分「玩家没配队」和「配了但没有这张卡」）。</summary>
	public static bool FormationIsEmpty(PlayerInstance player, uint type)
	{
		(uint Start, uint End) range = Range(type);
		for (uint sid = range.Start; sid <= range.End; sid++)
		{
			if (player.Attributes.GetValue(GroupId, sid) >> 16 != 0)
			{
				return false;
			}
		}
		return true;
	}
}
