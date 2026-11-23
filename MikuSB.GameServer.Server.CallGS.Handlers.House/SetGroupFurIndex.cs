using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SetGroupFurIndex")]
public class SetGroupFurIndex : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int areaId = HouseJson.NumField(root, "AreaId");
			int groupId = HouseJson.NumField(root, "GroupId");
			int index = HouseJson.NumField(root, "Index");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (areaId > 0 && groupId >= 1 && groupId <= 10)
			{
				uint sid = (uint)(areaId * 50 + 20);
				uint prev = HouseAttr.Read(connection.Player, sid);
				int shift = (groupId - 1) * 3;
				uint mask = (uint)(~(7 << shift));
				uint next = (prev & mask) | (uint)((index & 7) << shift);
				await HouseAttr.SetAsync(connection, sid, next, sync, deleteIfZero: false, sendImmediate: true);
			}
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
