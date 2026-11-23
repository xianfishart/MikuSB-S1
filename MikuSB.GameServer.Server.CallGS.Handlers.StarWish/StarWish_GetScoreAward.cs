using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_GetScoreAward")]
public class StarWish_GetScoreAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint score = StarWishAttr.Score(player);
		uint got = StarWishAttr.Read(player, 2u);
		JsonArray granted = new JsonArray();
		JsonArray gotList = new JsonArray();
		int wantId = 0;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			wantId = req?["nId"]?.GetValue<int>() ?? (req?["nIndex"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		foreach (var (id, row) in GameData.StarWishScoreAwardData.OrderBy((KeyValuePair<uint, StarWishScoreAwardExcel> x) => x.Key))
		{
			if ((wantId > 0 && id != (uint)wantId) || score < row.Score)
			{
				continue;
			}
			uint bit = id - 1;
			if (bit > 31 || ((got >> (int)bit) & 1) != 0)
			{
				continue;
			}
			foreach (List<uint> award in row.Awards)
			{
				if (award.Count < 5 || award[4] == 0)
				{
					continue;
				}
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, award);
				JsonArray arr = new JsonArray();
				foreach (uint v in award)
				{
					arr.Add((int)v);
				}
				granted.Add(arr);
			}
			got = id;
			gotList.Add((int)id);
		}
		StarWishAttr.Set(player, 2u, got, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_GetScoreAward",
			["tbReward"] = granted,
			["tbGot"] = gotList,
			["nScore"] = (int)score
		};
		await CallGSRouter.SendScript(connection, "StarWish_GetScoreAward", rsp.ToJsonString(), sync);
	}
}
