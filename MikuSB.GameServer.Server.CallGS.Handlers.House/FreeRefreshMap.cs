using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("FreeRefreshMap")]
public class FreeRefreshMap : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			uint free = HouseAttr.Read(player, DormPuzzleRequests.FreeRefreshSid);
			if (free == 0)
			{
				await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"FreeRefreshMapSuccess\",\"sErr\":\"ui.PuzzleFreshTimeOut\"}");
				return;
			}
			List<DormPuzzleService.MapRow> maps = DormPuzzleService.LoadMaps();
			int mapId = ((maps.Count <= 0) ? 1 : maps[(int)(DateTime.Now.Ticks % maps.Count)].MapId);
			await HouseAttr.SetAsync(connection, DormPuzzleRequests.FreeRefreshSid, free - 1, sync);
			await HouseAttr.SetAsync(connection, DormPuzzleRequests.MapIdSid, (uint)mapId, sync);
			player.SetStrAttr(101u, DormPuzzleRequests.MapStateSid, "[]");
			sync.CustomStr[player.ToPackedAttrKey(101u, DormPuzzleRequests.MapStateSid)] = "[]";
			DatabaseHelper.SaveDatabaseType(player.Data);
			await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"FreeRefreshMapSuccess\",\"bSuccess\":true}", sync);
		}
	}
}
