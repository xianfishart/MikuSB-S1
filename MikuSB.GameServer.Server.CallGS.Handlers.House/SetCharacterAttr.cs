using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SetCharacterAttr")]
public class SetCharacterAttr : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int detail = HouseJson.NumField(root, "Detail");
		int attr = HouseJson.NumField(root, "Attr");
		int num = HouseJson.NumField(root, "Num");
		uint sid = (uint)(detail * 50 + attr);
		await HouseProgressHelper.SetAttr(connection, sid, (uint)num);
	}
}
