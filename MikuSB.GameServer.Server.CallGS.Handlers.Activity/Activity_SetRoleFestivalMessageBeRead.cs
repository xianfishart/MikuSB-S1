using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_SetRoleFestivalMessageBeRead")]
public class Activity_SetRoleFestivalMessageBeRead : CallGSHandler
{
	private static readonly Logger Log = new Logger("Activity_SetRoleFestivalMessageBeRead");

	private const string SuccessApi = "SetRoleFestivalMessageBeReadSuccess";

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		uint npcId = ReadUint(ParseObject(param)?["nGirlId"]);
		uint actId = RoleFestivalService.GetActId(player);
		if (npcId == 0 || actId == 0 || !RoleFestivalService.IsMessageOfAct(actId, npcId))
		{
			Log.Warn($"Role festival read request for npc {npcId} of act {actId} from uid={player.Uid} " + "(unknown letter); answering without a state change.");
			return CallGSResult.Ok("{}", null, "SetRoleFestivalMessageBeReadSuccess");
		}
		JsonObject response = new JsonObject { ["nGirlId"] = npcId };
		if (player.Attributes.GetValue(72u, RoleFestivalService.ReadSid(npcId)) == 1)
		{
			return CallGSResult.Ok(response.ToJsonString(), null, "SetRoleFestivalMessageBeReadSuccess");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(72u, RoleFestivalService.ReadSid(npcId), 1u);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"Role festival letter {npcId} of act {actId} read by uid={player.Uid} (read {RoleFestivalService.CountRead(player, actId)}).");
		return CallGSResult.Ok(response.ToJsonString(), sync, "SetRoleFestivalMessageBeReadSuccess");
	}

	private static JsonObject? ParseObject(string param)
	{
		try
		{
			return string.IsNullOrWhiteSpace(param) ? null : (JsonNode.Parse(param) as JsonObject);
		}
		catch (Exception ex)
		{
			Log.Warn("Could not parse the role festival read request: " + ex.Message);
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
}
