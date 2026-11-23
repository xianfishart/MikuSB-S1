using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal static class VirCaptureAchievementClaim
{
	public static async Task<JsonArray?> TryClaim(PlayerInstance player, uint missionId, NtfSyncPlayer sync)
	{
		if (missionId == 0 || !GameData.VirCaptureMissionData.TryGetValue(missionId, out VirCaptureMissionExcel mission))
		{
			return null;
		}
		if (VirCaptureTaskStore.IsClaimed(player, missionId))
		{
			return null;
		}
		if (VirCaptureTaskStore.GetProgress(player, missionId) < mission.CheckValue)
		{
			return null;
		}
		List<List<uint>> rows = RewardText.Parse(mission.Rewards);
		if (rows.Count == 0)
		{
			return null;
		}
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		VirCaptureTaskStore.MarkClaimed(player, missionId, sync);
		return granted;
	}
}
