using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardUpgradeParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }

	[JsonPropertyName("tbMaterials")]
	public List<UpgradeMaterial> Materials { get; set; } = new List<UpgradeMaterial>();
}
