using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.TcpSharp;
using S1Admin;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("dlc", "Serve any DLC era at runtime.", "Usage: /dlc [<id>|off|status] [-save]", new string[] { "era" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDlc : ICommands
{
	[CommandMethod("status")]
	public async ValueTask ShowStatus(CommandArg arg)
	{
		await arg.SendMsg(Describe());
	}

	[CommandMethod("off")]
	public async ValueTask TurnOff(CommandArg arg)
	{
		if (!DlcPin.Write(0u))
		{
			await arg.SendMsg("写 DlcPin.json 失败，pin 没改成。");
			return;
		}
		// 回到「游戏默认」时存档里的版本期号也要跟着回到玩家该有的那一期：
		// 之前 pin 停在第 N 期会把 Gid15/Sid1 钉成 N，off 之后客户端仍按 N 显示。
		// 先用 DlcDefaultEra 算出该回到哪一期（pin 已置 0，算出来就是默认语义）。
		int reset = 0;
		foreach (Connection conn in OnlineConnections())
		{
			try
			{
				DlcEraState.SetEra(conn.Player, DlcEraState.PickDefaultEraId(conn.Player, DateTime.Now), null);
				reset++;
			}
			catch
			{
			}
		}
		await arg.SendMsg($"已取消版本 pin，回到玩家各自选的期数（在线 {reset} 人已同步）。{EraGacha.Describe()}");
		// 关键：主界面入口的显隐判定必须重推，否则「游戏默认」下那块白框/入口会一直留着
		// （EntryHiddenFor 对默认态返回 true，但 off 以前根本没走到 DlcMainEntry）。
		PushAll("dlc off");
	}

	/// <summary>
	/// 切期之后必须重推的全套客户端状态。`/dlc N`、`/dlc off` 与 GM 网页「活动复刻」
	/// 三处必须走同一份清单，漏一个就会出现「服务端已经切了、界面上还是上一期」。
	/// </summary>
	private static void PushAll(string reason)
	{
		// 第 418 节：先重播种「当期期号」属性（18/1 噬神斗场、72/1 心意寄语、
		// 12/0 捞金派对、164/1 爆爆菜园）并即时同步，否则切期后客户端仍读上一期期号。
		DlcEraSeeds.PushAllOnline(reason);
		EraGacha.ResendAll();
		DlcWindowPush.PushAllOnline(reason);
		DlcClientPatches.PushAllOnline(reason);
		DlcMainEntry.ResendAll(reason);
		ChapterCard.PushAllOnline();
		DlcEraGuards.PushAllOnline(reason);
		ActivityWindowGuard.PushAllOnline(reason);
		MainSurveyHide.PushAllOnline(reason);
		MainRedDotHide.PushAllOnline(reason);
	}

	private static List<Connection> OnlineConnections()
	{
		List<Connection> list = new List<Connection>();
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			if (item.Value is Connection conn && conn.Player != null)
			{
				list.Add(conn);
			}
		}
		return list;
	}

	[CommandDefault]
	public async ValueTask Execute(CommandArg arg)
	{
		string text = arg.Args.FirstOrDefault()?.ToLowerInvariant();
		if (string.IsNullOrEmpty(text) || text == "status")
		{
			await arg.SendMsg(Describe());
			return;
		}
		if (text == "off")
		{
			await TurnOff(arg);
			return;
		}
		if (!uint.TryParse(text, out var eraId))
		{
			await arg.SendMsg("'" + text + "' 不是期数。" + Describe());
			return;
		}
		if (!GameData.DlcActivityData.ContainsKey(eraId))
		{
			await arg.SendMsg($"第 {eraId} 期不在 dlc_activities 里。可选：{AvailableIds()}");
			return;
		}
		if (!DlcPin.Write(eraId))
		{
			await arg.SendMsg("写 DlcPin.json 失败，pin 没改成。");
			return;
		}
		int switched = 0;
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			SocketConnection value = item.Value;
			Connection conn = value as Connection;
			if (conn != null && conn.Player != null)
			{
				try
				{
					DlcEraState.SetEra(conn.Player, eraId, null);
					switched++;
				}
				catch
				{
				}
			}
		}
		await arg.SendMsg($"已切到第 {eraId} 期（在线 {switched} 人已同步）。{EraGacha.Describe()}");
		PushAll($"dlc {eraId}");
	}

	private static string Describe()
	{
		uint num = DlcPin.Override();
		string value = (DlcPin.IsDefault(num) ? $"游戏默认（pin={DlcPin.DefaultEra}）" : $"pin = {num}");
		return $"版本活动：{value}｜可选 {GameData.DlcActivityData.Count} 期：{AvailableIds()}｜{EraGacha.Describe()}";
	}

	private static string AvailableIds()
	{
		List<uint> values = GameData.DlcActivityData.Keys.OrderBy((uint x) => x).ToList();
		string text = string.Join(",", values);
		return (text.Length > 400) ? (text.Substring(0, 400) + "...") : text;
	}

	private static bool WritePin(int id)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
			if (!File.Exists(path))
			{
				return false;
			}
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject))
			{
				return false;
			}
			JsonObject jsonObject2 = jsonObject["ServerOption"] as JsonObject;
			if (jsonObject2 == null)
			{
				jsonObject2 = (JsonObject)(jsonObject["ServerOption"] = new JsonObject());
			}
			jsonObject2["PinDlcActivityId"] = id;
			File.WriteAllText(path, jsonObject.ToJsonString());
			return true;
		}
		catch
		{
			return false;
		}
	}
}
