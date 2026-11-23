using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Player;

public sealed class PlayerAttributes
{
	private readonly PlayerGameData data;

	private readonly Dictionary<(uint Gid, uint Sid), PlayerAttr> attributes = new Dictionary<(uint, uint), PlayerAttr>();

	private readonly Dictionary<(uint Gid, uint Sid), PlayerStrAttr> stringAttributes = new Dictionary<(uint, uint), PlayerStrAttr>();

	public IReadOnlyList<PlayerAttr> All => data.Attrs;

	public IReadOnlyList<PlayerStrAttr> AllStrings => data.StrAttrs;

	public PlayerAttributes(PlayerGameData data)
	{
		this.data = data;
		if (data.Attrs.Count != data.Attrs.Select((PlayerAttr x) => (Gid: x.Gid, Sid: x.Sid)).Distinct().Count())
		{
			DatabaseHelper.logger.Info($"dedupe attrs {data.Attrs.Count} -> {data.Attrs.Select((PlayerAttr x) => (Gid: x.Gid, Sid: x.Sid)).Distinct().Count()} uid={data.Uid}");
			data.Attrs = (from x in data.Attrs
				group x by (Gid: x.Gid, Sid: x.Sid) into g
				select g.Last()).ToList();
			DatabaseHelper.SaveDatabaseType(data);
		}
		foreach (PlayerAttr attr in data.Attrs)
		{
			attributes.TryAdd((attr.Gid, attr.Sid), attr);
		}
		foreach (PlayerStrAttr strAttr in data.StrAttrs)
		{
			stringAttributes.TryAdd((strAttr.Gid, strAttr.Sid), strAttr);
		}
	}

	public PlayerAttr? Get(uint gid, uint sid)
	{
		return attributes.GetValueOrDefault((gid, sid));
	}

	public uint GetValue(uint gid, uint sid)
	{
		return Get(gid, sid)?.Val ?? 0;
	}

	public bool TryGet(uint gid, uint sid, out PlayerAttr? attr)
	{
		return attributes.TryGetValue((gid, sid), out attr);
	}

	public PlayerAttr GetOrCreate(uint gid, uint sid)
	{
		if (attributes.TryGetValue((gid, sid), out PlayerAttr value))
		{
			return value;
		}
		value = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(value);
		attributes[(gid, sid)] = value;
		return value;
	}

	public PlayerAttr Set(uint gid, uint sid, uint value)
	{
		PlayerAttr orCreate = GetOrCreate(gid, sid);
		orCreate.Val = value;
		return orCreate;
	}

	public PlayerAttr Add(uint gid, uint sid, uint amount)
	{
		PlayerAttr orCreate = GetOrCreate(gid, sid);
		orCreate.Val = Math.Min(uint.MaxValue - orCreate.Val, amount) + orCreate.Val;
		return orCreate;
	}

	public bool Remove(uint gid, uint sid)
	{
		if (!attributes.Remove((gid, sid), out PlayerAttr value))
		{
			return false;
		}
		data.Attrs.Remove(value);
		return true;
	}

	public void RemoveWhere(Func<PlayerAttr, bool> predicate)
	{
		foreach (PlayerAttr item in data.Attrs.Where(predicate).ToList())
		{
			Remove(item.Gid, item.Sid);
		}
	}

	public PlayerStrAttr? GetString(uint gid, uint sid)
	{
		return stringAttributes.GetValueOrDefault((gid, sid));
	}

	public string? GetStringValue(uint gid, uint sid)
	{
		return GetString(gid, sid)?.Val;
	}

	public PlayerStrAttr SetString(uint gid, uint sid, string value)
	{
		if (!stringAttributes.TryGetValue((gid, sid), out PlayerStrAttr value2))
		{
			value2 = new PlayerStrAttr
			{
				Gid = gid,
				Sid = sid
			};
			data.StrAttrs.Add(value2);
			stringAttributes[(gid, sid)] = value2;
		}
		value2.Val = value;
		return value2;
	}

	public void SyncTo(NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}

	public void SyncTo(NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		sync.Custom[ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[ToShiftedAttrKey(gid, sid)] = value;
	}

	public void SyncTo(NtfSyncPlayer sync, PlayerStrAttr attr)
	{
		sync.CustomStr[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}

	public void SyncTo(MikuSB.Proto.Player player)
	{
		foreach (PlayerAttr attr in data.Attrs)
		{
			player.Attrs[ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
			player.Attrs[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		}
	}

	public void SyncTo(MikuSB.Proto.Player player, PlayerStrAttr attr)
	{
		player.StrAttrs[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}

	public uint ToPackedAttrKey(uint gid, uint sid)
	{
		if (gid == 0)
		{
			return sid;
		}
		return gid * 10000 + sid;
	}

	public uint ToShiftedAttrKey(uint gid, uint sid)
	{
		if (gid == 0)
		{
			return sid;
		}
		return (gid << 16) | sid;
	}
}
