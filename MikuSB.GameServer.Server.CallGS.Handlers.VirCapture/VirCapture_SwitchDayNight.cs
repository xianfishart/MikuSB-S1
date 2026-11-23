using System;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「昼夜切换」（2026-11-15 新增，修复记录第 341 节）。
///
/// 客户端链路（运行时 dump 确认）：
///   UMG.VirCapture.umg_vircapture_gacha? → VirCapture.DoSwitchDayNight()
///     自检 CheckOpenRegionConfig / GetOperateDataConfig.tbLightSwitchCondition
///     → me:CallGS("VirCapture_SwitchDayNight", {"nMapId": <等级Id>})
///   回调 s2c.tbCmds.VirCapture_SwitchDayNight:
///     UI.CloseConnection()
///     if rsp.sErr then UI.ShowTip 并返回
///     local isDay = VirCapture.GetDayNight()          -- 读 128/(mapBase+7)，1=白天
///     PalClient.SetWorldTimeType(isDay and ERegionTimeType.Day or ERegionTimeType.Night)
///     VirCaptureMain:OnReceiveUpdate() / PalBigMap:UpdateWorldTime()
///
/// 也就是说：服务端**只负责把 mapBase+7 的值翻过来**，
/// 光影/按钮由客户端根据这个值自己切换。之前这里是 EmptyAck（直接回 {}），
/// 值永远是 1（白天），所以点多少次都没反应。
///
/// 取值：1 = 白天（EnsureBaseMapState 的初值），2 = 夜晚。
/// 客户端 GetDayNight 用的是 "值 == 1" 的布尔判断，只有 1 算白天。
/// </summary>
[CallGSApi("VirCapture_SwitchDayNight")]
public class VirCapture_SwitchDayNight : ICallGSHandler
{
	private static readonly Logger Log = new Logger("VirCapture.DayNight");

	/// <summary>白天（与 EnsureBaseMapState 写入的初值一致）。</summary>
	private const uint DayValue = 1u;

	/// <summary>夜晚。</summary>
	private const uint NightValue = 2u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint levelId = ParseLevelId(param);
		PlayerInstance? player = connection.Player;
		if (player == null)
		{
			return;
		}
		if (levelId == 0u)
		{
			Log.Warn("SwitchDayNight 缺少 nMapId，param=" + param);
			await CallGSRouter.SendScript(connection, "VirCapture_SwitchDayNight", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint slot = VirCaptureStateHelper.FindOrAllocateMapSlot(player, levelId);
		if (slot == 0u)
		{
			Log.Warn($"SwitchDayNight 找不到地图槽，levelId={levelId}");
			await CallGSRouter.SendScript(connection, "VirCapture_SwitchDayNight", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VirCaptureStateHelper.EnsureBaseMapState(player, levelId, sync);
		uint sid = slot + VirCaptureStateHelper.OffDayNight;
		uint current = VirCaptureStateHelper.GetUnsignedAttr(player, sid);
		uint next = ((current == DayValue) ? NightValue : DayValue);
		VirCaptureStateHelper.SetUnsignedAttr(player, sid, next, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"SwitchDayNight levelId={levelId} slot={slot} {current} -> {next}");
		string rsp = $"{{\"nMapId\":{levelId},\"nDayNight\":{next}}}";
		await CallGSRouter.SendScript(connection, "VirCapture_SwitchDayNight", rsp, sync);
	}

	private static uint ParseLevelId(string? param)
	{
		if (string.IsNullOrWhiteSpace(param))
		{
			return 0u;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(param);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return 0u;
			}
			if (!document.RootElement.TryGetProperty("nMapId", out JsonElement value))
			{
				return 0u;
			}
			if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint num))
			{
				return num;
			}
			if (value.ValueKind == JsonValueKind.String && uint.TryParse(value.GetString(), out uint num2))
			{
				return num2;
			}
		}
		catch (JsonException)
		{
		}
		return 0u;
	}
}
