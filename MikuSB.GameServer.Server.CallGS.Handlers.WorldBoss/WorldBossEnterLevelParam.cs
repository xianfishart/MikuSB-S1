using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

public sealed class WorldBossEnterLevelParam
{
	[JsonPropertyName("nID")]
	public uint ActivityId { get; set; }

	[JsonPropertyName("nActID")]
	public uint ActId { get; set; }

	[JsonPropertyName("nLevelID")]
	public uint LevelId { get; set; }

	[JsonPropertyName("nTeamID")]
	public uint TeamId { get; set; }

	/// <summary>
	/// 进关时客户端带过来的战斗日志。第 441 节：这里原来是 JsonObject，
	/// 而客户端发的是数组（tbLog:[45,"11-2501",29,...]），反序列化直接抛
	/// 「The JSON value could not be converted to JsonObject」，
	/// 导致 WorldBossLogic_EnterLevel 整体失败 → 没分配挑战块 → 打完后无处记录。
	/// 改成 JsonNode 后数组/对象都能收。
	/// </summary>
	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
