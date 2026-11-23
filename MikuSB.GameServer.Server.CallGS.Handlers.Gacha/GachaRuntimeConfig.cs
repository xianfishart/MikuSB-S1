using System.Collections.Generic;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

internal sealed class GachaRuntimeConfig
{
	public List<GachaPoolItem> AllPoolItems { get; set; } = new List<GachaPoolItem>();

	public int ProtectThreshold { get; set; }

	public List<PoolRarityRef> ProtectPools { get; set; } = new List<PoolRarityRef>();

	public PoolRarityRef? UpTarget { get; set; }

	public int TopRarity { get; set; }

	public int TopGenre { get; set; }

	public int TenGuaranteeRarity { get; set; }
}
