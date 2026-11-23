using System.Threading.Tasks;

namespace MikuSB.GameServer.Command;

public interface ICommandSender
{
	ValueTask SendMsg(string msg);

	int GetSender();
}
