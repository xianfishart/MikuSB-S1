using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_ChangeFormation")]
public class VirCapture_ChangeFormation : ICallGSHandler
{
	private const uint StrGroupId = 57u;

	private const uint FormationSid = 1u;

	private const uint VirCaptureGroupId = 128u;

	private const uint CurLevelSid = 3u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureChangeFormationParam req = JsonSerializer.Deserialize<VirCaptureChangeFormationParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		List<uint> formation = ReadFormation(player);
		uint addId = (uint)Math.Max(0, req.Id);
		uint unloadId = (uint)Math.Max(0, req.UnloadId);
		int unloadIndex = ((unloadId == 0) ? (-1) : formation.FindIndex((uint x) => x == unloadId));
		if (unloadId != 0 && unloadIndex < 0)
		{
			await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (addId != 0)
		{
			if (formation.Contains(addId))
			{
				await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			BaseGameItemInfo addItem = player.InventoryManager.GetNormalItem(addId);
			if (addItem == null || addItem.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
			{
				await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
		}
		if (unloadIndex >= 0)
		{
			formation.RemoveAt(unloadIndex);
		}
		if (addId != 0)
		{
			if (unloadIndex >= 0 && unloadIndex <= formation.Count)
			{
				formation.Insert(unloadIndex, addId);
			}
			else
			{
				formation.Add(addId);
			}
		}
		if (!ValidateFormation(player, formation))
		{
			await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		string json = JsonSerializer.Serialize(formation);
		player.SetStrAttr(57u, 1u, json);
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.CustomStr[player.ToShiftedAttrKey(57u, 1u)] = json;
		JsonObject response = new JsonObject
		{
			["nId"] = req.Id,
			["nUnloadId"] = req.UnloadId,
			["bAdd"] = addId != 0
		};
		await CallGSRouter.SendScript(connection, "VirCapture_ChangeFormation", response.ToJsonString(), sync);
	}

	private static List<uint> ReadFormation(PlayerInstance player)
	{
		string text = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 57 && x.Sid == 1)?.Val;
		if (string.IsNullOrWhiteSpace(text))
		{
			return new List<uint>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<uint>>(text) ?? new List<uint>();
		}
		catch
		{
			return new List<uint>();
		}
	}

	private static bool ValidateFormation(PlayerInstance player, List<uint> formation)
	{
		uint key = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == 3)?.Val ?? 1;
		if (!GameData.VirCaptureLevelListData.TryGetValue(key, out VirCaptureLevelListExcel value))
		{
			return formation.Count == 0;
		}
		if (formation.Count > value.Num)
		{
			return false;
		}
		uint num = 0u;
		foreach (uint item in formation)
		{
			BaseGameItemInfo normalItem = player.InventoryManager.GetNormalItem(item);
			if (normalItem == null || normalItem.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
			{
				return false;
			}
			if (!GameData.MonsterCardData.TryGetValue(normalItem.TemplateId, out MonsterCardExcel value2))
			{
				return false;
			}
			num += value2.CostValue;
		}
		return num <= value.MaxCost;
	}
}
