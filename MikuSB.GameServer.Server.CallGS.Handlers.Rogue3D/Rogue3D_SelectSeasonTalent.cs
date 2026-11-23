using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SelectSeasonTalent")]
public class Rogue3D_SelectSeasonTalent : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint SeasonTalentIdSid = 1007u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SelectSeasonTalentParam req = JsonSerializer.Deserialize<SelectSeasonTalentParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_SelectSeasonTalent", "{}");
			return;
		}
		PlayerInstance player = connection.Player;
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == 1007);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 124u,
				Sid = 1007u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = req.TalentId;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(124u, 1007u)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(124u, 1007u)] = attr.Val;
		await CallGSRouter.SendScript(connection, "Rogue3D_SelectSeasonTalent", "{}", sync);
	}
}
