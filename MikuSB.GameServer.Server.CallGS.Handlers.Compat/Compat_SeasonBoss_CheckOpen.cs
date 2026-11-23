using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("SeasonBoss_CheckOpen")]
public class Compat_SeasonBoss_CheckOpen : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		bool force = false;
		try
		{
			JsonNode jsonNode = JsonNode.Parse(param);
			JsonNode n = default;
			int num;
			if (jsonNode is JsonObject obj)
			{
				n = obj["ForceEnter"];
				num = ((n != null) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				force = n.GetValue<bool>();
			}
		}
		catch
		{
		}
		string rsp = (force ? "{\"bForceEnter\":true}" : "{}");
		await CallGSRouter.SendScript(connection, "SeasonBoss_CheckOpen", rsp);
	}
}
