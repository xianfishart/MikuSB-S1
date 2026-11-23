using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

[CallGSApi("DefendLogic_GetLevelAward")]
public class DefendLogic_GetLevelAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint actId = player.GetAttr(10u, 0u);
		uint score = player.GetAttr(10u, 5u);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (uint index in ReadIds(request["tbIdList"]))
		{
			DefendStore.LevelAwardRow row = DefendStore.LevelAwards().FirstOrDefault((DefendStore.LevelAwardRow x) => x.ActId == actId && x.Index == index);
			if (row == null || row.Award.Count < 5 || score < row.NeedExp)
			{
				continue;
			}
			uint flagSid = 20 + index / 30;
			if (flagSid > 30)
			{
				continue;
			}
			int bit = (int)(index % 30);
			PlayerAttr flag = DefendStore.GetOrCreate(player, flagSid);
			if (((flag.Val >> bit) & 1) != 0)
			{
				continue;
			}
			List<IReadOnlyList<uint>> rewards = new List<IReadOnlyList<uint>> { row.Award };
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rewards, sync))
			{
				granted.Add(item?.DeepClone());
			}
			flag.Val |= (uint)(1 << bit);
			player.Attributes.SyncTo(sync, flag);
		}
		if (granted.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		await CallGSRouter.SendScript(connection, "DefendLogic_GetLevelAward", new JsonObject { ["tbAwardList"] = granted }.ToJsonString(), sync);
	}

	private static List<uint> ReadIds(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			uint num = DefendStore.ReadUint(item);
			if (num != 0 && !list.Contains(num))
			{
				list.Add(num);
			}
		}
		return list;
	}
}
