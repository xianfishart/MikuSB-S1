using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 2026-11-17（第 386 节）：星球守护（AlienDefense / DLC10）通关结算。
///
/// 修复前这里是空壳，只回 "{}"，什么都不落库 —— 于是：
///   · 关卡地图左下角「守护进度」永远是 0/6；
///   · 「伙伴手册 / 助战少女 / 机能图鉴」全部锁着，提示「通关【星球守护1】后解锁」。
/// 因为客户端这两处的解锁判据是通用条件 {1, 关卡id, 1}（aliendefense_girl.txt 的 Condition 列，
/// aliendefense_pal_main.txt 同款），类型 1 的判定就是 me:GetAttribute(22, levelId) ~= 0，
/// 与主线 / DLC 章节完全一致（见 DLC_Chapter_LevelSettlement.WriteLevelProgress）。
///
/// 结算入口是 Chapter_DealLevelSettlement 的 sCmd 分发：客户端把 sCmd=AlienDefense_LevelSettlement
/// 和 tbParam（nID=关卡id、nScore、tbKillInfo…）一起发上来，最终落到本类。
/// 落库口径与主线一致：
///   · (21, levelId) |= 1   —— 通关标记（位掩码，星球守护无星级，记 1 位即可）
///   · (22, levelId) += 1   —— 通关次数（条件判定与「守护进度」都只看非 0）
/// 另外顺手触发 TaskEventService.OnLevelClear，让任务 / 成就的「通关某关」进度同步。
///
/// 第 390 节补充：本类原先只写通关标记，不写积分，于是「不息守护」打完回主界面
/// 「最高积分」恒为 0。客户端口径（反汇编 bc2_GetMaxScoreAndWave / bc2_GetScore）：
///   GetScore()           = me:GetAttribute(143, AlienDefense.Score    = 2)
///   GetMaxScoreAndWave() = GetBits(me:GetAttribute(143, MaxScore = 6), 0, 15)   // 最高积分
///                        , GetBits(..., 16, 31)                                 // 最高波次
/// 即最高分与最高波次打包在同一条属性里，低 16 位积分、高 16 位波次
/// （aliendefense_level.txt 的 Scorelimit 列正是 65535，正好用满 16 位）。
/// 结算参数里 nScore / nWave 都带上来（实测 6910：nScore=5904、nWave=24）。
/// </summary>
[CallGSApi("AlienDefense_LevelSettlement")]
public class AlienDefense_LevelSettlement : ICallGSHandler
{
	/// <summary>
	/// 星球守护关卡 id 段（alienlist.txt 的 LevelList 全集）：
	///   6201~6210 / 6211~6220 / 6221~6230 / 6231~6236（普通关，第 4 期当前开放 6 关）
	///   6701~6723（精英关）、6901~6915（Boss 关）
	/// 只在这个范围里写通关属性，避免别的玩法误带同名 sCmd 时污染主线进度。
	/// </summary>
	private const uint LevelIdMin = 6201u;

	private const uint LevelIdMax = 6999u;

	private static readonly Logger Log = new Logger("AlienDefense");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint levelId = ParseUInt(param, "nID");
		uint score = ParseUInt(param, "nScore");
		uint wave = ParseUInt(param, "nWave");
		if (levelId >= LevelIdMin && levelId <= LevelIdMax)
		{
			WriteLevelClear(player, sync, levelId);
			WriteScore(player, sync, score, wave);
			DatabaseHelper.SaveDatabaseType(player.Data);
			try
			{
				TaskEventService.OnLevelClear(player, levelId);
			}
			catch (Exception ex)
			{
				Log.Warn($"AlienDefense OnLevelClear failed level={levelId} ex={ex.Message}");
			}
			Log.Info($"AlienDefense settlement level={levelId} score={score} wave={wave} cleared={CountCleared(player)}");
		}
		else
		{
			Log.Warn($"AlienDefense settlement skipped: bad levelId={levelId}");
		}
		await CallGSRouter.SendScript(connection, "AlienDefense_LevelSettlement", "{\"nScore\":" + score + ",\"tbAward\":[]}", sync);
	}

	private static uint ParseUInt(string param, string name)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty(name, out JsonElement node) && node.TryGetUInt32(out uint value))
			{
				return value;
			}
		}
		catch
		{
		}
		return 0u;
	}

	/// <summary>通关落库：(21,levelId) 置 1 位，(22,levelId) 累加。</summary>
	private static void WriteLevelClear(PlayerInstance player, NtfSyncPlayer sync, uint levelId)
	{
		PlayerAttr star = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == levelId);
		if (star == null)
		{
			star = new PlayerAttr
			{
				Gid = 21u,
				Sid = levelId,
				Val = 0u
			};
			player.Data.Attrs.Add(star);
		}
		star.Val |= 1u;
		sync.Custom[player.ToPackedAttrKey(21u, levelId)] = star.Val;
		sync.Custom[player.ToShiftedAttrKey(21u, levelId)] = star.Val;

		PlayerAttr pass = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == levelId);
		if (pass == null)
		{
			pass = new PlayerAttr
			{
				Gid = 22u,
				Sid = levelId,
				Val = 0u
			};
			player.Data.Attrs.Add(pass);
		}
		pass.Val++;
		sync.Custom[player.ToPackedAttrKey(22u, levelId)] = pass.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, levelId)] = pass.Val;
	}

	/// <summary>调试用：当前已通关的星球守护关卡数量（写日志，方便对照客户端「守护进度 N/6」）。</summary>
	private static int CountCleared(PlayerInstance player)
	{
		return player.Data.Attrs.Count((PlayerAttr x) => x.Gid == 22 && x.Val != 0 && x.Sid >= LevelIdMin && x.Sid <= LevelIdMax);
	}

	/// <summary>积分属性所在组（AlienDefense.GID）。</summary>
	private const uint AttrGid = 143u;

	/// <summary>本局积分（AlienDefense.Score）。</summary>
	private const uint SidScore = 2u;

	/// <summary>最高积分 + 最高波次打包（AlienDefense.MaxScore）：低 16 位积分、高 16 位波次。</summary>
	private const uint SidBest = 6u;

	/// <summary>
	/// 第 390 节写入 / 第 396 节修正：积分记录只增不减。
	///   · (143,2) = max(旧记录, 本局)。客户端两处都读它：
	///       - CheckCanGetScoreAward() 拿 GetScore() 跟每一档 award.nScore 比；
	///       - umg_mow_main.RefershLevel 用 GetScore() 填主界面「不息守护 · 积分」。
	///     所以它必须是「历史最高」，写成本局会被 0 分局直接盖掉（第 396 节就是这个现象）。
	///     结算面板的「本局得分」走回包里的 nScore，与这条属性无关。
	///   · (143,6) = max(旧最高积分, 本局) | (max(旧最高波次, 本局) << 16)。
	/// </summary>
	private static void WriteScore(PlayerInstance player, NtfSyncPlayer sync, uint score, uint wave)
	{
		uint oldScore = GetAttr(player, SidScore);
		uint newScore = Math.Max(oldScore, score);
		if (newScore != oldScore)
		{
			SetAttr(player, sync, SidScore, newScore);
		}

		uint old = GetAttr(player, SidBest);
		uint oldBest = old & 0xFFFFu;
		uint oldWave = (old >> 16) & 0xFFFFu;
		uint newBest = Math.Max(oldBest, score & 0xFFFFu);
		uint newWave = Math.Max(oldWave, wave & 0xFFFFu);
		uint packed = (newBest & 0xFFFFu) | ((newWave & 0xFFFFu) << 16);
		if (packed != old)
		{
			SetAttr(player, sync, SidBest, packed);
		}
	}

	private static uint GetAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == AttrGid && x.Sid == sid);
		return (attr != null) ? attr.Val : 0u;
	}

	private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == AttrGid && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = AttrGid,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = value;
		sync.Custom[player.ToPackedAttrKey(AttrGid, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(AttrGid, sid)] = attr.Val;
	}
}
