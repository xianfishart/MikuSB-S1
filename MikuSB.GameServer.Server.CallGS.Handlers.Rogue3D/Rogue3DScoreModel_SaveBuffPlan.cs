using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3DScoreModel_SaveBuffPlan")]
public class Rogue3DScoreModel_SaveBuffPlan : ICallGSHandler
{
	private const uint StrGroupId = 56u;

	private const uint BuffPlanSidStart = 7u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int index = 0;
		JsonObject plan = new JsonObject();
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			index = (req?["nIndex"]?.GetValue<int>()).GetValueOrDefault();
			plan["P"] = req?["tbPlayerBuff"]?.DeepClone() ?? new JsonArray();
			plan["B"] = req?["tbCommonBuff"]?.DeepClone() ?? new JsonArray();
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null && index >= 1 && index <= 6)
		{
			uint sid = (uint)(7 + index - 1);
			string json = plan.ToJsonString();
			player.SetStrAttr(56u, sid, json);
			sync.CustomStr[player.ToShiftedAttrKey(56u, sid)] = json;
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_SaveBuffPlan", new JsonObject
		{
			["bSuccess"] = true,
			["nIndex"] = index
		}.ToJsonString(), sync);
	}
}
