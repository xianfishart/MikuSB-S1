using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GetPuzzleMapReward")]
public class GetPuzzleMapReward : IHouseFuncHandler
{
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
		JsonObject request = (root["tbParam"] as JsonObject) ?? root;
		int mapId = (int)HouseAttr.Read(player, DormPuzzleRequests.MapIdSid);
		List<DormPuzzleService.MapRow> maps = DormPuzzleService.LoadMaps();
		DormPuzzleService.MapRow map = maps.FirstOrDefault((DormPuzzleService.MapRow x) => x.MapId == mapId);
		List<int[]> reward = new List<int[]>();
		if (map != null)
		{
			reward.AddRange(map.PercentReward);
			JsonNode jsonNode = request["MapState"];
			if (jsonNode is JsonArray state && state.Count > 0)
			{
				reward.AddRange(map.CompleteReward);
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (int[] row in reward)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, row.Select((int x) => (uint)x).ToList());
		}
		uint expectedVoucher = (uint)reward.Where((int[] x) => x[0] == 4 && x[1] == 1 && x[2] == 6 && x[3] == 5).Sum((int[] x) => (uint)Math.Max(0, x[4]));
		if (expectedVoucher != 0)
		{
			DormPuzzleService.AddVoucher(player, sync, expectedVoucher);
		}
		uint refresh = HouseAttr.Read(player, DormPuzzleRequests.RefreshSid);
		await HouseAttr.SetAsync(connection, DormPuzzleRequests.RefreshSid, (refresh != 0) ? (refresh - 1) : 0u, sync);
		await HouseAttr.SetAsync(connection, DormPuzzleRequests.MapIdSid, (refresh > 1) ? 1u : 0u, sync);
		player.SetStrAttr(101u, DormPuzzleRequests.MapStateSid, "[]");
		sync.CustomStr[player.ToPackedAttrKey(101u, DormPuzzleRequests.MapStateSid)] = "[]";
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "House_Request", new JsonObject
		{
			["FuncName"] = "GetPuzzleRewardSuccess",
			["bSuccess"] = true,
			["tbReward"] = new JsonArray(reward.Where((int[] x) => x.Length >= 5).Select((Func<int[], JsonNode>)((int[] x) => new JsonArray(x.Take(5).Select((Func<int, JsonNode>)((int v) => JsonValue.Create(v))).ToArray()))).ToArray())
		}.ToJsonString(), sync);
	}
}
