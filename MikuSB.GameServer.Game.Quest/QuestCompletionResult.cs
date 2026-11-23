using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Quest;

public readonly record struct QuestCompletionResult(int LevelCount, NtfSyncPlayer Sync);
