using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.StarWish;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 星祈心愿「心愿栽培」任务完成上报（修复记录第 564 节）。
/// 客户端对话结束发 {"FuncName":"CompleteStarWishTask","Index":101}；
/// 界面进度读的却是星祈剧情任务属性：Gid=180、Sid=100+3*ID
/// （客户端 StarWishLogic.GetStoryQuestCompleted 反汇编：SubNum=3、nStoryQuestIDOffset=100）。
/// 原来只写家园段 101/60140+Index，两边不是同一格属性 → 对话完永远 0/1。
/// </summary>
[HouseFunc("CompleteStarWishTask")]
public class CompleteStarWishTask : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int index = HouseJson.NumField(root, "Index");
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "CompleteStarWishTaskSuccess",
			["Index"] = index
		};
		if (index <= 0)
		{
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
			return;
		}
		// 老链路：家园任务段 101/60140+Index，保留（仍可能有界面按它读）
		await HouseProgressHelper.SetAttr(connection, (uint)(60140 + index), 1u);
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
			return;
		}
		// 新链路：星祈剧情任务进度（界面真正读的那一格）
		uint check = 1u;
		StarWishStoryQuestExcel quest = GameData.StarWishStoryQuestData.GetValueOrDefault((uint)index);
		if (quest != null && quest.CheckValue > 0)
		{
			check = quest.CheckValue;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		StarWishAttr.Set(player, StarWishQuests.ProgressSid((uint)index), check, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
