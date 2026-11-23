using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("account", "Game.Command.Account.Desc", "Game.Command.Account.Usage", new string[] { }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandAccount : ICommands
{
	[CommandMethod("create")]
	public async ValueTask Create(CommandArg arg)
	{
		if (!(await arg.CheckArgCnt(2)))
		{
			return;
		}
		string email = arg.Args[0].Trim();
		if (!int.TryParse(arg.Args[1], out var uid) || uid <= 0)
		{
			await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
			return;
		}
		try
		{
			AccountData account = AccountData.CreateAccount(email, uid, "");
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.Created", account.Username, account.Uid.ToString()));
		}
		catch (InvalidOperationException ex)
		{
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.CreateFailed", ex.Message));
		}
		catch (ArgumentException ex2)
		{
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.CreateFailed", ex2.Message));
		}
	}

	[CommandMethod("delete")]
	public async ValueTask Delete(CommandArg arg)
	{
		if (!(await arg.CheckArgCnt(1)))
		{
			return;
		}
		string identifier = arg.Args[0].Trim();
		AccountData account = ((int.TryParse(identifier, out var uid) && uid > 0) ? AccountData.GetAccountByUid(uid) : AccountData.GetAccountByUserName(identifier));
		if (account == null)
		{
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.NotFound", identifier));
			return;
		}
		try
		{
			if (Listener.GetActiveConnection(account.Uid) != null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Account.DeleteOnline", account.Username, account.Uid.ToString()));
				return;
			}
			AccountData.DeleteAccount(account.Uid);
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.Deleted", account.Username, account.Uid.ToString()));
		}
		catch (Exception ex)
		{
			await arg.SendMsg(I18NManager.Translate("Game.Command.Account.DeleteFailed", ex.Message));
		}
	}

	[CommandMethod("list")]
	public async ValueTask List(CommandArg arg)
	{
		List<AccountData> accounts = DatabaseHelper.GetAllInstance<AccountData>()?.OrderBy((AccountData accountData) => accountData.Uid).ToList();
		if (accounts == null || accounts.Count == 0)
		{
			await arg.SendMsg("No accounts found.");
			return;
		}
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("Accounts:");
		foreach (AccountData account in accounts)
		{
			StringBuilder stringBuilder = builder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder);
			handler.AppendFormatted(account.Username);
			handler.AppendLiteral(" -> UID ");
			handler.AppendFormatted(account.Uid);
			stringBuilder.AppendLine(ref handler);
		}
		await arg.SendMsg(builder.ToString().TrimEnd());
	}
}
