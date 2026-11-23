using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 2026-11-17（第 391 节）：星球守护「积分奖励」无法领取。
///
/// 修复前是空壳，只回 "{}"，既不校验也不发奖、不记已领，
/// 客户端点「一键领取」后属性没变化，按钮永远可点但拿不到东西。
///
/// 客户端口径（反汇编 bc3_GetScoreAward / bc3_CheckScoreGot / bc3_CheckCanGetScoreAward）：
///   · 请求体只有 nIdx，-1 = 一键领取全部可领档，>0 = 单档；
///   · 领取前本地已判过「够分」与「未领」，但服务端必须自己再判一遍；
///   · 已领状态来自位图属性：GetBits(me:GetAttribute(143, sid), bit, bit) ~= 0
///       档位 1~31 → sid = AlienDefense.AwardGot   = 3，bit = idx
///       档位 32+  → sid = AlienDefense.AwardGot_2 = 4，bit = idx-32
///     注意 bit 就是档位序号本身（不是 idx-1），bit0 闲置：这是实测结论，
///     置 (143,3)=7 时 CheckScoreGot(1)(2) 为 true、(3) 为 false 可反推。
///     （本期配置 50 档，正好拆两段 32 位）
///   · 客户端 CallGS 不带回调，界面刷新完全靠属性同步，
///     所以只要把位图写回去并随 NtfSyncPlayer 下发即可。
///
/// 配置来源：GameData.AlienDefenseAwardData（aliendefenseaward 表，170 条）
///   SeasonID=期号（本期 4）、Index=档位序号（1 起）、NeedScore=所需累计积分、
///   ScoreAward=奖励列表，元素为 {Genre,Detail,Particular,Level,Count} 五元组。
/// </summary>
[CallGSApi("AlienDefense_GetScoreAward")]
public class AlienDefense_GetScoreAward : ICallGSHandler
{
	private const uint Gid = 143u;

	private const uint SidScore = 2u;

	/// <summary>已领位图低段（档位 1~32）。</summary>
	private const uint SidGotLo = 3u;

	/// <summary>已领位图高段（档位 33~64）。</summary>
	private const uint SidGotHi = 4u;

	private const uint SidSeason = 1u;

	private static readonly Logger Log = new Logger("AlienDefense");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint score = GetAttr(player, SidScore);
		uint season = GetAttr(player, SidSeason);
		int wantIdx = ParseInt(param, "nIdx");
		List<int> grantedIdx = new List<int>();
		int awardCount = 0;
		// GameData.AlienDefenseAwardData 是两层字典：外层 key = 期号，内层 key = 档位序号。
		foreach (KeyValuePair<uint, Dictionary<uint, AlienDefenseAwardExcel>> seasonPair in GameData.AlienDefenseAwardData)
		{
			if (season != 0 && seasonPair.Key != season)
			{
				continue;
			}
			foreach (var pair in seasonPair.Value.OrderBy((KeyValuePair<uint, AlienDefenseAwardExcel> x) => x.Key))
			{
				uint index = pair.Key;
				AlienDefenseAwardExcel row = pair.Value;
				if (row == null)
				{
					continue;
				}
				if (row.NeedScore > score)
				{
					continue;
				}
				if (wantIdx > 0 && index != (uint)wantIdx)
				{
					continue;
				}
				if (IsGot(player, index))
				{
					continue;
				}
				foreach (List<uint> award in ParseAwards(row.ScoreAward))
				{
					if (award.Count < 5 || award[4] == 0)
					{
						continue;
					}
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, award);
					awardCount++;
				}
				MarkGot(player, index);
				grantedIdx.Add((int)index);
			}
		}
		SyncBitmap(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"AlienDefense score award: nIdx={wantIdx} score={score} season={season} got=[{string.Join(",", grantedIdx)}] items={awardCount}");
		await CallGSRouter.SendScript(connection, "AlienDefense_GetScoreAward", "{}", sync);
	}

	/// <summary>
	/// 已领判定。位号实测就是档位序号本身（不是 index-1）：
	///   置 (143,3)=7（bit0/1/2）后，CheckScoreGot(1)(2) 为 true 而 (3) 为 false
	///   → 客户端读的是 GetBits(v, idx, idx)，bit0 实际闲置。
	/// 每段 32 位：档位 1~31 落 (143,3)，32 及以上落 (143,4)。
	/// </summary>
	private static bool IsGot(PlayerInstance player, uint index)
	{
		if (index == 0)
		{
			return true;
		}
		uint sid = (index < 32) ? SidGotLo : SidGotHi;
		uint shift = (index < 32) ? index : (index - 32);
		if (shift >= 32)
		{
			return true;
		}
		return ((GetAttr(player, sid) >> (int)shift) & 1u) != 0;
	}

	private static void MarkGot(PlayerInstance player, uint index)
	{
		if (index == 0)
		{
			return;
		}
		uint sid = (index < 32) ? SidGotLo : SidGotHi;
		uint shift = (index < 32) ? index : (index - 32);
		if (shift >= 32)
		{
			return;
		}
		uint value = GetAttr(player, sid) | (1u << (int)shift);
		SetAttr(player, sid, value);
	}

	private static void SyncBitmap(PlayerInstance player, NtfSyncPlayer sync)
	{
		uint lo = GetAttr(player, SidGotLo);
		uint hi = GetAttr(player, SidGotHi);
		sync.Custom[player.ToPackedAttrKey(Gid, SidGotLo)] = lo;
		sync.Custom[player.ToShiftedAttrKey(Gid, SidGotLo)] = lo;
		sync.Custom[player.ToPackedAttrKey(Gid, SidGotHi)] = hi;
		sync.Custom[player.ToShiftedAttrKey(Gid, SidGotHi)] = hi;
	}

	/// <summary>
	/// 奖励列是 JSON token，配置写成 {{4,3,1,1,6000}} —— 一层奖励可能含多组五元组。
	/// 这里兼容两种形态：数组套数组（标准）与单个五元组数组（兜底）。
	/// </summary>
	private static List<List<uint>> ParseAwards(JToken token)
	{
		List<List<uint>> result = new List<List<uint>>();
		if (token == null || token.Type != JTokenType.Array)
		{
			return result;
		}
		JArray arr = (JArray)token;
		if (arr.Count == 0)
		{
			return result;
		}
		if (arr[0].Type == JTokenType.Array)
		{
			foreach (JToken item in arr)
			{
				if (item.Type != JTokenType.Array)
				{
					continue;
				}
				List<uint> one = new List<uint>();
				foreach (JToken v in item)
				{
					one.Add(v.Value<uint>());
				}
				if (one.Count > 0)
				{
					result.Add(one);
				}
			}
			return result;
		}
		List<uint> flat = new List<uint>();
		foreach (JToken v2 in arr)
		{
			flat.Add(v2.Value<uint>());
		}
		if (flat.Count > 0)
		{
			result.Add(flat);
		}
		return result;
	}

	private static int ParseInt(string param, string name)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty(name, out JsonElement node) && node.TryGetInt32(out int value))
			{
				return value;
			}
		}
		catch
		{
		}
		return 0;
	}

	private static uint GetAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == sid);
		return (attr != null) ? attr.Val : 0u;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == Gid && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = Gid,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = value;
	}
}
