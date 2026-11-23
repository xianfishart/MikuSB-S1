using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("player", "Player level / experience control for testing.", "Usage: /player level [1-100]  (no value = max level)", new string[] { "pl" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandPlayer : ICommands
{
	[CommandMethod("level")]
	public async ValueTask SetLevel(CommandArg arg)
	{
		int max = MaxLevel();
		int level = arg.GetInt(0);
		if (level <= 0)
		{
			level = max;
		}
		if (level > max)
		{
			level = max;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		uint old = player.Data.Level;
		player.Data.Level = (uint)level;
		player.Data.Exp = 0;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Core[0u] = (uint)level;
		sync.Core[1u] = 0u;
		await player.SendPacket(1004, sync);
		await arg.SendMsg($"Player level: {old} -> {level} (max {max}). Relog to refresh unlock checks.");
	}

	[CommandMethod("exp")]
	public async ValueTask SetExp(CommandArg arg)
	{
		int exp = arg.GetInt(0);
		if (exp < 0)
		{
			exp = 0;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		player.Data.Exp = exp;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Core[0u] = player.Data.Level;
		sync.Core[1u] = (uint)exp;
		await player.SendPacket(1004, sync);
		await arg.SendMsg($"Player exp set to {exp}.");
	}

	/// <summary>取玩家等级配置（Resources/player/levels.json）里的最高等级，避免写死。</summary>
	private static int MaxLevel()
	{
		int max = 1;
		foreach (uint key in GameData.PlayerLevelData.Keys)
		{
			if ((int)key > max)
			{
				max = (int)key;
			}
		}
		return max;
	}
}
