using System;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

internal static class Rogue3DAttr
{
	public const uint GroupId = 124u;

	public const uint NodeBase = 2001u;

	public const uint LineBase = 2101u;

	public const uint UavIdSid = 3101u;

	public const uint UavCoreSid = 3102u;

	public const uint PartBegin = 3105u;

	public const uint PartEnd = 3365u;

	public const int NodeStateUnlocked = 2;

	public const int LineStateUnlocked = 1;

	public static uint Read(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid)?.Val ?? 0;
	}

	public static void Write(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 124u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(124u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(124u, sid)] = value;
	}

	public static uint GetField(uint value, int index, int bits)
	{
		return (value >> index) & (uint)((1 << bits) - 1);
	}

	public static uint SetField(uint value, int index, int bits, uint field)
	{
		uint num = (uint)((1 << bits) - 1 << index);
		return (value & ~num) | ((field << index) & num);
	}

	public static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return (uint)Math.Max(0.0, value);
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (uint)Math.Max(0L, value3);
			}
		}
		return 0u;
	}
}
