using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SetPlayerRingInfo")]
public class SetPlayerRingInfo : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int ringId = HouseJson.NumField(root, "RingId");
			int ringPos = HouseJson.NumField(root, "RingPos");
			int ringOffset = HouseJson.NumField(root, "RingOffset");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (ringPos >= 1 && ringPos <= 10 && ringOffset >= 0 && ringOffset <= 2)
			{
				uint sid = (uint)(3174 + ringPos);
				uint prev = HouseAttr.Read(connection.Player, sid);
				int shift = ringOffset * 10;
				uint mask = (uint)(~(1023 << shift));
				uint next = (prev & mask) | (uint)((ringId & 0x3FF) << shift);
				await HouseAttr.SetAsync(connection, sid, next, sync, deleteIfZero: true, sendImmediate: true);
			}
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
