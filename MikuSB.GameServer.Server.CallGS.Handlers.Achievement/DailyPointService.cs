using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

internal static class DailyPointService
{
	public const uint GroupId = 8u;

	public const uint PointSid = 1u;

	public static uint Recompute(PlayerInstance player, NtfSyncPlayer? sync = null)
	{
		uint num = 0u;
		foreach (AchievementExcel value in GameData.AchievementData.Values)
		{
			if (value.Group == 1 && AchievementClaimService.IsFinished(player, 2u, value.Id, value.CheckValue))
			{
				num += value.AchievementPoint;
			}
		}
		PlayerAttr orCreate = player.Attributes.GetOrCreate(8u, 1u);
		if (orCreate.Val != num)
		{
			orCreate.Val = num;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
		}
		return num;
	}
}
