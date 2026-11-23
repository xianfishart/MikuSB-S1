using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Exchange")]
public class Item_Exchange : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JObject req = JObject.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
		JToken jToken = req["tbGDPLN"];
		JArray gdpln = jToken as JArray;
		if (gdpln == null || gdpln.Count < 5)
		{
			await CallGSRouter.SendScript(connection, "Item_Exchange", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint g = (uint)ReadInt(gdpln[0]);
		uint d = (uint)ReadInt(gdpln[1]);
		uint p = (uint)ReadInt(gdpln[2]);
		uint l = (uint)ReadInt(gdpln[3]);
		uint count = (uint)Math.Max(1, ReadInt(gdpln[4]));
		JObject exchange = LoadExchange(g, d, p, l);
		if (exchange == null)
		{
			await CallGSRouter.SendScript(connection, "Item_Exchange", "{\"sErr\":\"tip.exchange_invaild\"}");
			return;
		}
		JArray cash = exchange["Cash"] as JArray;
		int cashId = ((cash != null && cash.Count > 0) ? ReadInt(cash[0]) : 0);
		int ratio = ((cash != null && cash.Count > 1) ? ReadInt(cash[1]) : 0);
		if (cashId <= 0 || ratio <= 0)
		{
			await CallGSRouter.SendScript(connection, "Item_Exchange", "{\"sErr\":\"tip.exchange_invaild\"}");
			return;
		}
		uint sid = ((cashId == 1) ? 3u : ((uint)(cashId * 2 + 1)));
		uint cost = (uint)(ratio * count);
		PlayerAttr money = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sid);
		if (money == null || money.Val < cost)
		{
			await CallGSRouter.SendScript(connection, "Item_Exchange", "{\"sErr\":\"tip.cash_not_enough\"}");
			return;
		}
		money.Val -= cost;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(1u, sid)] = money.Val;
		sync.Custom[player.ToShiftedAttrKey(1u, sid)] = money.Val;
		foreach (var (key, value) in player.BuildMoneySync())
		{
			sync.Money[key] = value;
		}
		await IBLogic_BuyGoods.GrantRewardAsync(player, sync, new List<uint> { g, d, p, l, count });
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "Item_Exchange", "{}", sync);
	}

	private static JObject? LoadExchange(uint g, uint d, uint p, uint l)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "exchange.json");
			if (!File.Exists(path))
			{
				return null;
			}
			JArray source = JArray.Parse(File.ReadAllText(path));
			return source.OfType<JObject>().FirstOrDefault((JObject x) => ReadInt(x["G"]) == g && ReadInt(x["D"]) == d && ReadInt(x["P"]) == p && ReadInt(x["L"]) == l);
		}
		catch
		{
		}
		return null;
	}

	private static int ReadInt(JToken? token)
	{
		if (token == null)
		{
			return 0;
		}
		if (token.Type == JTokenType.Integer)
		{
			return token.Value<int>();
		}
		if (token.Type == JTokenType.Float)
		{
			return (int)token.Value<decimal>();
		}
		int result;
		return int.TryParse(token.ToString(), out result) ? result : 0;
	}
}
