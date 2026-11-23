using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal static class VirCaptureTaskStore
{
	public const uint GroupId = 133u;

	public const uint ProgressMask = 2147483646u;

	public const uint ClaimedBit = 1u;

	public static uint GetProgress(PlayerInstance player, uint missionId)
	{
		uint attr = player.GetAttr(133u, missionId);
		return (attr & 0x7FFFFFFE) >> 1;
	}

	public static bool IsClaimed(PlayerInstance player, uint missionId)
	{
		return (player.GetAttr(133u, missionId) & 1) != 0;
	}

	public static void SetProgress(PlayerInstance player, uint missionId, uint progress, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(133u, missionId);
		orCreate.Val = (orCreate.Val & 1) | ((progress & 0x7FFFFFFF) << 1);
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static void MarkClaimed(PlayerInstance player, uint missionId, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(133u, missionId);
		orCreate.Val |= 1u;
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
