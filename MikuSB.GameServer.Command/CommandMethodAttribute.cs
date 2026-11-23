using System;

namespace MikuSB.GameServer.Command;

[AttributeUsage(AttributeTargets.Method)]
public class CommandMethodAttribute(string method) : Attribute
{
	public string MethodName { get; } = method;
}
