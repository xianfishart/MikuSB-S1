using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_CheckOpenAct")]
public class Rogue3D_CheckOpenAct : ICallGSHandler
{
	private static JsonArray? _acts;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		try
		{
			PlayerInstance player = connection.Player;
			if (player != null)
			{
				NtfSyncPlayer stateSync = Rogue3DStateHelper.EnsureUnlockState(player);
				foreach (KeyValuePair<uint, uint> kv in stateSync.Custom)
				{
					sync.Custom[kv.Key] = kv.Value;
				}
				Rogue3DStateHelper.EnsureUavDefaults(player, sync);
				Rogue3DProgress.EnsureInitialized(player, sync);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine("[Rogue3D_CheckOpenAct] EnsureUnlockState failed: " + ex2.Message);
		}
		JsonArray actIds = GetOpenActIds();
		int[] array = new int[3] { 1, 2, 3 };
		foreach (int id in array)
		{
			if (!actIds.Any((JsonNode x) => x != null && x.GetValue<int>() == id))
			{
				actIds.Add(id);
			}
		}
		JsonObject jsonObject = new JsonObject
		{
			["bOpen"] = true,
			["listActId"] = actIds
		};
		InlineArray7<JsonNode> buffer = default;
		buffer[0] = 1;
		buffer[1] = 2;
		buffer[2] = 3;
		buffer[3] = 4;
		buffer[4] = 5;
		buffer[5] = 6;
		buffer[6] = 7;
		jsonObject["listSeasonId"] = new JsonArray(buffer);
		jsonObject["nSeasonId"] = 7;
		JsonObject response = jsonObject;
		await CallGSRouter.SendScript(connection, "Rogue3D_CheckOpenAct", response.ToJsonString(), sync);
	}

	private static JsonArray GetOpenActIds()
	{
		JsonArray jsonArray = new JsonArray();
		DateTime now = DateTime.Now;
		foreach (JsonNode item in LoadActs())
		{
			if (!(item is JsonObject jsonObject) || !uint.TryParse(jsonObject["ID"]?.ToString(), out var result))
			{
				continue;
			}
			DateTime? dateTime = ParseTime(jsonObject["StartTime"]?.ToString());
			DateTime? dateTime2 = ParseTime(jsonObject["EndTime"]?.ToString());
			if (dateTime.HasValue && dateTime2.HasValue && dateTime <= now)
			{
				DateTime value = now;
				DateTime? dateTime3 = dateTime2;
				if (value < dateTime3)
				{
					jsonArray.Add((int)result);
				}
			}
		}
		if (jsonArray.Count == 0)
		{
			jsonArray.Add(1);
		}
		return jsonArray;
	}

	private static DateTime? ParseTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string s = raw.Trim().Trim(new char[2] { '[', ']' });
		DateTime result;
		return DateTime.TryParseExact(s, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}

	private static JsonArray LoadActs()
	{
		if (_acts != null)
		{
			return _acts;
		}
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "rogue3d";
			buffer[4] = "server_04_timelist.json";
			string path = Path.Combine(buffer);
			_acts = ((!File.Exists(path)) ? new JsonArray() : JsonNode.Parse(File.ReadAllText(path))?.AsArray());
		}
		catch
		{
			_acts = new JsonArray();
		}
		return _acts ?? new JsonArray();
	}
}
