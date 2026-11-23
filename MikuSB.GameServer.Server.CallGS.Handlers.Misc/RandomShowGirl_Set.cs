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

[CallGSApi("RandomShowGirl_Set")]
public class RandomShowGirl_Set : ICallGSHandler
{
	private const uint AttrGroup = 139u;

	private const uint FormGroup = 188u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<int> ids = new List<int>();
		List<int> forms = new List<int>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(param);
			if (doc.RootElement.TryGetProperty("tbItemId", out var idsNode) && idsNode.ValueKind == JsonValueKind.Array)
			{
				ids.AddRange(from x in idsNode.EnumerateArray()
					select x.TryGetInt32(out var value) ? value : 0);
			}
			if (doc.RootElement.TryGetProperty("tbForm", out var formsNode) && formsNode.ValueKind == JsonValueKind.Array)
			{
				forms.AddRange(from x in formsNode.EnumerateArray()
					select x.TryGetInt32(out var value) ? value : 0);
			}
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		for (int i = 0; i < ids.Count && i <= 300; i++)
		{
			SetAttr(player, sync, 139u, (uint)i, (uint)Math.Max(0, ids[i]));
			SetAttr(player, sync, 188u, (uint)i, (uint)Math.Max(0, (i < forms.Count) ? forms[i] : 0));
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		await Task.CompletedTask;
	}

	private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			if (value == 0)
			{
				return;
			}
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}
}
