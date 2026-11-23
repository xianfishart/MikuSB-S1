using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardLockParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }

	/// <summary>0 = 解锁，非 0 = 上锁。客户端传整数，避免布尔序列化差异。</summary>
	[JsonPropertyName("Lock")]
	public int Lock { get; set; }
}
