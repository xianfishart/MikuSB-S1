using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ArcadeGameEnterMainUI")]
public class ArcadeGameEnterMainUI : IHouseFuncHandler
{
	private static readonly int[] ArcadeUnlockedGirlList = new int[5] { 1, 5, 13, 23, 10 };

	public async Task Handle(Connection connection, string param)
	{
		JsonArray unlockGirls = new JsonArray();
		int[] arcadeUnlockedGirlList = ArcadeUnlockedGirlList;
		foreach (int girlId in arcadeUnlockedGirlList)
		{
			unlockGirls.Add(girlId);
		}
		// 2026-11-13 追加（修复记录第 270 节）：进主界面时按普通模式累计回合补齐 101/18005
		// 无尽模式解锁位图。客户端 GetEndlessUnlocked 只看这个位图，修复前已经打满 5 回合的
		// 存档位图仍是 0，靠这里补，不用重打一局。
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await ArcadeGameHelper.RepairEndlessUnlockAsync(connection, ArcadeUnlockedGirlList, sync);
		JsonObject rsp = new JsonObject
		{
			["tbUnlockGirlList"] = unlockGirls,
			["FuncName"] = "ArcadeGameEnterMainUI"
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
