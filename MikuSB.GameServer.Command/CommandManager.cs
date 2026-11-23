using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MikuSB.Database.Account;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;
using MikuSB.TcpSharp;
using MikuSB.Util;

namespace MikuSB.GameServer.Command;

public class CommandManager
{
	public static Logger Logger { get; } = new Logger("CommandManager");

	public static Dictionary<string, ICommands> Commands { get; } = new Dictionary<string, ICommands>();

	public static Dictionary<string, CommandInfoAttribute> CommandInfo { get; } = new Dictionary<string, CommandInfoAttribute>();

	public static Dictionary<string, string> CommandAlias { get; } = new Dictionary<string, string>();

	public static void RegisterCommands()
	{
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types)
		{
			if (typeof(ICommands).IsAssignableFrom(type) && !type.IsAbstract)
			{
				RegisterCommand(type);
			}
		}
		Logger.Info(I18NManager.Translate("Server.ServerInfo.RegisterItem", Commands.Count.ToString(), I18NManager.Translate("Word.Command")));
	}

	public static void RegisterCommand(Type type)
	{
		CommandInfoAttribute customAttribute = type.GetCustomAttribute<CommandInfoAttribute>();
		if (customAttribute == null)
		{
			return;
		}
		object obj = Activator.CreateInstance(type);
		if (obj is ICommands value)
		{
			Commands.Add(customAttribute.Name, value);
			CommandInfo.Add(customAttribute.Name, customAttribute);
			string[] alias = customAttribute.Alias;
			foreach (string key in alias)
			{
				CommandAlias.Add(key, customAttribute.Name);
			}
		}
	}

	public static async void HandleCommand(string input, ICommandSender sender)
	{
		try
		{
			CommandArg argInfo = new CommandArg(input, sender);
			int target = sender.GetSender();
			foreach (string arg in argInfo.Args.ToList())
			{
				switch (arg[0])
				{
				case '-':
					argInfo.Attributes.Add(arg.Substring(1));
					break;
				case '@':
					int.TryParse(arg.Substring(1), out target);
					argInfo.Args.Remove(arg);
					break;
				}
			}
			argInfo.TargetUid = target;
			SocketConnection socketConnection = SocketListener.Connections.Values.ToList().Find((SocketConnection item) =>
			{
				Connection obj = item as Connection;
				return obj != null && obj.Player?.Uid == target;
			});
			if (socketConnection is Connection con)
			{
				argInfo.Target = con;
			}
			string cmdName = argInfo.Args[0];
			if (CommandAlias.TryGetValue(cmdName, out string fullName))
			{
				cmdName = fullName;
			}
			if (!Commands.TryGetValue(cmdName, out ICommands command))
			{
				await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.CommandNotFound"));
				return;
			}
			argInfo.Args.RemoveAt(0);
			CommandInfoAttribute cmdInfo = CommandInfo[cmdName];
			if (!AccountData.HasPerm(cmdInfo.Perm, sender.GetSender()))
			{
				await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
				return;
			}
			if (argInfo.Target?.Player?.Uid != sender.GetSender() && !AccountData.HasPerm(new PermEnum[1] { PermEnum.Other }, sender.GetSender()))
			{
				await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
				return;
			}
			bool isFound = false;
			MethodInfo[] methods = command.GetType().GetMethods();
			foreach (MethodInfo methodInfo in methods)
			{
				CommandMethodAttribute attr = methodInfo.GetCustomAttribute<CommandMethodAttribute>();
				if (attr != null && argInfo.Args.Count > 0 && attr.MethodName == argInfo.Args[0])
				{
					argInfo.Args.RemoveAt(0);
					isFound = true;
					methodInfo.Invoke(command, new object[1] { argInfo });
					break;
				}
			}
			if (isFound)
			{
				return;
			}
			MethodInfo[] methods2 = command.GetType().GetMethods();
			foreach (MethodInfo methodInfo2 in methods2)
			{
				CommandDefaultAttribute attr2 = methodInfo2.GetCustomAttribute<CommandDefaultAttribute>();
				if (attr2 != null)
				{
					isFound = true;
					methodInfo2.Invoke(command, new object[1] { argInfo });
					break;
				}
			}
			if (!isFound)
			{
				await sender.SendMsg(I18NManager.Translate(cmdInfo.Usage));
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Logger.Error(I18NManager.Translate("Game.Command.Notice.InternalError", ex2.ToString()));
		}
	}
}
