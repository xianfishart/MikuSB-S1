using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

internal sealed class GachaUpSelectState
{
	public uint[]? SelectedItem { get; set; }

	public bool GuaranteedNext { get; set; }

	public JArray? RawState { get; set; } = new JArray();
}
