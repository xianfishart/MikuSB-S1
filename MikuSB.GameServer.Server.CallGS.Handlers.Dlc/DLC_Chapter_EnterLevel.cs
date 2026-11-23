using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Server.CallGS.Handlers.Chapter;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("DLC_Chapter_EnterLevel")]
public class DLC_Chapter_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// DLC 关卡进关扣体力（修复记录第 221 节补齐第 181 节的遗留口子）。
		// 只认明确键名，且必须命中 DlcChapterLevelData，避免章节 id 被误当关卡 id 扣费。
		uint levelId = ReadUInt(request["nID"], request["nLevelID"], request["nLevelId"], request["nId"], request["levelId"], request["nStageID"]);
		if (levelId != 0 && GameData.DlcChapterLevelData.TryGetValue(levelId, out DlcChapterLevelExcel config) && !await ChapterVigor.ConsumeCost(connection, connection.Player, config.ConsumeVigor(), sync, "DLC_Chapter_EnterLevel"))
		{
			return;
		}
		string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
		await CallGSRouter.SendScript(connection, "DLC_Chapter_EnterLevel", rsp, sync);
	}

	private static uint ReadUInt(params JsonNode[] nodes)
	{
		foreach (JsonNode node in nodes)
		{
			if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value) && value >= 1.0 && value < 4294967295.0)
			{
				return (uint)value;
			}
		}
		return 0u;
	}
}
