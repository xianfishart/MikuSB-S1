using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_SetLock")]
public class DonoDuel_SetLock : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_SetLock");

	private const uint LockBit = 0u;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject root = ParseObject(param);
		uint uniqueId = ReadUint(root?["nUniqueId"]);
		bool locked = ReadBool(root?["bLock"]);
		if (!DonoDuelBag.HasCard(player, uniqueId))
		{
			Log.Warn($"Donoduel lock request for unknown card {uniqueId} from uid={player.Uid}.");
			return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" });
		}
		uint flags = DonoDuelBag.GetFlags(player, uniqueId);
		flags = (locked ? (flags | 1) : (flags & 0xFFFFFFFEu));
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelBag.SetFlags(player, uniqueId, flags, sync);
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} {(locked ? "locked" : "unlocked")} by uid={player.Uid} (flags={flags}).");
		return CallGSResult.Ok(new JsonObject
		{
			["nUniqueId"] = uniqueId,
			["bLock"] = locked
		});
	}

	private static JsonObject? ParseObject(string param)
	{
		try
		{
			return string.IsNullOrWhiteSpace(param) ? null : (JsonNode.Parse(param) as JsonObject);
		}
		catch (Exception ex)
		{
			Log.Warn("Could not parse the Donoduel lock request: " + ex.Message);
			return null;
		}
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return 0u;
		}
		try
		{
			if (jsonValue.TryGetValue<uint>(out var value))
			{
				return value;
			}
			uint result;
			if (jsonValue.TryGetValue<string>(out string value2))
			{
				return uint.TryParse(value2.Trim(), out result) ? result : 0u;
			}
		}
		catch
		{
		}
		return 0u;
	}

	private static bool ReadBool(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return false;
		}
		try
		{
			if (jsonValue.TryGetValue<bool>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<uint>(out var value2))
			{
				return value2 != 0;
			}
			if (jsonValue.TryGetValue<string>(out string value3))
			{
				return value3.Equals("true", StringComparison.OrdinalIgnoreCase) || value3 == "1";
			}
		}
		catch
		{
		}
		return false;
	}
}
