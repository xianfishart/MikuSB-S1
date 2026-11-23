using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_EnterLevel")]
public class VirCaptureTower_EnterLevel : ICallGSHandler
{
	private const uint LaunchPassGroupId = 22u;

	private const uint VirCaptureGroupId = 128u;

	private const uint VirCaptureLevelSid = 3u;

	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		VirCaptureTowerEnterLevelParam req = JsonSerializer.Deserialize<VirCaptureTowerEnterLevelParam>(param);
		if (req == null || req.LevelId <= 0 || req.TeamId <= 0)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTower_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.VirCaptureTowerData.TryGetValue((uint)req.LevelId, out VirCaptureTowerExcel levelCfg))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTower_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		if (!CheckConditions(player.Data, levelCfg.Condition))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTower_EnterLevel", "{\"sErr\":\"tip.LevelLocked\"}");
			return;
		}
		await CallGSRouter.SendScript(connection, "VirCaptureTower_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}

	private static bool CheckConditions(PlayerGameData data, IReadOnlyDictionary<int, uint> conditions)
	{
		foreach (KeyValuePair<int, uint> condition in conditions)
		{
			var (num3, value) = condition;
			switch (num3)
			{
			case 1:
				if (data.Level < value)
				{
					return false;
				}
				break;
			case 2:
			{
				PlayerAttr? playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == value);
				if (playerAttr == null || playerAttr.Val == 0)
				{
					return false;
				}
				break;
			}
			case 20:
			{
				uint num4 = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == 3)?.Val ?? 0;
				if (num4 < value)
				{
					return false;
				}
				break;
			}
			}
		}
		return true;
	}
}
