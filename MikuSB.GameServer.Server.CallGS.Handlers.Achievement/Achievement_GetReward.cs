using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("Achievement_GetReward")]
public class Achievement_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		GetRewardParam req = JsonSerializer.Deserialize<GetRewardParam>(param);
		if (req == null || req.Id <= 0)
		{
			await CallGSRouter.SendScript(connection, "Achievement_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		try
		{
			player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)req.Id);
		}
		catch
		{
		}
		// 第 450 节：绘梦沙盘「繁荣嘉奖 → 绘梦档案」的任务只存在于
		// Resources/achievement/activity_quest.json（属性组 7），
		// 走 TaskProgressService（achievement.json / 属性组 2）永远查不到，
		// 所以这里先按绘梦任务处理一遍，命中就不往下走。
		if (RmolTaskService.Find(req.Id) != null)
		{
			(List<int> claimed, JsonArray rmolRewards, _) = await RmolTaskService.ClaimAsync(player, new int[1] { req.Id }, sync);
			if (claimed.Count == 0)
			{
				await CallGSRouter.SendScript(connection, "Achievement_GetReward", "{\"sErr\":\"achievement.NotFinished\"}");
				return;
			}
			JsonObject rmolRsp = new JsonObject
			{
				["nId"] = req.Id,
				["tbRewards"] = rmolRewards
			};
			await CallGSRouter.SendScript(connection, "Achievement_GetReward", rmolRsp.ToJsonString(), sync);
			return;
		}
		List<JsonArray> groups = await TaskProgressService.Claim(connection, req.Id, sync);
		if (groups.Count == 0 && !HasClaimed(player, req.Id))
		{
			await CallGSRouter.SendScript(connection, "Achievement_GetReward", "{\"sErr\":\"achievement.NotFinished\"}");
			return;
		}
		TaskProgressService.Save(player);
		DailyPointService.Recompute(player, sync);
		JsonArray rewards = new JsonArray();
		foreach (JsonArray group in groups)
		{
			foreach (JsonNode item in group)
			{
				rewards.Add(item?.DeepClone());
			}
		}
		JsonObject rsp = new JsonObject
		{
			["nId"] = req.Id,
			["tbRewards"] = rewards
		};
		await CallGSRouter.SendScript(connection, "Achievement_GetReward", rsp.ToJsonString(), sync);
	}

	private static bool HasClaimed(PlayerInstance player, int taskId)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)taskId);
		return playerAttr != null && (playerAttr.Val & 1) != 0;
	}
}
