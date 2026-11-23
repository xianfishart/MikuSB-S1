using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

namespace MikuSB.GameServer.Game.Progress;

public static class OnlineMissionAutoComplete
{
	private const uint DlcMissionGid = 14u;

	private static readonly int[] MissionIds = new int[11]
	{
		30003, 40001, 21157, 21158, 21159, 21160, 21161, 21380, 21381, 21382,
		21383
	};

	public static void Ensure(PlayerInstance player)
	{
		bool flag = false;
		int[] missionIds = MissionIds;
		foreach (int missionId in missionIds)
		{
			JsonObject jsonObject = DlcMissionStore.Find(missionId);
			if (jsonObject == null)
			{
				continue;
			}
			int num = DlcMissionStore.ReadInt(jsonObject, "CheckValue");
			if (num > 0)
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
				uint num2 = playerAttr.Val & 1;
				uint num3 = (playerAttr.Val >> 1) & 0x7FFFFFFF;
				if (num2 == 0 && num3 < (uint)num)
				{
					playerAttr.Val = num2 | (uint)(num << 1);
					flag = true;
				}
			}
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
	}
}
