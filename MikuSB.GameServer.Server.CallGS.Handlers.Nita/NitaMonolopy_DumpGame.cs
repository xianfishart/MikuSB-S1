using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

/// <summary>
/// 妮塔之邀（NitaMonolopy）棋盘状态上报。
///
/// 关键点一：必须把请求里的 <c>nUniqueNum</c> 原样回显。
/// 客户端在 <c>EventSystem</c> 上注册了 <c>NitaMonolopy_DumpGameRet</c>，
/// 回包一到就按 <c>nUniqueNum</c> 对账，一致才继续 <c>Game:NextStep()</c>。
///
/// 关键点二：存档必须落库到字符串属性 GID=102，否则「保存退出」等于没保存。
/// 客户端 <c>NitaMonolopy.RecoverGame</c>（源码 2390~2424）读的是：
///   me:GetStrAttribute(NitaMonolopy.STRGID=102, STRSID.BaseGameData=1)      → 整盘 JSON
///   me:GetStrAttribute(102, STRSID.CellDataBegin + i = 10 + i)              → 第 i 格 JSON
///
/// 关键点三：落库之外还必须**实时推送 1012 NtfSetStrAttr**。
/// 客户端的字符串属性缓存只在登录（RspLogin 全量）和收到 1012 时更新，
/// 只写数据库的话，本次会话里 <c>me:GetStrAttribute(102, 1)</c> 依旧返回空串，
/// 玩家「保存退出」再进棋盘时 RecoverGame 读不到存档 → 当场重开一局 →
/// 新局的 Dump 又把旧存档覆盖掉，表现为「每次都从头开始」。
///
/// <c>tbCellData</c> 是**增量**的（只带变化过的格子，形如 {"0":"{...}"} 或 [null,"{...}"]），
/// 因此只覆盖本次给出的格子，不能清空未出现的格子；只有整盘重开时才整体清空。
/// </summary>
[CallGSApi("NitaMonolopy_DumpGame")]
public class NitaMonolopy_DumpGame : CallGSHandler<NitaMonolopyDumpGameParam>
{
	private static readonly Logger Log = new Logger("NitaMonolopy_DumpGame");

	/// <summary>客户端 NitaMonolopy.STRGID。</summary>
	private const uint StrGid = 102u;

	/// <summary>客户端 NitaMonolopy.STRSID.BaseGameData：整盘 Game:Dump() 的 JSON。</summary>
	private const uint SidBaseGameData = 1u;

	/// <summary>客户端 NitaMonolopy.STRSID.CellDataBegin：第 i 格 = 10 + i。</summary>
	private const uint SidCellDataBegin = 10u;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, NitaMonolopyDumpGameParam request)
	{
		string gameData = request.GameData ?? string.Empty;
		PlayerInstance player = context.Player;
		List<KeyValuePair<uint, string>> changed = new List<KeyValuePair<uint, string>>();
		if (player != null && !string.IsNullOrEmpty(gameData))
		{
			try
			{
				SaveBoard(player, gameData, request.CellData, changed);
			}
			catch (Exception ex)
			{
				Log.Warn($"Nita dump save fail uid={player.Uid}: {ex.Message}");
			}
		}
		Log.Info($"Nita dump #{request.UniqueNum} uid={player?.Uid} cells={(request.CellData == null ? 0 : 1)} saved={changed.Count} data={gameData}{(string.IsNullOrEmpty(request.Log) ? string.Empty : " log=" + request.Log)}");

		if (player != null && changed.Count > 0)
		{
			// 1004 同步包兜底：部分界面走 CustomStr 合并，和 1012 双发确保缓存一定刷新。
			NtfSyncPlayer strSync = new NtfSyncPlayer();
			foreach (KeyValuePair<uint, string> item in changed)
			{
				strSync.CustomStr[player.ToShiftedAttrKey(StrGid, item.Key)] = item.Value;
			}
			try
			{
				await player.SendPacket(1004, strSync);
			}
			catch (Exception ex)
			{
				Log.Warn($"Nita dump sync fail uid={player.Uid}: {ex.Message}");
			}
		}

		if (player != null)
		{
			foreach (KeyValuePair<uint, string> item in changed)
			{
				try
				{
					await player.SendPacket(1012, new NtfSetStrAttr
					{
						Gid = StrGid,
						Sid = item.Key,
						Val = item.Value
					});
				}
				catch (Exception ex)
				{
					Log.Warn($"Nita dump push fail uid={player.Uid} sid={item.Key}: {ex.Message}");
				}
			}
		}

		return CallGSResult.Ok(new JsonObject
		{
			["nUniqueNum"] = request.UniqueNum,
		});
	}

	/// <summary>整盘重开的判定：还在起点、钱是初始 300、步数 0。</summary>
	private static bool IsFreshGame(string gameData)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(gameData);
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}
			int step = root.TryGetProperty("CurStep", out JsonElement eStep) && eStep.ValueKind == JsonValueKind.Number ? eStep.GetInt32() : -1;
			int cellIndex = root.TryGetProperty("CurCellIndex", out JsonElement eCell) && eCell.ValueKind == JsonValueKind.Number ? eCell.GetInt32() : -1;
			int money = root.TryGetProperty("CurMoney", out JsonElement eMoney) && eMoney.ValueKind == JsonValueKind.Number ? eMoney.GetInt32() : -1;
			return step == 0 && cellIndex == 0 && money == 300;
		}
		catch
		{
			return false;
		}
	}

	private static void SaveBoard(PlayerInstance player, string gameData, JsonNode? cellData, List<KeyValuePair<uint, string>> changed)
	{
		List<PlayerStrAttr> attrs = player.Data.StrAttrs;
		if (IsFreshGame(gameData))
		{
			// 新开一局：上一局残留的棋盘格子必须清掉，否则 RecoverGame 会把旧格子读回来。
			for (int i = attrs.Count - 1; i >= 0; i--)
			{
				if (attrs[i].Gid == StrGid)
				{
					uint oldSid = attrs[i].Sid;
					attrs.RemoveAt(i);
					// 同步把客户端缓存里的旧格子清空，否则客户端仍会看到上一局的格子。
					changed.Add(new KeyValuePair<uint, string>(oldSid, string.Empty));
				}
			}
		}
		SetAttr(player, SidBaseGameData, gameData, changed);
		if (cellData is JsonObject obj)
		{
			foreach (KeyValuePair<string, JsonNode?> kv in obj)
			{
				if (!uint.TryParse(kv.Key, out uint index))
				{
					continue;
				}
				string? value = AsCellJson(kv.Value);
				if (value != null)
				{
					SetAttr(player, SidCellDataBegin + index, value, changed);
				}
			}
		}
		else if (cellData is JsonArray arr)
		{
			for (int i = 0; i < arr.Count; i++)
			{
				string? value = AsCellJson(arr[i]);
				if (value != null)
				{
					SetAttr(player, SidCellDataBegin + (uint)i, value, changed);
				}
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	/// <summary>格子数据可能是 JSON 字符串（"{\"Index\":0}"），也可能已经是对象。</summary>
	private static string? AsCellJson(JsonNode? node)
	{
		if (node == null)
		{
			return null;
		}
		if (node is JsonValue value)
		{
			if (value.TryGetValue(out string? s))
			{
				return string.IsNullOrEmpty(s) ? null : s;
			}
			return null;
		}
		return node.ToJsonString();
	}

	private static void SetAttr(PlayerInstance player, uint sid, string value, List<KeyValuePair<uint, string>> changed)
	{
		PlayerStrAttr? attr = player.Data.StrAttrs.FirstOrDefault(a => a.Gid == StrGid && a.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerStrAttr
			{
				Gid = StrGid,
				Sid = sid
			};
			player.Data.StrAttrs.Add(attr);
		}
		attr.Val = value;
		changed.Add(new KeyValuePair<uint, string>(sid, value));
	}
}
