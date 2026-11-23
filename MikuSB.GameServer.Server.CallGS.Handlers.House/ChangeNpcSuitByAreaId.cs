using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ChangeNpcSuitByAreaId")]
public class ChangeNpcSuitByAreaId : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int npcId = HouseJson.NumField(root, "NpcId");
		int areaId = HouseJson.NumField(root, "AreaId");
		int suitId = HouseJson.NumField(root, "SuitId");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (npcId > 0 && areaId > 0)
		{
			uint[] slotSids = (from i in Enumerable.Range(24, 6)
				select (uint)(npcId * 50 + i)).ToArray();
			uint? chosenSid = null;
			uint[] array = slotSids;
			foreach (uint sid in array)
			{
				if ((HouseAttr.Read(connection.Player, sid) & 0xFFFF) == (uint)areaId)
				{
					chosenSid = sid;
					break;
				}
			}
			if (!chosenSid.HasValue)
			{
				uint[] array2 = slotSids;
				foreach (uint sid2 in array2)
				{
					if (HouseAttr.Read(connection.Player, sid2) == 0)
					{
						chosenSid = sid2;
						break;
					}
				}
			}
			chosenSid.GetValueOrDefault();
			if (!chosenSid.HasValue)
			{
				uint value = slotSids[0];
				chosenSid = value;
			}
			uint packed = (uint)(((suitId & 0xFFFF) << 16) | (areaId & 0xFFFF));
			await HouseAttr.SetAsync(connection, chosenSid.Value, packed, sync, deleteIfZero: false, sendImmediate: true);
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
	}
}
