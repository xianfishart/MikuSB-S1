using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「岛屿等级」等关卡级数据（属性组 128，客户端 VirCapture 模块）。
///
/// 运行时实测常量（第 503 节）：
///   VirCapture.GroupId      = 128
///   VirCapture.MapDataStart = 10000
///   VirCapture.MAX_MAP      = 3, MAX_MAPDATA_LEN = 3000
///   VirCapture.Off_MapId    = 1, Off_MapLevel = 8
/// 客户端 VirCapture.GetMapDataStartId(mapId) 的做法就是：
///   从 MapDataStart 开始，每 3000 一个槽，读槽内 +Off_MapId，等于 mapId 就返回该槽起始 key。
/// 326（新星开拓蓝图岛）落在第 0 槽，所以等级 key = 128/10008。
/// </summary>
internal static class VirCaptureLevelState
{
	public const uint Gid = 128u;

	public const uint MapDataStart = 10000u;

	public const uint SlotLen = 3000u;

	public const uint MaxSlots = 3u;

	public const uint OffMapId = 1u;

	public const uint OffMapLevel = 8u;

	/// <summary>岛屿等级上限（operating_data.json 的 StaffProgressbar / BuildingProgressbar 都是 5 档）。</summary>
	public const uint MaxMapLevel = 5u;

	/// <summary>找该 mapId 所在槽的起始 key，找不到返回 0。</summary>
	public static uint FindSlotBase(PlayerInstance player, uint mapId)
	{
		if (mapId == 0u)
		{
			return 0u;
		}
		for (uint slot = 0u; slot < MaxSlots; slot++)
		{
			uint baseKey = MapDataStart + slot * SlotLen;
			if (player.GetAttr(Gid, baseKey + OffMapId) == mapId)
			{
				return baseKey;
			}
		}
		return 0u;
	}

	public static uint GetMapLevel(PlayerInstance player, uint mapId)
	{
		uint baseKey = FindSlotBase(player, mapId);
		if (baseKey == 0u)
		{
			return 0u;
		}
		return player.GetAttr(Gid, baseKey + OffMapLevel);
	}

	/// <summary>
	/// 岛屿等级 +delta（任务奖励类型 3 就是 [[3,326,1]]，即岛等级 +1）。
	/// 只升不降，上限 MaxMapLevel。返回新等级。
	/// </summary>
	public static uint UpgradeMapLevel(PlayerInstance player, uint mapId, uint delta, NtfSyncPlayer? sync)
	{
		uint baseKey = FindSlotBase(player, mapId);
		if (baseKey == 0u || delta == 0u)
		{
			return 0u;
		}
		uint level = player.GetAttr(Gid, baseKey + OffMapLevel);
		uint next = level + delta;
		if (next > MaxMapLevel)
		{
			next = MaxMapLevel;
		}
		if (next == level)
		{
			return level;
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, baseKey + OffMapLevel);
		attr.Val = next;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return next;
	}
}
