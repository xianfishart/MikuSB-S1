using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("RandomShowGirl_SetGirlSkinInLevel")]
public class RandomShowGirl_SetGirlSkinInLevel : ICallGSHandler
{
	private const uint AttrGroup = 139u;

	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<int> list = new List<int>();
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
			{
				list.AddRange(from x in jsonDocument.RootElement.EnumerateArray()
					select x.TryGetInt32(out var value) ? value : 0);
			}
		}
		catch
		{
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		for (int num = 0; num < list.Count && num < 300; num++)
		{
			SetAttr(player, ntfSyncPlayer, (uint)(600 + num), (uint)Math.Max(0, list[num]));
		}
		uint sid;
		for (sid = (uint)(600 + list.Count); sid < 900; sid++)
		{
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 139 && x.Sid == sid);
			if (playerAttr != null)
			{
				player.Data.Attrs.Remove(playerAttr);
				ntfSyncPlayer.Custom[player.ToPackedAttrKey(139u, sid)] = 0u;
				ntfSyncPlayer.Custom[player.ToShiftedAttrKey(139u, sid)] = 0u;
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.CompletedTask;
	}

	private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 139 && x.Sid == sid);
		if (playerAttr == null)
		{
			if (value == 0)
			{
				return;
			}
			playerAttr = new PlayerAttr
			{
				Gid = 139u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(139u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(139u, sid)] = value;
	}
}
