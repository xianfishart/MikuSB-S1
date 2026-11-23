using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GetHouseLevelReward")]
public class GetHouseLevelReward : IHouseFuncHandler
{
	private const uint Gid = 101u;

	private const uint HouseInfoStart = 3000u;

	private const uint HouseLoveLevelRewardStart = 102u;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int level = 0;
		try
		{
			level = root["Level"]?.GetValue<int>() ?? 0;
		}
		catch
		{
		}
		if (level > 0)
		{
			// 2026-11-13 改（修复记录第 283 节）：原来只置位、不发奖励，
			// 氛围值升上去后点「领取」只看到按钮消失、背包里什么都没有。
			// 改成走 HouseLevelRewardHelper：置位 + 真正发放 house_level_reward.json 的 Item。
			uint sid = 3102 + (uint)(level / 30);
			int bit = level % 30;
			List<uint>? reward = HouseLevelRewardHelper.LoadRow("house_level_reward.json", level);
			if (reward != null && reward.Count >= 5)
			{
				(JsonArray Rewards, NtfSyncPlayer Sync) granted = await HouseLevelRewardHelper.GrantAsync(connection, reward, sid, bit);
				root["Rewards"] = granted.Rewards;
				// 2026-11-13 改（修复记录第 286 节）：客户端领奖回包读的是 tbReward / tbRewards
				// （与推币、神经调理等已调通接口一致），只给 "Rewards" 时客户端拿不到奖励数据，
				// 界面上就没有「获得物品」的即时反馈。
				root["tbReward"] = granted.Rewards.DeepClone();
				root["tbRewards"] = granted.Rewards.DeepClone();
				await player.SendPacket(1004, granted.Sync);
				// 2026-11-13 改（修复记录第 285 节）：官方原版置位后是发 NtfSetAttr(1011)，
				// 客户端界面读的是本地属性缓存。上一版改成发奖励后只发了 1004 同步包，
				// 结果库里已置位、奖励也进了背包，但界面依然显示「可领取」，再点不会重复发。
				await player.SendPacket(1011, new NtfSetAttr
				{
					Gid = Gid,
					Sid = sid,
					Val = player.GetAttr(101u, sid)
				});
			}
			else
			{
				uint val = player.GetAttr(101u, sid) | (uint)(1 << bit);
				player.SetAttr(Gid, sid, val);
				DatabaseHelper.SaveDatabaseType(player.Data);
				await player.SendPacket(1011, new NtfSetAttr
				{
					Gid = Gid,
					Sid = sid,
					Val = val
				});
			}
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root));
	}
}
