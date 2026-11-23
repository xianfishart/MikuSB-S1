using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuel_ChangeFormation")]
public class DonoDuel_ChangeFormation : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject jsonObject = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint num = DonoDuelFormation.ReadUint(jsonObject["nType"]);
		(uint Start, uint End) tuple = DonoDuelFormation.Range(num);
		uint item = tuple.Start;
		uint item2 = tuple.End;
		List<uint> list = new List<uint>();
		if (jsonObject["tbFormation"] is JsonArray jsonArray)
		{
			foreach (JsonNode item3 in jsonArray)
			{
				uint num2 = DonoDuelFormation.ReadUint(item3);
				if (num2 != 0)
				{
					list.Add(num2);
				}
			}
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		for (uint num3 = item; num3 <= item2; num3++)
		{
			PlayerAttr orCreate = player.Attributes.GetOrCreate(185u, num3);
			if (orCreate.Val != 0)
			{
				orCreate.Val = 0u;
				player.Attributes.SyncTo(ntfSyncPlayer, orCreate);
			}
		}
		for (int i = 0; i < list.Count; i += 2)
		{
			uint num4 = item + (uint)(i / 2);
			if (num4 > item2)
			{
				break;
			}
			uint num5 = list[i];
			uint num6 = ((i + 1 < list.Count) ? list[i + 1] : 0u);
			PlayerAttr orCreate2 = player.Attributes.GetOrCreate(185u, num4);
			orCreate2.Val = (num5 << 16) | (num6 & 0xFFFF);
			player.Attributes.SyncTo(ntfSyncPlayer, orCreate2);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonArray jsonArray2 = new JsonArray();
		foreach (uint item4 in list)
		{
			jsonArray2.Add(item4);
		}
		return CallGSRouter.SendScript(connection, "DonoDuel_ChangeFormation", new JsonObject
		{
			["nType"] = num,
			["tbFormation"] = jsonArray2
		}.ToJsonString(), ntfSyncPlayer);
	}
}
