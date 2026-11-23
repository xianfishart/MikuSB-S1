using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 第 424 节：数据表体检。排查「第 34 期某活动显示配置错误 / 关卡解锁时间离谱」时，
/// 需要确认服务端到底加载到了哪些 Excel 行（StarWish 主表、FarmWar 章节关数、活动窗口）。
/// 只读，不改任何玩家数据。
///
/// 用法：/dataprobe [活动id ...]
/// </summary>
[CommandInfo("dataprobe", "Dump loaded excel counts for DLC activities (read-only).", "Usage: /dataprobe [activityId ...]", new string[]
{
	"dpb"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDataProbe : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		JsonObject root = new JsonObject();
		try
		{
			root["StarWishMain"] = GameData.StarWishMainData.Count;
			root["StarWishJigsaw"] = GameData.StarWishJigsawData.Count;
			root["StarWishScoreAward"] = GameData.StarWishScoreAwardData.Count;
			root["StarWishStoryQuest"] = GameData.StarWishStoryQuestData.Count;
			root["StarWishStoryPage"] = GameData.StarWishStoryPageData.Count;
			root["FarmWarAct"] = GameData.FarmWarActData.Count;
			root["FarmWarLevel"] = GameData.FarmWarLevelData.Count;
			JsonArray actRows = new JsonArray();
			foreach (uint id in new uint[] { 390u, 417u, 103u, 34u })
			{
				if (GameData.ActivityData.TryGetValue(id, out var conf))
				{
					actRows.Add(new JsonObject
					{
						["id"] = (long)id,
						["class"] = conf.Class,
						["start"] = conf.StartTime,
						["end"] = conf.EndTime
					});
				}
			}
			root["activities"] = actRows;
			JsonArray fw = new JsonArray();
			foreach (var kv in GameData.FarmWarActData.OrderBy(x => x.Key))
			{
				fw.Add(new JsonObject
				{
					["id"] = (long)kv.Key,
					["start"] = kv.Value.Start,
					["end"] = kv.Value.End
				});
			}
			root["farmwarActs"] = fw;
		}
		catch (Exception ex)
		{
			root["error"] = ex.GetType().Name + ": " + ex.Message;
		}
		await Task.CompletedTask;
		await arg.SendMsg("dataprobe: " + root.ToJsonString());
	}
}
