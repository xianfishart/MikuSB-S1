using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

/// <summary>
/// 妮塔之邀棋盘 → 客户端每推进一步都会把整盘状态 Dump 给服务端（仅用于日志/对账）。
/// 客户端侧 <c>Activity.lua:1702</c> 收到回包后会
/// <c>EventSystem.Trigger("NitaMonolopy_DumpGameRet", tbParam.nUniqueNum, tbParam.sErr)</c>，
/// 而 <c>NitaMonolopy.lua:2361</c> 的处理器第一句就是
/// <c>if NitaMonolopy.DumpUniqueNum ~= nUniqueNum then return end</c>。
/// 也就是说回包必须原样带回 <c>nUniqueNum</c>，否则状态机永远停在 CellPreTriger(4)。
/// </summary>
public sealed class NitaMonolopyDumpGameParam
{
	[JsonPropertyName("sGameData")]
	public string? GameData { get; set; }

	[JsonPropertyName("sLog")]
	public string? Log { get; set; }

	[JsonPropertyName("nUniqueNum")]
	public long UniqueNum { get; set; }

	/// <summary>格子数据，客户端有时给 object（数字键）、有时给数组，这里不做约束。</summary>
	[JsonPropertyName("tbCellData")]
	public JsonNode? CellData { get; set; }
}
