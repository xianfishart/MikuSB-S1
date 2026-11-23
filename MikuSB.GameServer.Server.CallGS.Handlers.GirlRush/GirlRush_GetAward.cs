using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 心绪补给领取。客户端请求只有 {"nIndex": 奖励序号}，
/// 领取状态读属性组 160、sid = nIndex 的 bit0（GirlRush.GetAwardState）。
/// 奖励配置在 Resources/girlrush/girlrushaward.json，
/// 物品是五元组 [genre, detail, particular, level, count]。
/// 响应 {"tbAward":[[...]]} 会被 umg_girlrush_reward:OnGetAward 拿去调 Item.Gain 弹「获得物品」。
/// </summary>
[CallGSApi("GirlRush_GetAward")]
public class GirlRush_GetAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint index = GirlRushStore.ReadUint(request["nIndex"]);
		if (index == 0)
		{
			await Send(connection, new JsonArray());
			return;
		}
		GirlRushStore.GirlRushAward? award = GirlRushStore.FindAward(index);
		if (award == null)
		{
			GirlRushStore.AppendDebug($"[GetAward] 找不到配置 index={index}");
			await Send(connection, new JsonArray());
			return;
		}
		PlayerAttr claimed = player.Attributes.GetOrCreate(GirlRushStore.AwardGid, index);
		if ((claimed.Val & 1u) != 0)
		{
			await Send(connection, new JsonArray());
			return;
		}
		if (!GirlRushStore.CheckAwardCondition(player, award))
		{
			GirlRushStore.AppendDebug($"[GetAward] 条件未达标 index={index} level={award.LevelId}");
			await CallGSRouter.SendScript(connection, "GirlRush_GetAward", "{\"tbAward\":[],\"sErr\":\"girlrush.NotFinish\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (List<uint> item in award.Items)
		{
			if (item.Count < 5 || item[0] == 0)
			{
				continue;
			}
			await VirCapture_GetLevelAward.GrantRewardAsync(player, sync, item);
			granted.Add(ToJson(item));
		}
		claimed.Val |= 1u;
		player.Attributes.SyncTo(sync, claimed);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		GirlRushStore.AppendDebug($"[GetAward] index={index} level={award.LevelId} items={granted.Count} 已领取");
		await CallGSRouter.SendScript(connection, "GirlRush_GetAward",
			new JsonObject { ["tbAward"] = granted }.ToJsonString(), sync);
	}

	private static JsonArray ToJson(List<uint> item)
	{
		return new JsonArray(item.Take(5).Select(x => (JsonNode)JsonValue.Create((int)x)).ToArray());
	}

	private static async Task Send(Connection connection, JsonArray awards)
	{
		await CallGSRouter.SendScript(connection, "GirlRush_GetAward",
			new JsonObject { ["tbAward"] = awards }.ToJsonString());
	}
}
