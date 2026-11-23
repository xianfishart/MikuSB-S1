using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database.Account;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command;

public class CommandArg
{
	public string RawArg { get; } = "";

	public List<string> Args { get; } = new List<string>();

	public List<string> Attributes { get; } = new List<string>();

	public ICommandSender Sender { get; }

	public int TargetUid { get; set; } = 0;

	public Connection? Target { get; set; }

	public CommandArg(string rawArg, ICommandSender sender)
	{
		Sender = sender;
		RawArg = rawArg;
		string[] array = rawArg.Split(' ');
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				Args.Add(text);
			}
		}
	}

	public async ValueTask SendMsg(string msg)
	{
		await Sender.SendMsg(msg);
	}

	public int GetInt(int index)
	{
		if (Args.Count <= index)
		{
			return 0;
		}
		if (int.TryParse(Args[index], out var result))
		{
			return result;
		}
		return 0;
	}

	public async ValueTask<int?> GetOption(char pre, string def = "1")
	{
		string opStr = Args.FirstOrDefault((string x) => x[0] == pre)?.Substring(1) ?? def;
		if (!int.TryParse(opStr, out var op))
		{
			await SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
			return null;
		}
		return op;
	}

	public async ValueTask<bool> CheckArgCnt(int start, int? end = null)
	{
		end.GetValueOrDefault();
		if (!end.HasValue)
		{
			end = start;
		}
		if (Args.Count >= start && Args.Count <= end)
		{
			return true;
		}
		await SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
		return false;
	}

	public async ValueTask<bool> CheckTarget()
	{
		if (AccountData.GetAccountByUid(TargetUid) == null)
		{
			await SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
			return false;
		}
		return true;
	}

	public async ValueTask<bool> CheckOnlineTarget(bool sendMsg = true)
	{
		if (Target == null)
		{
			if (sendMsg)
			{
				await SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
			}
			return false;
		}
		return true;
	}
}
