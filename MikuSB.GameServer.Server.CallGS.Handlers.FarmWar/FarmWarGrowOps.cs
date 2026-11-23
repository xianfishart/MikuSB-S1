using System.Text.Json.Nodes;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

internal static class FarmWarGrowOps
{
	/// <summary>
	/// 培育战技的激活 / 重置。请求里的 nId 是培育行号（1、2…），nSkillId 才是技能节点 id（10000+）。
	/// 旧实现把 nId 当节点 id 存进 165 属性，导致客户端 IsActive 永远读不到，界面点了没反应。
	/// </summary>
	public static CallGSResult Apply(PlayerInstance player, FarmWarGrowParam request, bool active, string api)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint num = ((request.SkillId != 0) ? request.SkillId : request.Id);
		if (num != 0 && !FarmWarActivityService.SetSkill(player, num, active, sync))
		{
			return CallGSResult.Error("farmwar.farmwar_tip_03", api);
		}
		JsonObject jsonObject = new JsonObject { ["nId"] = request.Id };
		if (request.SkillId != 0)
		{
			jsonObject["nSkillId"] = request.SkillId;
		}
		return CallGSResult.Ok(jsonObject, sync, api);
	}
}
