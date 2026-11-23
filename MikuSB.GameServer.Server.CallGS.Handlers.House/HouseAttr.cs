using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseAttr
{
	internal const uint Gid = 101u;

	internal const uint BedroomStartSid = 2550u;

	internal const uint BedroomRegisteredNoRoom = 100u;

	internal const uint PlayerRingInfoSidBase = 3174u;

	internal const uint BedroomPerFloor = 8u;

	internal static uint Read(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid)?.Val ?? 0;
	}

	internal static async Task SetAsync(Connection connection, uint sid, uint value, NtfSyncPlayer sync, bool deleteIfZero = false, bool sendImmediate = false)
	{
		PlayerInstance player = connection.Player;
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if ((value == 0) & deleteIfZero)
		{
			if (attr != null)
			{
				player.Data.Attrs.Remove(attr);
			}
		}
		else
		{
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = 101u,
					Sid = sid
				};
				player.Data.Attrs.Add(attr);
			}
			attr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = value;
		if (sendImmediate)
		{
			await player.SendPacket(1011, new NtfSetAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = value
			});
		}
	}

	internal static uint BedroomSlotSid(int roomId)
	{
		return (uint)(2550 + roomId);
	}

	internal static uint GirlRoomNumSid(int girlId)
	{
		return (uint)(girlId * 50 + 1);
	}

	internal static async Task MoveGirlIntoRoomAsync(Connection connection, int girlId, int roomId, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint targetSlotSid = BedroomSlotSid(roomId);
		uint occupant = Read(player, targetSlotSid);
		await ClearGirlSlotsAsync(connection, girlId, sync);
		await SetAsync(connection, targetSlotSid, (uint)girlId, sync);
		await SetAsync(connection, GirlRoomNumSid(girlId), (uint)roomId, sync);
		if (occupant != 0 && occupant != (uint)girlId)
		{
			await ClearGirlSlotsAsync(connection, (int)occupant, sync);
			await SetAsync(connection, GirlRoomNumSid((int)occupant), 100u, sync);
		}
	}

	internal static int FindGirlRoom(PlayerInstance player, int girlId)
	{
		int num = (int)Read(player, GirlRoomNumSid(girlId));
		bool flag = num >= 1 && num < 100 && Read(player, BedroomSlotSid(num)) == (uint)girlId;
		for (uint num2 = 1u; num2 <= 32; num2++)
		{
			if (Read(player, BedroomSlotSid((int)num2)) == (uint)girlId)
			{
				return (int)num2;
			}
		}
		return flag ? num : 0;
	}

	internal static async Task ClearGirlSlotsAsync(Connection connection, int girlId, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		for (uint i = 1u; i <= 32; i++)
		{
			uint slotSid = BedroomSlotSid((int)i);
			if (Read(player, slotSid) == (uint)girlId)
			{
				// 2026-11-13（修复记录第 281 节）：搬出时直接删属性，不再留 Val=0 的空壳，
				// 否则 GetNextBedroomSid 会把住过的房间当成仍然占用。
				await SetAsync(connection, slotSid, 0u, sync, deleteIfZero: true);
			}
		}
	}

	internal static async Task<int> MoveGirlOutOfRoomAsync(Connection connection, int girlId, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		int roomId = FindGirlRoom(player, girlId);
		await ClearGirlSlotsAsync(connection, girlId, sync);
		await SetAsync(connection, GirlRoomNumSid(girlId), 100u, sync);
		return roomId;
	}

	internal static uint GetNextBedroomSid(PlayerInstance player, uint floorId)
	{
		// 2026-11-13 修复（修复记录第 281 节）：空房判定改为「属性不存在 **或** 值为 0」。
		// 旧版只看属性是否存在，而少女搬出房间（GirlRoomChange RoomId=0 或直接换房）时
		// ClearGirlSlotsAsync 写的是 0 而不是删属性 → 住过又搬走的房间永远被当成「已占用」。
		// 4 层 8 间里只要有 4 间被这样占住，本层就会被判成满员，入住办理返回 house.NoRoom，
		// 客户端没有这个错误码的提示，表现就是「点了没反应」。
		if (floorId < 2)
		{
			floorId = 2;
		}
		uint num = 2550 + (floorId - 2) * 8 + 1;
		for (uint num2 = 0u; num2 < 8; num2++)
		{
			uint sid = num + num2;
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
			if (attr == null || attr.Val == 0)
			{
				return sid;
			}
		}
		return 0u;
	}

	internal static uint PackArcadePropUse(int type, int id, ushort count)
	{
		return (uint)(((count & 0xFFFF) << 16) | ((id & 0xFF) << 8) | (type & 0xFF));
	}

	/// <summary>
	/// 2026-11-13 追加（修复记录第 283 节）：把「区域/房间号」换算成住在那里的少女 ID。
	///
	/// 客户端 GiveGiftToArea 只传 AreaId（房间号，如 20 = 20 号房），不传少女 ID。
	/// 旧代码直接把 AreaId 当少女 ID 传给 HouseFavor.Award（sid = areaId×50），
	/// 写到了根本不存在的少女属性块上 —— 送礼物后好感度当然一点没变。
	///
	/// 先查房间槽 2550+roomId（值就是少女 ID），再退化成按少女的 RoomNum(girlId*50+1) 反查，
	/// 兼容公共区域（1~7）等没有住人的情况（返回 0，只加氛围值不加角色好感）。
	/// </summary>
	internal static int GirlInArea(PlayerInstance player, int areaId)
	{
		// 2026-11-13 修正（修复记录第 284 节）：送礼/摆家具用的 AreaId 在「卧室」场景下就是**少女 ID 本身**，
		// 不是卧室槽（2550+房间号）里的房间号。证据：
		//   ① 客户端 EGirlAttr.FurnitureStart=10 / FurnitureEnd=19，每个少女的家具位就存在自己的属性块里，
		//      HouseGiveGift.FurSlot(areaId) = areaId*50 + 10 + idx/10 → area 20 落在少女20 的 1000~1012；
		//   ② gift.json 的 SupportTargets 取值 1~50 与少女 ID 完全一致（另加 51/60/70/80/90 公共区域）；
		//   ③ 反汇编 HouseGiftLogic.CheckIsGirl(AreaId) 直接拿 AreaId 当少女 ID 判断。
		// 旧实现按「卧室槽 2550+areaId」反查，会把 area 20（卜卜）认成房间 20 里的少女 13。
		if (areaId < 1 || areaId > 50)
		{
			// 51/60/70/80/90 等公共区域没有单一少女，只加基地氛围值。
			return 0;
		}
		return areaId;
	}
}
