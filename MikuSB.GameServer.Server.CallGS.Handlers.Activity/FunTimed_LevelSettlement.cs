using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("FunTimed_LevelSettlement")]
public class FunTimed_LevelSettlement : ICallGSHandler
{
	private static readonly Logger Logger = new Logger("FunTimed");

	// 客户端 FunTimedLevel:StarMark() 读 Launch.GID(21) 的位掩码（1/3/7 → 1/2/3 星）
	private const uint StarGroupId = 21u;

	// 客户端 FunTimedLevel:ScoreMark() 读 Launch.GPASSID(22)，卡片上「记录」直接显示这个数
	private const uint ScoreGroupId = 22u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int levelId = 0;
		int star = 0;
		int score = 0;
		try
		{
			JsonNode node = JsonNode.Parse(param);
			levelId = ReadInt(node?["nID"]);
			star = ReadInt(node?["nStar"]);
			score = ReadInt(node?["nScore"]);
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (player != null && levelId != 0)
		{
			uint lid = (uint)levelId;

			// ---- 1. 记录（最高分） ----
			uint bestScore = (uint)Math.Max(0, score);
			PlayerAttr scoreAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == ScoreGroupId && x.Sid == lid);
			if (scoreAttr == null)
			{
				scoreAttr = new PlayerAttr
				{
					Gid = ScoreGroupId,
					Sid = lid,
					Val = bestScore
				};
				player.Data.Attrs.Add(scoreAttr);
			}
			else if (bestScore > scoreAttr.Val)
			{
				scoreAttr.Val = bestScore;
			}
			sync.Custom[player.ToPackedAttrKey(ScoreGroupId, lid)] = scoreAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(ScoreGroupId, lid)] = scoreAttr.Val;

			// ---- 2. 星级（位掩码，只增不减） ----
			uint oldStarMask = 0u;
			if (star > 0)
			{
				uint mask = ((star >= 3) ? 7u : ((uint)((1 << star) - 1)));
				PlayerAttr starAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == StarGroupId && x.Sid == lid);
				if (starAttr == null)
				{
					starAttr = new PlayerAttr
					{
						Gid = StarGroupId,
						Sid = lid,
						Val = 0u
					};
					player.Data.Attrs.Add(starAttr);
				}
				oldStarMask = starAttr.Val;
				starAttr.Val |= mask;
				sync.Custom[player.ToPackedAttrKey(StarGroupId, lid)] = starAttr.Val;
				sync.Custom[player.ToShiftedAttrKey(StarGroupId, lid)] = starAttr.Val;
			}

			DatabaseHelper.SaveDatabaseType(player.Data);
			Logger.Info($"[FunTimed] settle level={levelId} star={star} score={score} starMask={oldStarMask}->{(star > 0 ? (oldStarMask | ((star >= 3) ? 7u : ((uint)((1 << star) - 1)))) : oldStarMask)} best={scoreAttr.Val}");
			AppendLog($"settle level={levelId} star={star} score={score} starMask={oldStarMask} best={scoreAttr.Val}");
		}
		await CallGSRouter.SendScript(connection, "FunTimed_LevelSettlement", "{\"tbAwards\":[]}", sync);
	}

	private static void AppendLog(string line)
	{
		try
		{
			string dir = "D:/DSH/.tools";
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.AppendAllText(Path.Combine(dir, "ftsettle.log"), $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	private static int ReadInt(JsonNode node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<long>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<double>(out var value3))
			{
				return (int)value3;
			}
		}
		return 0;
	}
}
