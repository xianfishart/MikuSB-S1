using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

internal static class StarWishAttr
{
	public const uint Gid = 180u;

	public const uint ActivitySid = 1u;

	public const uint ScoreAwardGotSid = 2u;

	public const uint TreeJigsawStepSid = 3u;

	public const uint ExtraAwardSid = 4u;

	public const uint JigsawSidBase = 80u;

	public const uint PageAwardSidBase = 90u;

	public const uint StorySidBase = 100u;

	public static uint Read(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 180 && x.Sid == sid)?.Val ?? 0;
	}

	public static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 180 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 180u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		if (sync != null)
		{
			sync.Custom[player.ToPackedAttrKey(180u, sid)] = value;
			sync.Custom[player.ToShiftedAttrKey(180u, sid)] = value;
		}
	}

	public static uint JigsawSid(uint id)
	{
		return 80 + id / 30;
	}

	public static uint JigsawBit(uint id)
	{
		return id % 30;
	}

	public static bool OwnsJigsaw(PlayerInstance player, uint id)
	{
		return ((Read(player, JigsawSid(id)) >> (int)JigsawBit(id)) & 1) != 0;
	}

	public static void GrantJigsaw(PlayerInstance player, uint id, NtfSyncPlayer? sync)
	{
		uint sid = JigsawSid(id);
		Set(player, sid, Read(player, sid) | (uint)(1 << (int)JigsawBit(id)), sync);
	}

	public static uint Score(PlayerInstance player)
	{
		uint num = 0u;
		foreach (var (id, starWishJigsawExcel2) in GameData.StarWishJigsawData)
		{
			if (OwnsJigsaw(player, id))
			{
				num += starWishJigsawExcel2.Score;
			}
		}
		return num;
	}

	public static StarWishMainExcel? Act()
	{
		return GameData.StarWishMainData.Values.FirstOrDefault();
	}
}
