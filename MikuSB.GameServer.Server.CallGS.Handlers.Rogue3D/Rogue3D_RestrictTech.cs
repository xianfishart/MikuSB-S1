using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue3D;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_RestrictTech")]
public class Rogue3D_RestrictTech : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint techId = 0u;
		uint restrict = 0u;
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			techId = (req?["nTechId"]?.GetValue<uint>()).GetValueOrDefault();
			restrict = (req?["nRestrict"]?.GetValue<uint>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (!Rogue3DTechHelper.TryGetScience(techId, out Rogue3DScienceExcel science))
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_RestrictTech", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (restrict != 0 && !Rogue3DTechHelper.IsUnlocked(player, science))
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_RestrictTech", "{\"sErr\":\"error.condition_limit\"}");
			return;
		}
		uint sid = Rogue3DTechHelper.GetRestrictionSid(techId);
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 124u,
				Sid = sid,
				Val = ((restrict != 0) ? 1u : 0u)
			};
			player.Data.Attrs.Add(attr);
		}
		else
		{
			attr.Val = ((restrict != 0) ? 1u : 0u);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(124u, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(124u, sid)] = attr.Val;
		JsonObject rsp = new JsonObject
		{
			["nTechId"] = techId,
			["nRestrict"] = restrict
		};
		await CallGSRouter.SendScript(connection, "Rogue3D_RestrictTech", rsp.ToJsonString(), sync);
	}
}
