using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("RouletteEnd")]
public class RouletteEnd : IHouseFuncHandler
{
	private const uint HouseHotSpringInfoStart = 15000u;

	private const uint MaxRoundsInWaterGun = 16u;

	private const uint HasCompleteGameGuide = 19u;

	private const uint HotSpringGirlInfoStart = 15100u;

	private const uint HotSpringGirlAttrCount = 10u;

	private const uint HotSpringGirlHasCompleteStory = 1u;

	private const int RoulettePlayModePlot = 1;

	private const int RoulettePlayModeEndless = 2;

	private const int RouletteEndReasonSuccess = 0;

	private const int RouletteTutorialGirlId = 5;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int modeType = HouseJson.NumField(root, "ModeType");
		int girlId = HouseJson.NumField(root, "GirlId");
		int isSuccess = HouseJson.NumField(root, "IsSuccess");
		int rounds = HouseJson.NumField(root, "Rounds");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (isSuccess == 0 && modeType == 1 && girlId > 0)
		{
			await HouseAttr.SetAsync(connection, (uint)(15100 + girlId * 10 + 1), 1u, sync);
			if (girlId == 5)
			{
				await HouseAttr.SetAsync(connection, 15019u, 1u, sync);
			}
		}
		// 2026-11-12 改（修复记录第 260 节）：连战（无尽）的「最高回合」不再直接写 Rounds。
		// 客户端结算传的 Rounds 是「已通关卡数」（实测清掉第 1 关时 Rounds=1、RoundsState=[1,1,1]），
		// 而界面「最高回合」要的是回合数，所以按 关卡数 × 每关回合数 + 本关胜场 换算（WaterGunHelper.ScoreFromEnd）。
		// 另外失败（IsSuccess=2）也要记录：连战本来就是打到输为止，只在通关时记等于永远记不到最终成绩。
		if (modeType == 2)
		{
			int score = WaterGunHelper.ScoreFromEnd(rounds, isSuccess, root["RoundsState"]);
			if (score > 0)
			{
				uint prev = HouseAttr.Read(connection.Player, 15016u);
				if ((uint)score > prev)
				{
					await HouseAttr.SetAsync(connection, 15016u, (uint)score, sync);
				}
			}
		}
		if (isSuccess == 0)
		{
			// 2026-11-12 追加（修复记录第 258 节）：结算加经验，等级才有来源。
			// 每局经验取 Resources/house/LoveGun/ModeInfo.json 的 ExpPerRound
			// （剧情/普通 100，无尽 500），剧情模式没回合数按 1 局算。
			int perRound = WaterGunHelper.ExpPerRound(modeType);
			int times = Math.Max(1, rounds);
			var grow = await WaterGunHelper.AddExpAsync(connection, perRound * times, sync);
			root["nAddExp"] = JsonValue.Create(grow.AddExp);
			root["nLevel"] = JsonValue.Create(grow.Level);
			root["nCurExp"] = JsonValue.Create(grow.Point);
			root["nMaxExp"] = JsonValue.Create(grow.Need);
		}
		// 2026-11-12 追加（修复记录第 260 节）：显式落盘。
		// 实测：15016 在内存里是 1（客户端能读到），但 Miku.db 里连这条属性都没有 ——
		// 只在退出/偶尔的定时保存时才写库，玩家一重登「最高回合」就回到 0，表现就是「没保存分数」。
		// 这里跟 WaterGunHelper.GrantAsync 的做法一致，改完属性立刻存一次。
		if (sync.Custom.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
	}
}
