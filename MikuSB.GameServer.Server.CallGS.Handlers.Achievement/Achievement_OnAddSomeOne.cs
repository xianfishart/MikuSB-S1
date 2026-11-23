using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("Achievement_OnAddSomeOne")]
public class Achievement_OnAddSomeOne : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int type = ReadType(param);
		if (type > 0)
		{
			switch (type)
			{
			case 93:
				TaskEventService.AddByType(player, 93, 1);
				TaskEventService.RefreshSnapshot(player, 63);
				DailyPointService.Recompute(player);
				break;
			case 350:
				TaskProgressService.Ensure(player);
				DailyPointService.Recompute(player);
				break;
			default:
				TaskEventService.AddByType(player, type, 1);
				DailyPointService.Recompute(player);
				await Task.CompletedTask;
				break;
			}
		}
	}

	private static int ReadType(string param)
	{
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nType"];
			if (jsonNode is JsonValue jsonValue)
			{
				if (jsonValue.TryGetValue<int>(out var value))
				{
					return value;
				}
				if (jsonValue.TryGetValue<long>(out var value2))
				{
					return (int)value2;
				}
				if (jsonValue.TryGetValue<double>(out var value3))
				{
					return (int)value3;
				}
			}
			int result;
			return int.TryParse(jsonNode?.ToString(), out result) ? result : 0;
		}
		catch
		{
			return 0;
		}
	}
}
