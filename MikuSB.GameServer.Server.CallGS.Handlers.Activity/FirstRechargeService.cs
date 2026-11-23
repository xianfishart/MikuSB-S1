using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

public static class FirstRechargeService
{
	private const int ActivityId = 14;

	private const uint ActivityGroup = 50u;

	private const uint ClaimedSid = 143u;

	public static async Task Ensure(Connection connection)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		PlayerAttr flag = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == 143);
		if (flag != null && flag.Val != 0)
		{
			return;
		}
		JsonNode jsonNode = LoadActivity()?["CustomData"];
		if (!(jsonNode is JsonArray rewards))
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (JsonArray node in rewards.OfType<JsonArray>())
		{
			List<uint> reward = node.Select((JsonNode x) => (uint)ReadInt(x)).Take(5).ToList();
			if (reward.Count >= 5 && reward[0] != 0)
			{
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			}
		}
		if (flag == null)
		{
			flag = new PlayerAttr
			{
				Gid = 50u,
				Sid = 143u,
				Val = 1u
			};
			player.Data.Attrs.Add(flag);
		}
		else
		{
			flag.Val = 1u;
		}
		sync.Custom[player.ToPackedAttrKey(50u, 143u)] = 1u;
		sync.Custom[player.ToShiftedAttrKey(50u, 143u)] = 1u;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		await connection.SendPacket(1004, sync, 0);
	}

	private static JsonObject? LoadActivity()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "activity", "activities.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			return (jsonArray != null) ? jsonArray.OfType<JsonObject>().FirstOrDefault((JsonObject x) => ReadInt(x["Id"]) == 14) : null;
		}
		catch
		{
			return null;
		}
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
