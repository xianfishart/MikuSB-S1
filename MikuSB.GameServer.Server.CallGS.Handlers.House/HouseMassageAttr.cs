using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseMassageAttr
{
	internal const uint Gid = 101u;

	internal const uint InfoStart = 11000u;

	internal const uint GirlAttrCount = 50u;

	internal const int HardLevelFiveStarsReward = 38;

	internal const int MaxHardLevelUnLock = 39;

	internal const int HardLevelMaxStarsBegin = 24;

	internal const int MassageHistoryStarsBegin = 31;

	internal const int TimesBitShift = 3;

	internal const int TimesBitCount = 30;

	private const uint TimesLowMask = 7u;

	internal static uint Sid(int girlId, int offset)
	{
		return (uint)(11000 + girlId * 50 + offset);
	}

	internal static uint Read(PlayerInstance player, int girlId, int offset)
	{
		return player.Attributes.GetValue(101u, Sid(girlId, offset));
	}

	internal static void Set(PlayerInstance player, NtfSyncPlayer sync, int girlId, int offset, uint value)
	{
		uint sid = Sid(girlId, offset);
		player.Attributes.Set(101u, sid, value);
		player.Attributes.SyncTo(sync, 101u, sid, value);
	}

	internal static uint ReadTimes(PlayerInstance player, int girlId, int timesOffset)
	{
		return Read(player, girlId, timesOffset) >> 3;
	}

	internal static void SetTimes(PlayerInstance player, NtfSyncPlayer sync, int girlId, int timesOffset, uint times)
	{
		uint num = Read(player, girlId, timesOffset);
		uint value = (num & 7) | ((times & 0x3FFFFFFF) << 3);
		Set(player, sync, girlId, timesOffset, value);
	}

	internal static int TimesOffset(int hardLevel)
	{
		return 24 + hardLevel;
	}

	internal static int StarsOffset(int hardLevel)
	{
		return 31 + hardLevel;
	}

	/// <summary>
	/// 2026-11-13 新增（修复记录第 271 节）：存档里是否已经有任何一次神经调理的星级记录。
	/// 星级 offset = 31 + 难度（1~3）→ 32/33/34，值 1~5。
	/// 用于给修复前已经调理过、但 20702 成就没被标记的老存档补进度。
	/// </summary>
	internal static bool HasAnyStars(PlayerInstance player)
	{
		foreach (PlayerAttr attr in player.Data.Attrs)
		{
			if (attr.Gid != Gid || attr.Sid < InfoStart || attr.Sid > InfoStart + 50u * 40u)
			{
				continue;
			}
			int offset = (int)((attr.Sid - InfoStart) % 50u);
			if (offset >= 32 && offset <= 34 && attr.Val > 0)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 2026-11-13 新增（修复记录第 271 节）：调理过至少一次就把 20702「完成一次神经调理」标完成。
	/// 只写进度、不发奖，奖励仍由玩家在成就页领取。
	/// </summary>
	internal static async Task MarkAchievementIfNeededAsync(Connection connection, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null || !HasAnyStars(player))
		{
			return;
		}
		await HouseAchievementClaim.MarkCompleteAsync(connection, 20702, sync);
	}
}
