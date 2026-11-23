using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Command;

public class ConsoleCommandSender(Logger logger) : ICommandSender
{
	public async ValueTask SendMsg(string msg)
	{
		logger.Info(msg);
		await Task.CompletedTask;
	}

	public int GetSender()
	{
		return 0;
	}
}
