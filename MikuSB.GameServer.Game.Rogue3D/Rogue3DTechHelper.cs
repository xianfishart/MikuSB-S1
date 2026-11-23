using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Game.Rogue3D;

internal static class Rogue3DTechHelper
{
	public static bool TryGetScience(uint techId, out Rogue3DScienceExcel science)
	{
		if (techId <= 100 || techId > 300)
		{
			science = null;
			return false;
		}
		return GameData.Rogue3DScienceData.TryGetValue(techId, out science);
	}

	public static uint GetRestrictionSid(uint techId)
	{
		return checked(techId + 500 - 100);
	}

	public static bool IsUnlocked(PlayerInstance player, Rogue3DScienceExcel science)
	{
		return science.UnlockCondition.Count == 0 || science.UnlockCondition.Any((uint id) => player.Attributes.GetValue(124u, id) != 0);
	}

	public static bool IsRestricted(PlayerInstance player, uint techId)
	{
		return player.Attributes.GetValue(124u, GetRestrictionSid(techId)) != 0;
	}
}
