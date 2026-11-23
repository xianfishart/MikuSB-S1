using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

[CallGSApi("TokenBox_Use")]
public class TokenBox_Use : CallGSHandler<TokenBoxUseParam>
{
	private readonly record struct BoxUse(BaseGameItemInfo Item, uint BoxIndex, List<BoxExcel> Rows);

	private readonly record struct AwardKey(uint Genre, uint Detail, uint Particular, uint Level)
	{
		public override int GetHashCode()
		{
			return (int)(Genre * 31337 + Detail * 7919 + Particular * 131 + Level);
		}
	}

	private static readonly Logger Log = new Logger("TokenBox_Use");

	private const int MaxUseCount = 99;

	private const string TokenBoxLuaType = "token_box";

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, TokenBoxUseParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (request.TbList == null || request.TbList.Count == 0 || request.TbList.Count > 99)
		{
			return CallGSResult.Error("error.BadParam");
		}
		InventoryData inventory = player.InventoryManager.InventoryData;
		List<BoxUse> uses = new List<BoxUse>();
		Dictionary<uint, uint> listed = new Dictionary<uint, uint>();
		foreach (uint rowId in request.TbList)
		{
			listed[rowId] = listed.GetValueOrDefault(rowId) + 1;
			if (!inventory.Items.TryGetValue(rowId, out BaseGameItemInfo item) || item.ItemCount < listed[rowId])
			{
				Log.Warn($"TokenBox_Use uid={player.Uid}: row {rowId} is missing, empty, or listed more " + "often than it holds; nothing consumed.");
				return CallGSResult.Error("error.BadParam");
			}
			if (!ItemTemplateInfo.TryResolve(item.TemplateId, out var template) || template.LuaType != "token_box" || template.Param1 == 0)
			{
				Log.Warn($"TokenBox_Use uid={player.Uid}: row {rowId} (template={item.TemplateId}) is not a " + "token_box; nothing consumed.");
				return CallGSResult.Error("error.BadParam");
			}
			if (!GameData.BoxData.TryGetValue(template.Param1, out List<BoxExcel> rows))
			{
				Log.Warn($"TokenBox_Use uid={player.Uid}: token_box {item.TemplateId} points at box {template.Param1}, which has no rows in item/box.json; nothing consumed.");
				return CallGSResult.Error("error.BadParam");
			}
			uses.Add(new BoxUse(item, template.Param1, rows));
			item = null;
			template = default;
			rows = null;
		}
		Dictionary<AwardKey, ulong> awardTotals = new Dictionary<AwardKey, ulong>();
		foreach (BoxUse use in uses)
		{
			List<uint[]> awards = SelectAwards(use.Rows);
			if (awards.Count == 0)
			{
				Log.Warn($"TokenBox_Use uid={player.Uid}: box {use.BoxIndex} produced no award rows; " + "nothing consumed.");
				return CallGSResult.Error("error.BadParam");
			}
			foreach (uint[] row in awards)
			{
				AddAward(awardTotals, row);
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<BaseGameItemInfo> touched = new List<BaseGameItemInfo>();
		foreach (BoxUse use2 in uses)
		{
			use2.Item.ItemCount--;
			if (!touched.Contains(use2.Item))
			{
				touched.Add(use2.Item);
			}
		}
		foreach (BaseGameItemInfo item2 in touched)
		{
			sync.Items.Add(ItemTemplateInfo.ToSyncProto(item2));
		}
		List<IReadOnlyList<uint>> rewardRows = new List<IReadOnlyList<uint>>();
		foreach (var (key, count) in awardTotals)
		{
			uint[] row2 = new uint[5]
			{
				key.Genre,
				key.Detail,
				key.Particular,
				key.Level,
				(uint)count
			};
			rewardRows.Add(row2);
		}
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
		if (granted.Count < rewardRows.Count)
		{
			Log.Warn($"TokenBox_Use uid={player.Uid}: {rewardRows.Count - granted.Count} of {rewardRows.Count} award row(s) could not be granted; see the Reward log.");
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(inventory);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject payload = new JsonObject { ["tbAward"] = granted };
		Log.Info($"TokenBox_Use uid={player.Uid} used {uses.Count} token box(es) from {touched.Count} stack(s) (box {uses[0].BoxIndex}) and granted {granted.Count} row(s): {Describe(granted)}.");
		return CallGSResult.Ok(payload.ToJsonString(), sync);
	}

	private static List<uint[]> SelectAwards(List<BoxExcel> rows)
	{
		List<uint[]> list = new List<uint[]>();
		foreach (IGrouping<uint, BoxExcel> item in from x in rows
			where x.IsSelect
			group x by x.Group)
		{
			list.Add(item.First().Reward.ToArray());
		}
		foreach (BoxExcel item2 in rows.Where((BoxExcel x) => x.IsFix))
		{
			list.Add(item2.Reward.ToArray());
		}
		foreach (IGrouping<uint, BoxExcel> item3 in from x in rows
			where x.IsRandom
			group x by x.Group)
		{
			List<BoxExcel> list2 = item3.ToList();
			ulong num = list2.Aggregate(0uL, (ulong sum, BoxExcel x) => sum + x.Weight);
			if (num == 0)
			{
				list.Add(list2[0].Reward.ToArray());
				continue;
			}
			ulong num2 = (ulong)Random.Shared.NextInt64(0L, (long)num);
			ulong num3 = 0uL;
			BoxExcel boxExcel = list2[list2.Count - 1];
			foreach (BoxExcel item4 in list2)
			{
				num3 += item4.Weight;
				if (num2 < num3)
				{
					boxExcel = item4;
					break;
				}
			}
			list.Add(boxExcel.Reward.ToArray());
		}
		foreach (BoxExcel item5 in rows.Where((BoxExcel x) => x.IsProb))
		{
			if (item5.Weight == 0 || Random.Shared.Next(10000) < item5.Weight)
			{
				list.Add(item5.Reward.ToArray());
			}
		}
		return list;
	}

	private static void AddAward(Dictionary<AwardKey, ulong> totals, IReadOnlyList<uint> row)
	{
		AwardKey key = new AwardKey(row[0], row[1], row[2], row[3]);
		totals[key] = totals.GetValueOrDefault(key) + row[4];
	}

	private static string Describe(JsonArray rows)
	{
		return (rows.Count == 0) ? "none" : string.Join(", ", rows.Select((JsonNode x) => x?.ToJsonString() ?? "null"));
	}
}
