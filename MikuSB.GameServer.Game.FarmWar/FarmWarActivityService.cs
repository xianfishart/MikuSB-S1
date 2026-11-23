using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.FarmWar;

public static class FarmWarActivityService
{
	private static readonly Logger Log = new Logger("FarmWarActivityService");

	private const uint RecordMask = 4095u;

	private const uint WaveMask = 511u;

	private const int WaveShift = 12;

	// 第 369 节：战技激活态存 (165, 技能节点 id)，培育点存 (1, 109)。
	// 客户端 Cash.GetMoneyCount(54) 读的是 (Cash.GroupId=1, 54*2+1=109)，
	// 激活按钮点下去发的请求是 { nId = 培育行号, nSkillId = 技能节点 id }。
	private const uint SkillGid = 165u;

	private const uint SkillPointGid = 1u;

	private const uint SkillPointSid = 109u;

	// 第 380 节（2026-11-17）：菜园百科「正面效果」里 47 个培育包恒为「?」。
	// 客户端 riki_ShowBuffInfo 判锁条件是 FarmWarActivity.GetBuffState(nId)
	// = me:GetAttribute(FarmWarActivity.Buff_GID, nId)：0=没见过 1=见过(商店出现过)
	// 2=已购买。客户端在 Req_LevelSettlement / Req_LevelFail 里调 GetUpdateBuff()，
	// 把本局新增状态塞进结算包字段 "F"（[[buffId,state],...]）。
	// 服务端此前完全不落库 → 169 组恒空 → 全部显示「没见过的培育包」。
	private const uint BuffGid = 169u;

	private const uint BuffStateSeen = 1u;

	private const uint BuffStateBought = 2u;

	private static Dictionary<uint, uint>? _skillCost;

	private static Dictionary<uint, List<uint>>? _skillChains;

	public static FarmWarActExcel? SelectAct(DateTime now)
	{
		// 第 424 节：先看本期 DLC 的 tbDlcFuncConfId.Farmwar 指向哪一期。
		// 第 34 期是 Farmwar=2（爆爆菜园第二期），但两期的原生窗口（2025-07 / 2026-01）
		// 按真实时钟都已过期，下面的时间匹配永远落空、兜底又会挑到第 1 期，
		// 客户端于是拿着第 1 期的配置画第二期的卡片（标题「活动结束」、关卡全锁）。
		uint eraAct = S1Admin.DlcEraFunc.Current("Farmwar");
		if (eraAct != 0 && GameData.FarmWarActData.TryGetValue(eraAct, out FarmWarActExcel eraRow))
		{
			return eraRow;
		}
		FarmWarActExcel farmWarActExcel = null;
		FarmWarActExcel farmWarActExcel2 = null;
		foreach (FarmWarActExcel item in GameData.FarmWarActData.Values.OrderBy((FarmWarActExcel a) => a.Id))
		{
			// 第 370 节（2026-11-17）：兜底期从「最后一期」改为「第一期」。
			// 两期窗口（2025-07 / 2026-01）都已过期，SelectAct 永远落到兜底；
			// 客户端 LocalGetCfg 按 DlcFuncWindowFix 改过的窗口只会选中第 1 期，
			// 服务端兜底给第 2 期会导致两侧期号错位（蔬菜/道具/章节全查 0 期）。
			farmWarActExcel2 ??= item;
			long num = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(item.Start));
			long num2 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(item.End));
			if (num != 0)
			{
				long num3 = new DateTimeOffset(now).ToUnixTimeSeconds();
				if (num3 >= num && (num2 == 0L || num3 <= num2) && (farmWarActExcel == null || item.Id > farmWarActExcel.Id))
				{
					farmWarActExcel = item;
				}
			}
		}
		return farmWarActExcel ?? farmWarActExcel2;
	}

	public static uint CurrentActId(PlayerInstance player)
	{
		return player.Attributes.GetValue(164u, 1u);
	}

	public static FarmWarActExcel? CurrentAct(PlayerInstance player)
	{
		uint num = CurrentActId(player);
		if (num != 0 && GameData.FarmWarActData.TryGetValue(num, out FarmWarActExcel value))
		{
			return value;
		}
		return SelectAct(DateTime.UtcNow);
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		FarmWarActExcel farmWarActExcel = SelectAct(DateTime.UtcNow);
		if (farmWarActExcel == null)
		{
			return;
		}
		PlayerAttr playerAttr = player.Attributes.Get(164u, 1u);
		if (playerAttr != null && playerAttr.Val == farmWarActExcel.Id)
		{
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, playerAttr);
			}
			return;
		}
		PlayerAttr playerAttr2 = player.Attributes.Set(164u, 1u, farmWarActExcel.Id);
		if (sync != null && playerAttr2 != null)
		{
			player.Attributes.SyncTo(sync, playerAttr2);
		}
	}

	public static bool IsSkillActive(PlayerInstance player, uint nodeId)
	{
		return player.Attributes.GetValue(SkillGid, nodeId) != 0;
	}

	/// <summary>
	/// 激活 / 重置某个战技节点。nodeId 必须是技能节点 id（10000+），不是培育行号。
	/// 重置会连带清掉同一条链上后续的节点（对应客户端提示“重置当前战技和后续战技”），
	/// 并按每个节点各自的消耗返还混合补剂。
	/// </summary>
	public static bool SetSkill(PlayerInstance player, uint nodeId, bool active, NtfSyncPlayer sync)
	{
		if (nodeId == 0)
		{
			return false;
		}
		List<uint> list = new List<uint> { nodeId };
		if (!active)
		{
			List<uint> skillChain = GetSkillChain(nodeId);
			int num = skillChain.IndexOf(nodeId);
			if (num >= 0)
			{
				for (int i = num + 1; i < skillChain.Count; i++)
				{
					list.Add(skillChain[i]);
				}
			}
		}
		uint cost = GetSkillCost(nodeId);
		if (active && cost != 0 && player.Attributes.GetValue(SkillPointGid, SkillPointSid) < cost)
		{
			return false;
		}
		bool flag = false;
		foreach (uint item in list)
		{
			bool num2 = player.Attributes.GetValue(SkillGid, item) != 0;
			if (num2 == active)
			{
				continue;
			}
			uint skillCost = GetSkillCost(item);
			if (skillCost != 0)
			{
				uint value = player.Attributes.GetValue(SkillPointGid, SkillPointSid);
				uint num3 = (active ? ((value >= skillCost) ? (value - skillCost) : 0u) : (value + skillCost));
				PlayerAttr attr = player.Attributes.Set(SkillPointGid, SkillPointSid, num3);
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, attr);
				}
			}
			PlayerAttr attr2 = player.Attributes.Set(SkillGid, item, active ? 1u : 0u);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr2);
			}
			flag = true;
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return true;
	}

	public static void ResetAllSkills(PlayerInstance player, NtfSyncPlayer sync)
	{
		uint num = 0u;
		List<uint> list = new List<uint>();
		foreach (PlayerAttr attr in player.Data.Attrs)
		{
			if (attr.Gid == SkillGid && attr.Val != 0)
			{
				list.Add(attr.Sid);
				num += GetSkillCost(attr.Sid);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		if (num != 0)
		{
			PlayerAttr attr2 = player.Attributes.Set(SkillPointGid, SkillPointSid, player.Attributes.GetValue(SkillPointGid, SkillPointSid) + num);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr2);
			}
		}
		foreach (uint item in list)
		{
			PlayerAttr attr3 = player.Attributes.Set(SkillGid, item, 0u);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr3);
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	// 节点消耗（混合补剂）来自 farmwarout_skill.json 的 ActiveCos 列。
	private static uint GetSkillCost(uint nodeId)
	{
		if (_skillCost == null)
		{
			Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "farmwar", "farmwarout_skill.json");
				if (File.Exists(path))
				{
					foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
					{
						if (uint.TryParse(item["ID"]?.ToString(), out uint result))
						{
							uint.TryParse(item["ActiveCos"]?.ToString(), out uint result2);
							dictionary[result] = result2;
						}
					}
				}
			}
			catch
			{
			}
			_skillCost = dictionary;
		}
		if (!_skillCost.TryGetValue(nodeId, out uint value))
		{
			return 0u;
		}
		return value;
	}

	// 培育行里同一条战技链的节点顺序，来自 farmwarout_develop.json 的 Node 列
	// （列里 5 位以上的数字才是技能节点 id，前面几位是蔬菜伙伴 id）。
	private static List<uint> GetSkillChain(uint nodeId)
	{
		if (_skillChains == null)
		{
			Dictionary<uint, List<uint>> dictionary = new Dictionary<uint, List<uint>>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "farmwar", "farmwarout_develop.json");
				if (File.Exists(path))
				{
					foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
					{
						if (!(item["Node"] is JArray jArray))
						{
							continue;
						}
						foreach (JToken item2 in jArray)
						{
							if (!(item2 is JArray jArray2))
							{
								continue;
							}
							List<uint> list = new List<uint>();
							foreach (JToken item3 in jArray2)
							{
								if (uint.TryParse(item3.ToString(), out uint result) && result >= 10000)
								{
									list.Add(result);
								}
							}
							foreach (uint item4 in list)
							{
								if (!dictionary.ContainsKey(item4))
								{
									dictionary[item4] = list;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
			_skillChains = dictionary;
		}
		if (!_skillChains.TryGetValue(nodeId, out List<uint> value))
		{
			return new List<uint>();
		}
		return value;
	}

	public static string? GetFormation(PlayerInstance player, uint levelId)
	{
		return player.Attributes.GetStringValue(104u, levelId);
	}

	public static void SetFormation(PlayerInstance player, uint levelId, string json, NtfSyncPlayer sync)
	{
		PlayerStrAttr attr = player.Attributes.SetString(104u, levelId, json);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	public static bool RecordClear(PlayerInstance player, uint levelId, uint healthPercent, uint wave, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Attributes.Get(22u, levelId);
		bool result = playerAttr == null || playerAttr.Val == 0;
		playerAttr = player.Attributes.Set(22u, levelId, (playerAttr?.Val ?? 0) + 1);
		player.Attributes.SyncTo(sync, playerAttr);
		PlayerAttr playerAttr2 = player.Attributes.Get(21u, levelId);
		uint num = playerAttr2?.Val ?? 0;
		uint val = num & 0xFFF;
		uint val2 = (num >> 12) & 0x1FF;
		uint val3 = Math.Min(healthPercent, 4095u);
		uint val4 = Math.Min(wave, 511u);
		uint val5 = Math.Max(val, val3) | (Math.Max(val2, val4) << 12);
		if (playerAttr2 == null)
		{
			playerAttr2 = player.Attributes.GetOrCreate(21u, levelId);
		}
		playerAttr2.Val = val5;
		player.Attributes.SyncTo(sync, playerAttr2);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return result;
	}

	// 第 380 节：把结算包 "F" 里的培育包状态落库（只增不减，取 max）。
	// F 的两种可能形态都兼容：[[buffId,state],...] 与 [{Id=..,State=..},...]。
	public static void RecordBuffStates(PlayerInstance player, JToken? buffs, NtfSyncPlayer? sync)
	{
		if (player == null || buffs is not JArray arr)
		{
			return;
		}
		int changed = 0;
		if (arr.Count > 0)
		{
			string raw = arr.ToString(Newtonsoft.Json.Formatting.None);
			Log.Info($"RecordBuffStates: payload n={arr.Count} raw={(raw.Length > 300 ? raw.Substring(0, 300) : raw)}");
		}
		foreach (JToken item in arr)
		{
			uint buffId;
			uint state;
			if (item is JArray pair)
			{
				buffId = ToUInt(pair.Count > 0 ? pair[0] : null);
				state = ToUInt(pair.Count > 1 ? pair[1] : null);
			}
			else if (item is JObject obj)
			{
				buffId = ToUInt(obj["Id"] ?? obj["nId"] ?? obj["buffId"] ?? obj["nBuffId"]);
				state = ToUInt(obj["State"] ?? obj["nState"] ?? obj["state"]);
				JToken buyFlag = obj["bIsBuy"];
				if (buyFlag != null && buyFlag.Type == JTokenType.Boolean && buyFlag.Value<bool>())
				{
					state = BuffStateBought;
				}
			}
			else
			{
				continue;
			}
			if (buffId == 0 || state == 0)
			{
				continue;
			}
			state = Math.Min(state, BuffStateBought);
			PlayerAttr attr = player.Attributes.Get(BuffGid, buffId);
			if (attr != null && attr.Val >= state)
			{
				continue;
			}
			if (attr == null)
			{
				attr = player.Attributes.GetOrCreate(BuffGid, buffId);
			}
			attr.Val = state;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
			changed++;
		}
		if (changed > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"RecordBuffStates: {changed} buff(s) unlocked.");
		}
	}

	private static uint ToUInt(JToken? token)
	{
		if (token == null)
		{
			return 0u;
		}
		switch (token.Type)
		{
		case JTokenType.Integer:
			return (uint)Math.Max(0L, token.Value<long>());
		case JTokenType.Float:
			return (uint)Math.Max(0.0, token.Value<double>());
		default:
			return uint.TryParse(token.ToString().Trim(), out uint parsed) ? parsed : 0u;
		}
	}

	public static void MarkQuestProgress(PlayerInstance player, FarmWarActExcel act, uint levelId, NtfSyncPlayer sync)
	{
		foreach (uint taskId in act.TaskIds)
		{
			if (!GameData.ActivityQuestData.TryGetValue(taskId, out ActivityQuestExcel value))
			{
				continue;
			}
			if (value.Type == 2)
			{
				uint num = ReadFirstUInt(value.Param);
				if (num != 0 && num != levelId)
				{
					continue;
				}
			}
			AchievementClaimService.Complete(player, 7u, taskId, 1u, sync);
		}
	}

	public static void LogUnknownStage(string api, uint levelId)
	{
		Log.Warn($"{api}: unknown farmwar stage {levelId}.");
	}

	private static uint ReadFirstUInt(JToken? token)
	{
		if (token is JArray { Count: >0 } jArray)
		{
			token = jArray[0];
		}
		uint result;
		return uint.TryParse(token?.ToString().Trim(), out result) ? result : 0u;
	}
}
