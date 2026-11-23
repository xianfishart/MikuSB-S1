using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 关卡结算。客户端只把 nID / nSeed / tbLog / nScoreLevel 发上来，评价面板是本地算的，
/// 但「这一关到底算不算通关」由服务端属性决定：
///   GirlRushLevelCfg:IsPass()          -> me:GetAttribute(Launch.GPASSID=22, 关卡ID) 大于 0
///   GirlRushLevelCfg:GetHistoryLevel() -> GetBits(me:GetAttribute(158, 关卡ID), 26, 30)
/// 以前这里直接回死数据、什么都不写，于是打完永远是「未通关 / 评价 0」，
/// 心绪补给里所有任务都停在「前往」。现在把用时和评价落库。
/// </summary>
[CallGSApi("GirlRush_LevelSettlement")]
public class GirlRush_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = GirlRushStore.ReadUint(request["nID"]);
		uint scoreLevel = GirlRushStore.ReadUint(request["nScoreLevel"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint passTime = 0;
		if (levelId != 0)
		{
			passTime = GirlRushStore.TakePassTime(player.Uid, levelId);
			GirlRushStore.WritePassTime(player, levelId, passTime);
			GirlRushStore.WriteRating(player, levelId, scoreLevel);
			SyncAttr(player, sync, GirlRushStore.PassGid, levelId);
			SyncAttr(player, sync, GirlRushStore.AttrGid, levelId);
			DatabaseHelper.SaveDatabaseType(player.Data);
			GirlRushStore.AppendDebug(
				$"[Settlement] level={levelId} passTime={passTime} scoreLevel={scoreLevel} " +
				$"attr158={player.Attributes.GetValue(GirlRushStore.AttrGid, levelId)} " +
				$"attr22={player.Attributes.GetValue(GirlRushStore.PassGid, levelId)} raw={param}");
		}
		JsonObject response = new JsonObject
		{
			["tbAward"] = new JsonArray(),
			["nScore"] = 0,
			["nPassTime"] = passTime,
			["bSuccess"] = true
		};
		await CallGSRouter.SendScript(connection, "GirlRush_LevelSettlement", response.ToJsonString(), sync);
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(gid, sid);
		player.Attributes.SyncTo(sync, attr);
	}
}
