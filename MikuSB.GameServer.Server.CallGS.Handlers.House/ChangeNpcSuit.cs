using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ChangeNpcSuit")]
public class ChangeNpcSuit : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int npcId = HouseJson.NumField(root, "NpcId");
			int suitId = HouseJson.NumField(root, "SuitId");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			await HouseAttr.SetAsync(connection, (uint)(npcId * 50 + 7), (uint)Math.Max(0, suitId), sync, deleteIfZero: false, sendImmediate: true);
			JsonObject rsp = new JsonObject
			{
				["NpcId"] = npcId,
				["SuitId"] = suitId,
				["npcSuit"] = new JsonObject
				{
					["NpcId"] = npcId,
					["SuitId"] = suitId
				},
				["FuncName"] = "ChangeNpcSuitSuccess"
			};
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
		}
	}
}
