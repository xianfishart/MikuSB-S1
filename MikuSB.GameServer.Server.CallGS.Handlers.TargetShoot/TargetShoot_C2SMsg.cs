using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.TargetShoot;

[CallGSApi("TargetShoot_C2SMsg")]
public class TargetShoot_C2SMsg : ICallGSHandler
{
	private const uint Gid = 106u;

	private const uint HighestScoreSid = 1u;

	private const uint FirstEnterSid = 2u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject req = HouseShim.Parse(param);
		string func = req?["FuncName"]?.ToString() ?? "";
		NtfSyncPlayer sync = new NtfSyncPlayer();
		string text = func;
		string text2 = text;
		if (!(text2 == "RecordHighestScore"))
		{
			if (!(text2 == "RecordFirstEnter"))
			{
				await CallGSRouter.SendScript(connection, "TargetShoot_S2CMsg", new JsonObject { ["FuncName"] = func }.ToJsonString());
				return;
			}
			PlayerAttr attr = GetOrCreate(player, 2u);
			attr.Val = 1u;
			sync.Custom[player.ToPackedAttrKey(106u, 2u)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(106u, 2u)] = attr.Val;
			DatabaseHelper.SaveDatabaseType(player.Data);
			await CallGSRouter.SendScript(connection, "TargetShoot_S2CMsg", new JsonObject { ["FuncName"] = "RecordFirstEnter" }.ToJsonString(), sync);
			return;
		}
		uint score = ReadUInt(req?["Score"]);
		PlayerAttr attr2 = GetOrCreate(player, 1u);
		if (score > attr2.Val)
		{
			attr2.Val = score;
		}
		sync.Custom[player.ToPackedAttrKey(106u, 1u)] = attr2.Val;
		sync.Custom[player.ToShiftedAttrKey(106u, 1u)] = attr2.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "TargetShoot_S2CMsg", new JsonObject
		{
			["FuncName"] = "RecordHighestScore",
			["Score"] = attr2.Val
		}.ToJsonString(), sync);
	}

	private static PlayerAttr GetOrCreate(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 106 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 106u,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		return playerAttr;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
