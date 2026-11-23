using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("GirlSkin_ChangeSkinType2")]
public class Compat_GirlSkin_ChangeSkinType2 : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
