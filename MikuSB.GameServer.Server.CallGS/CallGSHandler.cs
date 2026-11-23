using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS;

public abstract class CallGSHandler : ICallGSHandler
{
	protected string Api { get; }

	protected CallGSHandler()
	{
		Api = GetType().GetCustomAttribute<CallGSApiAttribute>()?.Api ?? throw new InvalidOperationException("CallGSApiAttribute is missing on " + GetType().Name + ".");
	}

	protected abstract Task<CallGSResult> HandleAsync(CallGSContext context, string param);

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await SendResultAsync(connection, await HandleAsync(new CallGSContext
		{
			Connection = connection,
			Player = connection.Player,
			SequenceNumber = seqNo,
			RawParam = param
		}, param));
	}

	protected static TRequest? Deserialize<TRequest>(string param)
	{
		return JsonSerializer.Deserialize<TRequest>(param, CallGSJson.Options);
	}

	protected static CallGSResult BadParam()
	{
		return CallGSResult.Error("error.BadParam");
	}

	private Task SendResultAsync(Connection connection, CallGSResult result)
	{
		if (!result.SendResponse)
		{
			return Task.CompletedTask;
		}
		return CallGSRouter.SendScript(connection, result.Api ?? Api, result.Argument, result.Sync);
	}
}
public abstract class CallGSHandler<TRequest> : CallGSHandler where TRequest : class
{
	protected sealed override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		string text = (string.IsNullOrWhiteSpace(param) ? "{}" : param.Trim());
		if (text == "[]")
		{
			text = "{}";
		}
		TRequest val = CallGSHandler.Deserialize<TRequest>(text);
		if (val == null)
		{
			return Task.FromResult(CallGSHandler.BadParam());
		}
		return HandleAsync(context, val);
	}

	protected abstract Task<CallGSResult> HandleAsync(CallGSContext context, TRequest request);
}
