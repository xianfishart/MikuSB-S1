using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_LevelSettlement")]
public class VirCaptureTower_LevelSettlement : ICallGSHandler
{
	private const uint LaunchLevelStateGroupId = 21u;

	private const uint LaunchPassGroupId = 22u;

	private const uint PassedFlagBit = 256u;

	private static readonly Logger Logger = new Logger("VirCaptureTower");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonNode Response, NtfSyncPlayer Sync) tuple = await HandleSettlementAsync(connection.Player, JsonNode.Parse(param));
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Sync, connection: connection, api: "VirCaptureTower_LevelSettlement", arg: response.ToJsonString());
	}

	public static async Task<(JsonNode Response, NtfSyncPlayer Sync)> HandleSettlementAsync(PlayerInstance player, JsonNode? tbParam)
	{
		VirCaptureTowerSettlementParam req = ((tbParam != null) ? tbParam.Deserialize<VirCaptureTowerSettlementParam>() : null);
		if (req == null || req.LevelId == 0)
		{
			Logger.Error("Invalid vircapture tower settlement payload: " + (tbParam?.ToJsonString() ?? "null"));
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool wasPassed = player.IsLevelReallyPassed((uint)req.LevelId);
		PlayerAttr levelStateAttr = GetOrCreateAttr(player.Data, 21u, (uint)req.LevelId);
		levelStateAttr.Val |= MergeStarMask(req.StarMask) | 0x100;
		SyncAttr(sync, player, levelStateAttr);
		PlayerAttr passAttr = GetOrCreateAttr(player.Data, 22u, (uint)req.LevelId);
		passAttr.Val = Math.Max(1u, passAttr.Val + 1);
		SyncAttr(sync, player, passAttr);
		if (GameData.VirCaptureTowerData.TryGetValue((uint)req.LevelId, out VirCaptureTowerExcel config))
		{
			await player.RewardManager.GrantLevelRewardsAsync(config, !wasPassed, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync);
		}
		Logger.Info($"VirCaptureTower settlement saved. uid={player.Uid} levelId={req.LevelId} starMask={req.StarMask} levelStateVal={levelStateAttr.Val} passVal={passAttr.Val}");
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return (Response: new JsonObject(), Sync: sync);
	}

	private static uint MergeStarMask(int starMask)
	{
		uint num = 0u;
		for (int i = 0; i < 3; i++)
		{
			if (((starMask >> i) & 1) != 0)
			{
				num |= (uint)(1 << i);
			}
		}
		return num;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
