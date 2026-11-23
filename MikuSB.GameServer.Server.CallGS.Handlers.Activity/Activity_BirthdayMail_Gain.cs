using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_BirthdayMail_Gain")]
public class Activity_BirthdayMail_Gain : CallGSHandler
{
	private const uint Gained = 1u;

	private const uint MinYear = 2020u;

	private const uint MaxYear = 2100u;

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		uint num = ReadYear(param);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject jsonObject = new JsonObject();
		if (num < 2020 || num > 2100)
		{
			Logger.GetByClassName().Warn($"Activity_BirthdayMail_Gain without a usable year (param='{param}') for uid={player.Uid}.");
			return Task.FromResult(CallGSResult.Ok(jsonObject.ToJsonString(), sync, "Activity_BirthdayMail_Gain"));
		}
		PlayerAttr orCreate = player.Attributes.GetOrCreate(155u, num);
		if (orCreate.Val != 1)
		{
			orCreate.Val = 1u;
			player.Attributes.SyncTo(sync, orCreate);
		}
		jsonObject["nYear"] = num;
		Logger.GetByClassName().Info($"Birthday mail {num} marked as gained for uid={player.Uid} (attr {155u}/{num}={orCreate.Val}).");
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(jsonObject.ToJsonString(), sync, "Activity_BirthdayMail_Gain"));
	}

	private static uint ReadYear(string param)
	{
		if (string.IsNullOrWhiteSpace(param))
		{
			return 0u;
		}
		string text = param.Trim();
		if (text.StartsWith('{'))
		{
			try
			{
				if (JsonNode.Parse(text) is JsonObject jsonObject)
				{
					string[] array = new string[4] { "nYear", "year", "Year", "nId" };
					foreach (string propertyName in array)
					{
						string s = jsonObject[propertyName]?.ToString();
						if (uint.TryParse(s, out var result) && result != 0)
						{
							return result;
						}
					}
				}
			}
			catch (JsonException)
			{
				return 0u;
			}
			return 0u;
		}
		text = text.Trim('"');
		uint result2;
		return uint.TryParse(text, out result2) ? result2 : 0u;
	}
}
