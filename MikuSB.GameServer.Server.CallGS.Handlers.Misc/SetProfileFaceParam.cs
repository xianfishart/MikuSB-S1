using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class SetProfileFaceParam
{
	[JsonPropertyName("nHeadItemID")]
	public uint HeadItemId { get; set; }

	[JsonPropertyName("nFrameItemID")]
	public uint FrameItemId { get; set; }
}
