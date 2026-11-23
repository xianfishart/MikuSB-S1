using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

public static class PlayerChargeService
{
	public const uint GroupId = 0u;

	public const uint ChargedSid = 18u;

	public const uint ClaimedMaskSid = 19u;

	public const uint ShowStateSid = 20u;

	public const uint ShowAmountBit = 30u;

	public static uint Charged(PlayerInstance player)
	{
		return player.Attributes.GetValue(0u, 18u);
	}

	public static bool IsClaimed(PlayerInstance player, uint index)
	{
		if (index == 0 || index > 30)
		{
			return false;
		}
		uint value = player.Attributes.GetValue(0u, 19u);
		return (value & (uint)(1 << (int)(index - 1))) != 0;
	}

	public static void MarkClaimed(PlayerInstance player, uint index, NtfSyncPlayer? sync)
	{
		if (index != 0 && index <= 30)
		{
			uint value = player.Attributes.GetValue(0u, 19u);
			SetAttr(player, 19u, value | (uint)(1 << (int)(index - 1)), sync);
		}
	}

	public static bool ShowAmounts(PlayerInstance player)
	{
		return (player.Attributes.GetValue(0u, 19u) & 0x40000000) != 0;
	}

	public static void SetShowAmounts(PlayerInstance player, bool on, NtfSyncPlayer? sync)
	{
		uint value = player.Attributes.GetValue(0u, 19u);
		uint num = 1073741824u;
		value = (on ? (value | num) : (value & ~num));
		SetAttr(player, 19u, value, sync);
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(0u, sid);
		if (orCreate.Val != value)
		{
			orCreate.Val = value;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
		}
	}
}
