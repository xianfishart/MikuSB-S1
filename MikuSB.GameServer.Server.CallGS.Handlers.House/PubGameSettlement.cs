using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameSettlement")]
public class PubGameSettlement : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool isWin = root["bWin"]?.GetValue<bool>() ?? false;
		bool guide = root["bGuide"]?.GetValue<bool>() ?? false;
		uint rank = (uint)Math.Max(0, PubAttr.Num(root["nRank"]));
		uint addExp = PubAttr.ResolveExp(root, guide);
		if (addExp != 0)
		{
			uint sid = PubAttr.Sid(1u);
			await PubAttr.SetAttrAsync(connection, sid, player.GetAttr(101u, sid) + addExp, sync);
		}
		await PubAttr.UpdateHistoryAsync(connection, player, root, isWin, rank, sync);
		JsonObject rsp = new JsonObject();
		foreach (var (key, jsonNode2) in root)
		{
			rsp[key] = jsonNode2?.DeepClone();
		}
		rsp["FuncName"] = "PubGameSettlement";
		rsp["bWin"] = isWin;
		rsp["nRank"] = rank;
		rsp["nExp"] = (int)addExp;
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), sync);
	}
}
