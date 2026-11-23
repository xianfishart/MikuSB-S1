using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

[CallGSApi("WorldBossLogic_EnterLevel")]
public class WorldBossLogic_EnterLevel : CallGSHandler<WorldBossEnterLevelParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, WorldBossEnterLevelParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		WorldBossActExcel worldBossActExcel = WorldBossService.FindAct(request.ActivityId, request.ActId) ?? WorldBossService.SelectAct(DateTime.Now);
		if (worldBossActExcel == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int num = WorldBossService.BeginAttempt(player, worldBossActExcel, request.LevelId, sync, ResolveLineup(player, request.TeamId));
		if (num < 0)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		uint num2 = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
		if (num2 == 0)
		{
			num2 = 1u;
		}
		uint num3 = WorldBossService.BossMaxHp(request.LevelId);
		uint num4 = num3 / 1000000;
		Logger.GetByClassName().Info($"WorldBoss EnterLevel act={worldBossActExcel.ActivityId}/{worldBossActExcel.ActId} stage={request.LevelId} block={num} num={num4} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nSeed"] = num2,
			["levelId"] = request.LevelId,
			["num"] = num4
		}, sync, "WorldBossLogic_EnterLevel"));
	}

	/// <summary>
	/// 取本次进关用的阵容成员，写进挑战块（179/块*10+3..5），
	/// 客户端「已有阵容最高积分」里的阵容缩略图就是读这三个值。
	/// 第 442 节：写的是「detail*100+particular」编码（客户端 GetMemberGDPL 按这个拆），
	/// 不是编队里存着的角色 Guid —— 第 441 节传 Guid，拆出来 detail=0，头像永远是空的。
	/// </summary>
	private static List<uint> ResolveLineup(PlayerInstance player, uint teamId)
	{
		List<uint> members = new List<uint> { 0u, 0u, 0u };
		try
		{
			if (player.LineupManager?.LineupData?.LineupInfo != null && player.LineupManager.LineupData.LineupInfo.TryGetValue((int)teamId, out MikuSB.Database.Lineup.LineupDataInfo info) && info != null)
			{
				members[0] = WorldBossService.EncodeMember(player, info.Member1);
				members[1] = WorldBossService.EncodeMember(player, info.Member2);
				members[2] = WorldBossService.EncodeMember(player, info.Member3);
			}
		}
		catch
		{
		}
		return members;
	}
}
