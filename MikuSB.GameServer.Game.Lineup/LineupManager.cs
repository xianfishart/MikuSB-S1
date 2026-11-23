using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Lineup;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Lineup;

namespace MikuSB.GameServer.Game.Lineup;

public class LineupManager(PlayerInstance player) : BasePlayerManager(player)
{
	public LineupData LineupData { get; } = DatabaseHelper.GetInstanceOrCreateNew<LineupData>(player.Uid);

	public async ValueTask<LineupDataInfo?> UpdateLineup(int lineupId, uint member1, uint member2, uint member3, bool sendPacket = true, string? name = null)
	{
		if (!LineupData.LineupInfo.TryGetValue(lineupId, out LineupDataInfo formation))
		{
			formation = new LineupDataInfo
			{
				Index = (uint)lineupId,
				Name = lineupId.ToString()
			};
			LineupData.LineupInfo[lineupId] = formation;
		}
		formation.Member1 = member1;
		formation.Member2 = member2;
		formation.Member3 = member3;
		if (!string.IsNullOrEmpty(name))
		{
			formation.Name = name;
		}
		// 单条编队更新（客户端 Req_UpdateLineup 走这里）原来只改内存不落库，
		// 一重登就退回数据库里的旧编队（表现为「编好队伍，重登变回默认角色」）。
		// 批量接口 Lineups_Update 本来就有 SaveDatabaseType，单条这里补齐。
		DatabaseHelper.SaveDatabaseType(LineupData);
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfSyncLineup(formation));
		}
		return formation;
	}
}
