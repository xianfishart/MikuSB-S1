using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_GetRoleFestivalLevelReward")]
public class Activity_GetRoleFestivalLevelReward : CallGSHandler
{
	private static readonly Logger Log = new Logger("Activity_GetRoleFestivalLevelReward");

	private const string SuccessApi = "GetRoleFestivalLevelRewardSuccess";

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject request = ParseObject(param);
		uint actId = ReadUint(request?["nId"]);
		uint messageNum = ReadUint(request?["nMessageNum"]);
		uint currentAct = RoleFestivalService.GetActId(player);
		if (actId == 0 || actId != currentAct || messageNum == 0)
		{
			Log.Warn($"Role festival reward request act={actId} (current {currentAct}) tier={messageNum} from uid={player.Uid} rejected.");
			return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, "GetRoleFestivalLevelRewardSuccess");
		}
		JsonObject response = new JsonObject
		{
			["nId"] = actId,
			["nMessageNum"] = messageNum,
			["tbRewards"] = new JsonArray()
		};
		if (player.Attributes.GetValue(72u, RoleFestivalService.RewardSid(messageNum)) == 1)
		{
			Log.Info($"Role festival tier {messageNum} already claimed by uid={player.Uid}.");
			return CallGSResult.Ok(response.ToJsonString(), null, "GetRoleFestivalLevelRewardSuccess");
		}
		RoleFestivalRewardExcel row = RoleFestivalService.FindReward(actId, messageNum);
		if (row == null)
		{
			Log.Warn($"Role festival act {actId} has no reward row for tier {messageNum} (uid={player.Uid}).");
			return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, "GetRoleFestivalLevelRewardSuccess");
		}
		int read = RoleFestivalService.CountRead(player, actId);
		if (read < messageNum)
		{
			Log.Warn($"Role festival tier {messageNum} needs {messageNum} read letters, uid={player.Uid} has {read}; refusing to grant.");
			return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" }.ToJsonString(), null, "GetRoleFestivalLevelRewardSuccess");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<uint[]> rows = DlcRuntimeOverrides.ReadRewardRows(row.Rewards);
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		PlayerAttr claimAttr = player.Attributes.Set(72u, RoleFestivalService.RewardSid(messageNum), 1u);
		player.Attributes.SyncTo(sync, claimAttr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		response["tbRewards"] = granted;
		Log.Info($"Role festival act {actId} tier {messageNum} claimed by uid={player.Uid}: {rows.Count} configured row(s), {granted.Count} granted, {read} letter(s) read.");
		return CallGSResult.Ok(response.ToJsonString(), sync, "GetRoleFestivalLevelRewardSuccess");
	}

	private static JsonObject? ParseObject(string param)
	{
		try
		{
			return string.IsNullOrWhiteSpace(param) ? null : (JsonNode.Parse(param) as JsonObject);
		}
		catch (Exception ex)
		{
			Log.Warn("Could not parse the role festival reward request: " + ex.Message);
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
