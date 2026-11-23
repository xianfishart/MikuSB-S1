using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

[CallGSApi("Online_FirstPop")]
public class Online_FirstPop : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		if (connection.Player == null || string.IsNullOrWhiteSpace(param))
		{
			return Task.CompletedTask;
		}
		int num;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			num = (jsonDocument.RootElement.TryGetProperty("nId", out var value) ? value.GetInt32() : 0);
		}
		catch
		{
			return Task.CompletedTask;
		}
		if (num <= 0)
		{
			return Task.CompletedTask;
		}
		uint sid = (uint)(num / 30 + 20);
		if (sid < 20 || sid > 29)
		{
			return Task.CompletedTask;
		}
		PlayerAttr playerAttr = connection.Player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 23 && x.Sid == sid);
		if (playerAttr == null)
		{
			connection.Player.Data.Attrs.Add(new PlayerAttr
			{
				Gid = 23u,
				Sid = sid,
				Val = 0u
			});
			playerAttr = connection.Player.Data.Attrs.First((PlayerAttr x) => x.Gid == 23 && x.Sid == sid);
		}
		playerAttr.Val |= (uint)(1 << num % 30);
		return Task.CompletedTask;
	}
}
