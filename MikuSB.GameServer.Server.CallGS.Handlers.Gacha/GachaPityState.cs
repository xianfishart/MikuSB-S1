namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

internal sealed class GachaPityState
{
	public int ItemCount { get; set; }

	public int TenCount { get; set; }

	public int ProtectType { get; set; } = 1;

	public int PoolTotalTime { get; set; }
}
