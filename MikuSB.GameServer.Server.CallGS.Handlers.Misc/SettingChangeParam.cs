using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class SettingChangeParam
{
	[JsonPropertyName("id")]
	public uint Id { get; set; }
}
