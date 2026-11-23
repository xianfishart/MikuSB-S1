using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_LotteryJigsaw")]
public class StarWish_LotteryJigsaw : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int count = 1;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			count = req?["nCount"]?.GetValue<int>() ?? req?["nTimes"]?.GetValue<int>() ?? 1;
		}
		catch
		{
		}
		if (count <= 0)
		{
			count = 1;
		}
		if (count > 10)
		{
			count = 10;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		StarWishMainExcel act = StarWishAttr.Act();
		uint cashId;
		uint price;
		if (act != null)
		{
			(cashId, price) = act.LotteryCost;
		}
		else
		{
			cashId = 0u;
			price = 0u;
		}
		uint cost = price * (uint)count;
		if (cashId != 0 && price != 0)
		{
			uint moneySid = cashId * 2 + 1;
			PlayerAttr money = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == moneySid);
			if (money == null || money.Val < cost)
			{
				await CallGSRouter.SendScript(connection, "StarWish_LotteryJigsaw", "{\"sErr\":\"error.cash_not_enough\"}", sync);
				return;
			}
			money.Val -= cost;
			sync.Custom[player.ToPackedAttrKey(1u, moneySid)] = money.Val;
			sync.Custom[player.ToShiftedAttrKey(1u, moneySid)] = money.Val;
		}
		JsonArray ids = new JsonArray();
		List<StarWishJigsawExcel> pool = GameData.StarWishJigsawData.Values.Where((StarWishJigsawExcel x) => !StarWishAttr.OwnsJigsaw(player, x.Id)).ToList();
		Random rng = new Random();
		for (int i = 0; i < count; i++)
		{
			if (pool.Count <= 0)
			{
				break;
			}
			StarWishJigsawExcel pick = pool[rng.Next(pool.Count)];
			StarWishAttr.GrantJigsaw(player, pick.Id, sync);
			ids.Add((int)pick.Id);
			pool.Remove(pick);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "StarWish_LotteryJigsaw",
			["tbJigsaw"] = ids,
			["nScore"] = (int)StarWishAttr.Score(player)
		};
		await CallGSRouter.SendScript(connection, "StarWish_LotteryJigsaw", rsp.ToJsonString(), sync);
	}
}
