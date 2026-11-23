using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("DLC_Chapter_LevelSettlement")]
public class DLC_Chapter_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int levelId = ParseLevelId(param);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (levelId > 0)
		{
			WriteLevelProgress(player, sync, levelId, ParseStar(param));
			foreach (JsonObject row in DlcMissionStore.ActiveRows())
			{
				if (DlcMissionStore.ReadInt(row, "Type") == 2 && ParamContains(row, levelId))
				{
					int missionId = DlcMissionStore.ReadInt(row, "MissionID");
					if (missionId > 0)
					{
						MarkCompleted(player, sync, missionId, DlcMissionStore.ReadInt(row, "CheckValue"));
					}
				}
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "DLC_Chapter_LevelSettlement", "{\"tbAward\":[],\"tbStar\":[]}", sync);
	}

	private static void WriteLevelProgress(PlayerInstance player, NtfSyncPlayer sync, int levelId, int starMask)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == (uint)levelId);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 21u,
				Sid = (uint)levelId,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val |= (uint)(starMask & 7);
		sync.Custom[player.ToPackedAttrKey(21u, (uint)levelId)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(21u, (uint)levelId)] = playerAttr.Val;
		PlayerAttr playerAttr2 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == (uint)levelId);
		if (playerAttr2 == null)
		{
			playerAttr2 = new PlayerAttr
			{
				Gid = 22u,
				Sid = (uint)levelId,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr2);
		}
		playerAttr2.Val++;
		sync.Custom[player.ToPackedAttrKey(22u, (uint)levelId)] = playerAttr2.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, (uint)levelId)] = playerAttr2.Val;
	}

	private static int ParseStar(string param)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("nStar", out value) ? value.GetInt32() : 0;
		}
		catch
		{
			return 0;
		}
	}

	private static void MarkCompleted(PlayerInstance player, NtfSyncPlayer sync, int missionId, int checkValue)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 14 && x.Sid == (uint)missionId);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 14u,
				Sid = (uint)missionId,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		uint num = (uint)Math.Clamp(checkValue, 0, int.MaxValue);
		playerAttr.Val = (playerAttr.Val & 1) | (num << 1);
		sync.Custom[player.ToPackedAttrKey(14u, (uint)missionId)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(14u, (uint)missionId)] = playerAttr.Val;
	}

	private static bool ParamContains(JsonObject row, int levelId)
	{
		if (!row.TryGetPropertyValue("Param", out JsonNode jsonNode) || !(jsonNode is JsonArray source))
		{
			return false;
		}
		return source.Any((JsonNode x) => DlcMissionStore.ReadIntNode(x) == levelId);
	}

	private static int ParseLevelId(string param)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("nID", out value) ? value.GetInt32() : 0;
		}
		catch
		{
			return 0;
		}
	}
}
