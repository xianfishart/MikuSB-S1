using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ChangeFurnitureFoldByTmpId")]
public class ChangeFurnitureFoldByTmpId : IHouseFuncHandler
{
	private static Dictionary<(int Area, int Tmp), int>? _support;

	private static Dictionary<(int Area, int Tmp), (int GroupId, int Index)>? _groups;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		JsonObject request = (root["tbParam"] as JsonObject) ?? root;
		int areaId = HouseJson.NumField(request, "AreaId");
		int tmpId = HouseJson.NumField(request, "TmpId");
		JsonNode jsonNode = request["IsFold"];
		bool fold = default;
		bool isFold = (jsonNode is JsonValue b && b.TryGetValue<bool>(out fold)) & fold;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (areaId >= 1 && areaId <= 50 && tmpId > 0 && LoadSupport().TryGetValue((areaId, tmpId), out var index) && index > 0)
		{
			uint sid = (uint)(areaId * 50 + 23);
			uint value = HouseAttr.Read(player, sid);
			uint mask = (uint)(1 << index);
			value = (isFold ? (value | mask) : (value & ~mask));
			await HouseAttr.SetAsync(connection, sid, value, sync);
			if (LoadGroups().TryGetValue((areaId, tmpId), out var group))
			{
				uint groupSid = (uint)(areaId * 50 + 20);
				uint groupValue = HouseAttr.Read(player, groupSid);
				int shift = (group.GroupId - 1) * 3;
				uint groupMask = (uint)(7 << shift);
				uint selected = (groupValue >> shift) & 7;
				if (!isFold)
				{
					groupValue = (groupValue & ~groupMask) | (uint)(group.Index << shift);
				}
				else if (selected == (uint)group.Index)
				{
					groupValue &= ~groupMask;
				}
				await HouseAttr.SetAsync(connection, groupSid, groupValue, sync);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		JsonObject response = new JsonObject
		{
			["FuncName"] = "FurnitureFoldChangeSuccess",
			["AreaId"] = areaId,
			["TmpId"] = tmpId,
			["IsFold"] = isFold,
			["bSuccess"] = true
		};
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}

	private static Dictionary<(int Area, int Tmp), (int GroupId, int Index)> LoadGroups()
	{
		if (_groups != null)
		{
			return _groups;
		}
		_groups = new Dictionary<(int, int), (int, int)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "FurniturePos.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item3 in jsonArray)
				{
					if (!(item3 is JsonObject jsonObject) || !(jsonObject["FurnitureTmpId"] is JsonArray jsonArray2))
					{
						continue;
					}
					int item = HouseJson.NumField(jsonObject, "AreaId");
					int item2 = HouseJson.NumField(jsonObject, "GroupId");
					for (int i = 0; i < jsonArray2.Count; i++)
					{
						if (jsonArray2[i] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value) && value > 0)
						{
							_groups[(item, value)] = (item2, i + 1);
						}
					}
				}
			}
		}
		catch
		{
		}
		return _groups;
	}

	private static Dictionary<(int Area, int Tmp), int> LoadSupport()
	{
		if (_support != null)
		{
			return _support;
		}
		_support = new Dictionary<(int, int), int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "support_furniture.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "AreaId");
						int num2 = HouseJson.NumField(obj, "FurnitureTmpId");
						int num3 = HouseJson.NumField(obj, "Index");
						if (num > 0 && num2 > 0 && num3 > 0)
						{
							_support[(num, num2)] = num3;
						}
					}
				}
			}
		}
		catch
		{
		}
		return _support;
	}
}
