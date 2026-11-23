using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3DScoreModel_BuyBuff")]
public class Rogue3DScoreModel_BuyBuff : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int buffId = 0;
		try
		{
			buffId = (JsonNode.Parse(param)?["nBuffId"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (buffId <= 0)
		{
			await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_BuyBuff", "{\"sErr\":\"tip.invalid\"}");
			return;
		}
		int coinType = 0;
		int cost = 0;
		try
		{
			InlineArray6<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "rogue3d";
			buffer[4] = "score_model";
			buffer[5] = "score_buff.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path)).AsArray())
				{
					JsonObject row = node as JsonObject;
					if (row == null || (row["ID"]?.GetValue<int>() ?? 0) != buffId)
					{
						continue;
					}
					JsonNode jsonNode = row["UnlockCoin"];
					JsonArray coin = jsonNode as JsonArray;
					if (coin != null && coin.Count >= 2)
					{
						coinType = coin[0]?.GetValue<int>() ?? 0;
						cost = coin[1]?.GetValue<int>() ?? 0;
					}
					break;
				}
			}
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (cost > 0 && coinType > 0)
		{
			uint sid = (uint)(coinType * 2 + 1);
			PlayerAttr money = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sid);
			if (money == null || money.Val < cost)
			{
				await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_BuyBuff", "{\"sErr\":\"rogue3.massage_SocreCoinNotEnough\"}");
				return;
			}
			money.Val -= (uint)cost;
			foreach (var (key, value) in player.BuildMoneySync())
			{
				sync.Money[key] = value;
			}
		}
		int off = 2441 + buffId / 30;
		int pos = buffId % 30;
		PlayerAttr flag = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == (uint)off);
		if (flag == null)
		{
			flag = new PlayerAttr
			{
				Gid = 124u,
				Sid = (uint)off,
				Val = 0u
			};
			player.Data.Attrs.Add(flag);
		}
		flag.Val |= (uint)(1 << pos);
		sync.Custom[player.ToPackedAttrKey(124u, (uint)off)] = flag.Val;
		sync.Custom[player.ToShiftedAttrKey(124u, (uint)off)] = flag.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["bSuccess"] = true,
			["nBuffId"] = buffId
		};
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_BuyBuff", rsp.ToJsonString(), sync);
	}
}
