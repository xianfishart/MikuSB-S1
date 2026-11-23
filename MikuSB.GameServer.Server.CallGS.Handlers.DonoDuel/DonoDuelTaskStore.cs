using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

internal static class DonoDuelTaskStore
{
	public const uint GroupId = 186u;

	public const uint AcceptBit = 1u;

	public const uint ClaimedBit = 2u;

	public const uint ProgressMask = 4294967292u;

	public static uint Progress(PlayerInstance player, uint missionId)
	{
		return (player.GetAttr(186u, missionId) & 0xFFFFFFFCu) >> 2;
	}

	public static bool IsAccepted(PlayerInstance player, uint missionId)
	{
		return (player.GetAttr(186u, missionId) & 1) != 0;
	}

	public static bool IsClaimed(PlayerInstance player, uint missionId)
	{
		return (player.GetAttr(186u, missionId) & 2) != 0;
	}

	public static void SetAccepted(PlayerInstance player, uint missionId, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, missionId);
		orCreate.Val |= 1u;
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static void SetProgress(PlayerInstance player, uint missionId, uint progress, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, missionId);
		orCreate.Val = (orCreate.Val & 3) | ((progress & 0x3FFFFFFF) << 2);
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static void MarkClaimed(PlayerInstance player, uint missionId, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(186u, missionId);
		orCreate.Val |= 2u;
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
