using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("GuideLogic_SetBeginner")]
public class GuideLogic_SetBeginner : ICallGSHandler
{
	private const uint BeginnerGid = 187u;

	private const uint BeginnerSid = 1u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint type = 1u;
		try
		{
			JsonNode node = JsonNode.Parse(param);
			if (node?["nType"] != null && uint.TryParse(node["nType"].ToString(), out var parsed))
			{
				type = parsed;
			}
		}
		catch
		{
		}
		if (type != 2)
		{
			type = 1u;
		}
		player.SetAttr(187u, 1u, type);
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(187u, 1u)] = type;
		sync.Custom[player.ToShiftedAttrKey(187u, 1u)] = type;
		await CallGSRouter.SendScript(connection, "GuideLogic_SetBeginner", "{}", sync);
	}
}
