using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("DLCLogic_CheckOpenAct")]
public class DLCLogic_CheckOpenAct : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		DateTime now = DateTime.Now;
		PlayerInstance player = connection.Player;
		uint requested = 0u;
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			int num;
			double d = default;
			if (jsonNode is JsonObject req)
			{
				JsonNode node = req["nId"];
				if (node != null && node is JsonValue value)
				{
					num = (value.TryGetValue<double>(out d) ? 1 : 0);
					goto IL_00d3;
				}
			}
			num = 0;
			goto IL_00d3;
			IL_00d3:
			if (num != 0 && d > 0.0)
			{
				requested = (uint)d;
			}
		}
		catch
		{
		}
		DlcActivityExcel act = null;
		if (requested != 0)
		{
			GameData.DlcActivityData.TryGetValue(requested, out act);
		}
		if (act == null)
		{
			act = GameData.DlcActivityData.GetValueOrDefault(DlcEraState.PickDefaultEraId(player, now));
		}
		if (act == null)
		{
			await CallGSRouter.SendScript(connection, "DLCLogic_CheckOpenAct", "{\"bOpen\":false}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DlcEraState.SetEra(player, act.Id, sync);
		JsonObject response = new JsonObject
		{
			["bOpen"] = true,
			["nId"] = act.Id,
			["nStartTime"] = ToUnixSeconds(DlcEraState.ParseConfigTime(act.EnterStartTime)),
			["nEndTime"] = ToUnixSeconds(DlcEraState.ParseConfigTime(act.CloseEndTime))
		};
		await CallGSRouter.SendScript(connection, "DLCLogic_CheckOpenAct", response.ToJsonString(), sync);
		try
		{
			DlcWindowPush.PushSoon(connection, "check-open");
		}
		catch
		{
		}
	}

	private static long ToUnixSeconds(DateTime? value)
	{
		return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0;
	}
}
