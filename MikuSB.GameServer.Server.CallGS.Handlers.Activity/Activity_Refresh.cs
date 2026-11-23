using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_Refresh")]
public class Activity_Refresh : ICallGSHandler
{
	private const uint ActivityGroupId = 50u;

	private const uint ActivityIdStep = 10u;

	private const long WindowBeforeSeconds = 86400L;

	private const long WindowAfterSeconds = 5184000L;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		if (connection.Player == null || string.IsNullOrWhiteSpace(param))
		{
			return;
		}
		int activityId;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(param);
			activityId = (doc.RootElement.TryGetProperty("nId", out var p) ? p.GetInt32() : 0);
		}
		catch
		{
			return;
		}
		if (activityId <= 0)
		{
			return;
		}
		// 第 428 节：退役的往期队员试用（34 / 100）不再接受任何刷新/开窗请求。
		if (ActivityWindowGuard.IsRetired((uint)activityId, DlcPin.Override()))
		{
			return;
		}
		uint eraId = DlcPin.Override();
		long floor = DlcClientPatches.ActivationFloor(eraId);
		long ceil = DlcClientPatches.ActivationCeil(eraId);
		if (GameData.ActivityData.TryGetValue((uint)activityId, out ActivityExcel windowConf))
		{
			// free_items（特别邀请）类不回包：回包会把窗口开成 now±60d，客户端登录即弹窗
			if (ActivityWindowGuard.IsSuppressedClass(windowConf))
			{
				return;
			}
			long windowStart = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(windowConf.StartTime));
			long windowEnd = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(windowConf.EndTime));
			// 第 423 节：拼图（启忆录）类活动跟着 DLC 期数走，配置里的历史窗口不再拦截
			if (!ActivityWindowGuard.IsAlwaysOpenClass(windowConf) && !ActivityWindowGuard.IsEraActivity(windowStart, windowEnd, floor, ceil))
			{
				return;
			}
		}
		ActivityQuestProgress.Ensure(connection.Player);
		long now = DateTimeOffset.Now.ToUnixTimeSeconds();
		uint start = (uint)(now - 86400);
		uint end = (uint)(now + 5184000);
		uint sid = (uint)(activityId * 10);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SetAttr(connection.Player, sid, start, sync);
		JsonObject response = new JsonObject
		{
			["nId"] = activityId,
			["nStartTime"] = start,
			["nEndTime"] = end,
			["nCloseFlag"] = 0
		};
		await CallGSRouter.SendScript(connection, "ActivityQuest_Open", response.ToJsonString(), sync);
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 50u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(50u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(50u, sid)] = value;
	}
}
