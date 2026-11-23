using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("Cash_Exchange")]
public class Cash_Exchange : ICallGSHandler
{
	private static readonly Logger Log = new Logger("Cash_Exchange");
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonNode req = JsonNode.Parse(param);
		int source = (req?["nExhcangeCash"]?.GetValue<int>()).GetValueOrDefault();
		int cost = (req?["nCost"]?.GetValue<int>()).GetValueOrDefault();
		int target = (req?["nExhcangTarget"]?.GetValue<int>()).GetValueOrDefault();
		if (source <= 0 || cost <= 0 || target <= 0)
		{
			await CallGSRouter.SendScript(connection, "Cash_Exchange", "{\"sErr\":\"tip.exchange_invaild\"}");
			return;
		}
		uint targetCount = ((target == 3) ? ((uint)(LoadRows("silver_exchange.json").FirstOrDefault((JsonObject x) => (x["Cost"]?.GetValue<int>() ?? 0) == cost)?["Silver"]?.GetValue<int>()).GetValueOrDefault()) : ((target == 4) ? ((uint)(LoadRows("vigor_exchange.json").FirstOrDefault((JsonObject x) => (x["Cost"]?.GetValue<int>() ?? 0) == cost)?["Vigor"]?.GetValue<int>()).GetValueOrDefault()) : 0u));
		if (targetCount == 0)
		{
			await CallGSRouter.SendScript(connection, "Cash_Exchange", "{\"sErr\":\"tip.exchange_invaild\"}");
			return;
		}
		uint sourceSid = ((source == 1) ? 3u : ((uint)(source * 2 + 1)));
		int targetSid = target * 2 + 1;
		PlayerAttr sourceAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sourceSid);
		if (sourceAttr == null || sourceAttr.Val < cost)
		{
			await CallGSRouter.SendScript(connection, "Cash_Exchange", "{\"sErr\":\"tip.cash_not_enough\"}");
			return;
		}
		sourceAttr.Val -= (uint)cost;
		PlayerAttr targetAttr = null;
		if (target == 4)
		{
			// 数据金买体力允许溢出上限（第 229 节，用户要求）：
			// 不再走 VigorRecovery.AddCapped —— 240 满时买 60 就变成 300，
			// 恢复逻辑（VigorRecovery.Apply）也只补到上限、不会把它削回去。
			player.Data.Vigor += targetCount;
		}
		else
		{
			targetAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == (uint)targetSid);
			if (targetAttr == null)
			{
				targetAttr = new PlayerAttr
				{
					Gid = 1u,
					Sid = (uint)targetSid,
					Val = 0u
				};
				player.Data.Attrs.Add(targetAttr);
			}
			targetAttr.Val += targetCount;
		}
		PlayerAttr limitAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 70 && x.Sid == (uint)target);
		if (limitAttr == null)
		{
			limitAttr = new PlayerAttr
			{
				Gid = 70u,
				Sid = (uint)target,
				Val = 0u
			};
			player.Data.Attrs.Add(limitAttr);
		}
		limitAttr.Val++;
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr[] syncAttrs = ((targetAttr != null) ? new PlayerAttr[3] { sourceAttr, targetAttr, limitAttr } : new PlayerAttr[2] { sourceAttr, limitAttr });
		PlayerAttr[] array = syncAttrs;
		foreach (PlayerAttr attr in array)
		{
			sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		}
		if (target == 4)
		{
			// 体力不是 Attrs 里的货币，它走 Data.Vigor；上面那段只塞了 sourceAttr（数据金）
			// 和 limitAttr（Gid70/Sid4 兑换次数），体力本身没进同步包，
			// 于是库里已经加上了、客户端 HUD 还是旧值（第 228 节）。
			// 与 ChapterVigor / PlayerInstance.AddCurrency(4) 保持同一口径：Core[2]=体力、Core[4]=恢复锚点。
			sync.Core[2u] = player.Data.Vigor;
			sync.Core[4u] = player.Attributes.GetValue(9900u, 1u);
		}
		if (source == 1)
		{
			foreach (var (key, value) in player.BuildMoneySync())
			{
				sync.Money[key] = value;
			}
		}
		JsonObject response = new JsonObject
		{
			["nCashType"] = target,
			["nCount"] = (int)targetCount
		};
		Log.Info($"uid={player.Uid} exchange source={source} cost={cost} target={target} count={targetCount} vigor={player.Data.Vigor}/{VigorRecovery.MaxOf(player)}");
		await CallGSRouter.SendScript(connection, "Cash_Exchange", response.ToJsonString(), sync);
	}

	private static List<JsonObject> LoadRows(string fileName)
	{
		List<JsonObject> list = new List<JsonObject>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "cash", fileName);
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				if (item2 is JsonObject item)
				{
					list.Add(item);
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
