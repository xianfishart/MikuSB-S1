using System;

namespace MikuSB.GameServer.Game.Gacha;

public sealed class GachaRecordEntry
{
	public string Key { get; set; } = "";

	public uint[] Gdpl { get; set; } = Array.Empty<uint>();

	public long Time { get; set; }

	public int Order { get; set; }
}
