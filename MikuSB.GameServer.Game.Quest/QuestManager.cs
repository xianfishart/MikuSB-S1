using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Quest;

// 由 5.3 版 QuestManager 精简移植（2026-11-10，一百九十三节）：
// 5.3 的 CompleteAllQuestLevels 标志存 MikuCommon PlayerGameData 新列，
// 本版 MikuCommon 不带该列，故改为「实体化」——直接把通关状态写进
// 属性组 21（星级/通关状态）与 22（通关次数）并落库，登录时随常规
// 属性同步自然带到客户端，效果与 5.3 一致（角色状态栏全开）。
// 结算/周星奖励相关方法（SettleLevelAsync 等）本版未移植，现有结算
// 流程保持不动。
public class QuestManager : BasePlayerManager
{
	private const uint LevelStateGroupId = 21u;

	private const uint LevelPassGroupId = 22u;

	private static readonly Logger Logger = new Logger("Quest");

	private readonly SemaphoreSlim settlementLock = new SemaphoreSlim(1, 1);

	public QuestManager(PlayerInstance player)
		: base(player)
	{
	}

	public async ValueTask<QuestCompletionResult> SetAllLevelsForTestingAsync(bool completed)
	{
		await settlementLock.WaitAsync();
		try
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			List<(QuestLevelType LevelType, ILevelRewardConfig LevelConfig)> source = GetAllLevelConfigs().ToList();
			int num = source.Select((x) => x.LevelConfig.ID).Distinct().Count();
			Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
			foreach (var (levelType, levelRewardConfig) in source)
			{
				dictionary[levelRewardConfig.ID] = dictionary.GetValueOrDefault(levelRewardConfig.ID) | GetCompletedState(levelType, levelRewardConfig);
			}
			foreach (var (sid, value) in dictionary)
			{
				if (completed)
				{
					PlayerAttr levelState = Player.Attributes.GetOrCreate(LevelStateGroupId, sid);
					levelState.Val |= value;
					PlayerAttr levelPass = Player.Attributes.GetOrCreate(LevelPassGroupId, sid);
					if (levelPass.Val < 1)
					{
						levelPass.Val = 1u;
					}
					Player.Attributes.SyncTo(sync, LevelStateGroupId, sid, levelState.Val);
					Player.Attributes.SyncTo(sync, LevelPassGroupId, sid, levelPass.Val);
				}
				else
				{
					Player.Attributes.Remove(LevelStateGroupId, sid);
					Player.Attributes.Remove(LevelPassGroupId, sid);
					Player.Attributes.SyncTo(sync, LevelStateGroupId, sid, 0u);
					Player.Attributes.SyncTo(sync, LevelPassGroupId, sid, 0u);
				}
			}
			DatabaseHelper.SaveDatabaseType(Player.Data);
			Logger.Info($"All quest levels updated for testing. uid={Player.Uid} completed={completed} levelCount={num}");
			return new QuestCompletionResult(num, sync);
		}
		finally
		{
			settlementLock.Release();
		}
	}

	private IEnumerable<ILevelRewardConfig> GetLevelConfigs(QuestLevelType levelType)
	{
		return levelType switch
		{
			QuestLevelType.Chapter => (IEnumerable<ILevelRewardConfig>)GameData.ChapterLevelData.Values,
			QuestLevelType.Daily => GameData.DailyLevelData.Values,
			QuestLevelType.Role => GameData.RoleLevelData.Values,
			_ => Array.Empty<ILevelRewardConfig>(),
		};
	}

	private IEnumerable<(QuestLevelType LevelType, ILevelRewardConfig LevelConfig)> GetAllLevelConfigs()
	{
		QuestLevelType[] values = Enum.GetValues<QuestLevelType>();
		foreach (QuestLevelType levelType in values)
		{
			foreach (ILevelRewardConfig levelConfig in GetLevelConfigs(levelType))
			{
				yield return (levelType, levelConfig);
			}
		}
	}

	private static uint GetCompletedState(QuestLevelType levelType, ILevelRewardConfig levelConfig)
	{
		if (levelType == QuestLevelType.Chapter && levelConfig is ChapterLevelExcel chapterLevelExcel && chapterLevelExcel.IsPlot())
		{
			return 256u;
		}
		if (levelType != QuestLevelType.Daily)
		{
			return 7u;
		}
		return 263u;
	}
}
