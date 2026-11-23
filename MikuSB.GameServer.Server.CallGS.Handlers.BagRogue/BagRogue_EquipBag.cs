using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_EquipBag")]
public class BagRogue_EquipBag : CallGSHandler<BagRogueEquipParam>
{
	private static readonly Logger Log = new Logger("BagRogue_EquipBag");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueEquipParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		string json = player.Attributes.GetStringValue(61u, 2u) ?? "{}";
		JsonObject jsonObject = (JsonNode.Parse(json) as JsonObject) ?? new JsonObject();
		bool value = default;
		foreach (JsonArray item in request.List ?? new List<JsonArray>())
		{
			if (item == null || item.Count < 2)
			{
				continue;
			}
			uint num = BagRogueService.ReadUint(item[0]);
			if (num != 0)
			{
				if ((item[1] is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value)) & value)
				{
					jsonObject.Remove($"A{num}");
					continue;
				}
				uint num2 = ((item.Count > 2) ? BagRogueService.ReadUint(item[2]) : 0u);
				uint num3 = ((item.Count > 3) ? BagRogueService.ReadUint(item[3]) : 0u);
				string propertyName = $"A{num}";
				InlineArray2<JsonNode> buffer = default;
				buffer[0] = num2;
				buffer[1] = num3;
				jsonObject[propertyName] = new JsonArray(buffer);
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerStrAttr attr = player.Attributes.SetString(61u, 2u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"BagRogue_EquipBag: {string.Join(",", (request.List ?? new List<JsonArray>()).Select((JsonArray a) => a?.ToJsonString() ?? "null"))} uid={player.Uid}");
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
