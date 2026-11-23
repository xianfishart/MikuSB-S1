using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("UpdateCharacterAttr")]
public class UpdateCharacterAttr : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int detail = HouseJson.NumField(root, "Detail");
		int attr = HouseJson.NumField(root, "Attr");
		int num = HouseJson.NumField(root, "Num");
		uint sid = (uint)(detail * 50 + attr);
		PlayerInstance player = connection.Player;
		await HouseProgressHelper.SetAttr(value: (player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid)?.Val ?? 0) + (uint)num, connection: connection, sid: sid);
	}
}
