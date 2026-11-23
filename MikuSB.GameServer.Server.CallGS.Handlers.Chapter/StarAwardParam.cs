using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

internal sealed class StarAwardParam
{
	[JsonPropertyName("bMain")]
	public bool Main { get; set; }

	[JsonPropertyName("nDifficult")]
	public int Difficult { get; set; }

	[JsonPropertyName("nChapterID")]
	public int ChapterId { get; set; }

	[JsonPropertyName("nIndex")]
	public int Index { get; set; }
}
