using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public interface IHouseFuncHandler
{
	Task Handle(Connection connection, string param);
}
