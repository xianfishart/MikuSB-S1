using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("Achievement_QuickGetReward")]
public class Achievement_QuickGetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = JsonNode.Parse(param)?.AsObject();
		if (root == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray allRewards = new JsonArray();
		JsonNode jsonNode = root["tbIdList"];
		if (jsonNode is JsonArray ids)
		{
			foreach (JsonNode item in ids)
			{
				int id = (int.TryParse(item?.ToString(), out var n) ? n : 0);
				if (id <= 0)
				{
					continue;
				}
				foreach (JsonArray group in await TaskProgressService.Claim(connection, id, sync))
				{
					// 第 417 节：tbRewards 的元素是「一封奖励的分组」（组内是奖励行），
					// 客户端按一层分组渲染弹窗；扁平化会让每行数字被当成独立道具，弹窗只剩空格子。
					allRewards.Add(group.DeepClone());
				}
			}
		}
		// 第 416 节：一键领取顺带把执行度宝箱（group 1）里达到阈值且未领的全部领掉，
		// 领取口径与 AchievementPoint_GetReward 完全一致（sid = 10 + index - 1）。
		uint point = DailyPointService.Recompute(player, sync);
		List<AchievementPointRewardExcel> pointRows = (from x in GameData.PointRewardData
			where x.Group == 1u
			orderby x.Point
			select x).ToList();
		for (int i = 0; i < pointRows.Count; i++)
		{
			uint sid = 10u + (uint)i;
			if (player.Attributes.GetValue(8u, sid) != 0u || point < pointRows[i].Point)
			{
				continue;
			}
			List<List<uint>> rewardRows = AchievementClaimService.ReadRewardRows(pointRows[i].Rewards);
			JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
			PlayerAttr claimedAttr = player.Attributes.GetOrCreate(8u, sid);
			claimedAttr.Val = 1u;
			player.Attributes.SyncTo(sync, claimedAttr);
			JsonArray boxRewards = new JsonArray();
			foreach (JsonNode node in granted)
			{
				boxRewards.Add(node.DeepClone());
			}
			if (boxRewards.Count > 0)
			{
				// 每个宝箱作为一个分组，与「一封任务一组」的口径保持一致。
				allRewards.Add(boxRewards);
			}
		}
		TaskProgressService.Save(player);
		DailyPointService.Recompute(player, sync);
		JsonObject rsp = new JsonObject { ["tbRewards"] = allRewards };
		await CallGSRouter.SendScript(connection, "Achievement_QuickGetReward", rsp.ToJsonString(), sync);
	}
}
