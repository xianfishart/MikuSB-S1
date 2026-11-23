using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SelectTalent")]
public class Rogue3D_SelectTalent : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint TalentIdSid = 7u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SelectTalentParam req = JsonSerializer.Deserialize<SelectTalentParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_SelectTalent", "{}");
			return;
		}
		PlayerInstance player = connection.Player;
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == 7);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 124u,
				Sid = 7u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = req.TalentId;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(124u, 7u)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(124u, 7u)] = attr.Val;
		await CallGSRouter.SendScript(connection, "Rogue3D_SelectTalent", "{}", sync);
	}
}
