using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_NewPrologueSettlement")]
public class Chapter_NewPrologueSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NewPrologueSettlementParam req = JsonSerializer.Deserialize<NewPrologueSettlementParam>(param);
		if (req == null || req.LevelId == 0)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		player.SetAttr(22u, req.LevelId, now);
		sync.Custom[player.ToPackedAttrKey(22u, req.LevelId)] = now;
		sync.Custom[player.ToShiftedAttrKey(22u, req.LevelId)] = now;
		if (req.TbLog?["LevelFinish"] != null)
		{
			PlayerAttr star = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == req.LevelId);
			if (star == null)
			{
				star = new PlayerAttr
				{
					Gid = 21u,
					Sid = req.LevelId,
					Val = 7u
				};
				player.Data.Attrs.Add(star);
			}
			star.Val = 7u;
			sync.Custom[player.ToPackedAttrKey(21u, req.LevelId)] = star.Val;
			sync.Custom[player.ToShiftedAttrKey(21u, req.LevelId)] = star.Val;
		}
		AchievementProgress.MarkLevel(player, req.LevelId, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["bWaitServer"] = req.WaitServer,
			["tbShowAward"] = new JsonArray()
		};
		await CallGSRouter.SendScript(connection, "Chapter_NewPrologueSettlement", rsp.ToJsonString(), sync);
	}
}
