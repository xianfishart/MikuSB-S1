using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class AdminApi
{
	public sealed class Response
	{
		public int Status = 200;

		public string ContentType = "application/json; charset=utf-8";

		public byte[] Body = Array.Empty<byte>();

		public static Response Json(string json, int status = 200)
		{
			return new Response
			{
				Status = status,
				ContentType = "application/json; charset=utf-8",
				Body = Encoding.UTF8.GetBytes(json)
			};
		}

		public static Response Text(string text, string contentType, int status = 200)
		{
			return new Response
			{
				Status = status,
				ContentType = contentType,
				Body = Encoding.UTF8.GetBytes(text)
			};
		}

		public static Response Bytes(byte[] data, string contentType, int status = 200)
		{
			return new Response
			{
				Status = status,
				ContentType = contentType,
				Body = data
			};
		}
	}

	private sealed class CapturingSender(List<string> sink) : ICommandSender
	{
		public ValueTask SendMsg(string msg)
		{
			sink.Add(msg);
			return ValueTask.CompletedTask;
		}

		public int GetSender()
		{
			return 0;
		}
	}

	private sealed record EraRow(uint Id, string Name, long Start, long End, uint[] TryIds, uint Icon = 0u);

	private sealed record CastCharacter(string Key, uint Number, string Variant, string Name, string Skin, uint ActivityId);

	private sealed class CastTable
	{
		private readonly Dictionary<uint, (uint GroupId, string TitleKey)> _activities;

		private readonly Dictionary<uint, uint[]> _tryGirl;

		private readonly Dictionary<uint, (uint Number, int Variant)> _girls;

		private CastTable(Dictionary<uint, (uint, string)> activities, Dictionary<uint, uint[]> tryGirl, Dictionary<uint, (uint, int)> girls)
		{
			_activities = activities;
			_tryGirl = tryGirl;
			_girls = girls;
		}

		public static CastTable Load()
		{
			Dictionary<uint, (uint, string)> dictionary = new Dictionary<uint, (uint, string)>();
			Dictionary<uint, uint[]> dictionary2 = new Dictionary<uint, uint[]>();
			Dictionary<uint, (uint, int)> dictionary3 = new Dictionary<uint, (uint, int)>();
			string path = ResourceRoot();
			try
			{
				string path2 = Path.Combine(path, "activity", "activities.json");
				if (File.Exists(path2))
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path2));
					foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
					{
						uint num = ReadUInt(item, "Id");
						if (num != 0)
						{
							dictionary[num] = (ReadUInt(item, "GroupId"), ReadString(item, "TitleDes"));
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				string path3 = Path.Combine(path, "activity", "gacha_try", "gacha_try.json");
				if (File.Exists(path3))
				{
					using JsonDocument jsonDocument2 = JsonDocument.Parse(File.ReadAllText(path3));
					foreach (JsonElement item2 in jsonDocument2.RootElement.EnumerateArray())
					{
						uint num2 = ReadUInt(item2, "ActId");
						if (num2 != 0)
						{
							uint[] array = ReadNumbers(item2, "TryGirl");
							if (array.Length != 0)
							{
								dictionary2[num2] = array;
							}
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				string path4 = Path.Combine(path, "activity", "gacha_try", "try_girl.json");
				if (File.Exists(path4))
				{
					using JsonDocument jsonDocument3 = JsonDocument.Parse(File.ReadAllText(path4));
					foreach (JsonElement item3 in jsonDocument3.RootElement.EnumerateArray())
					{
						uint num3 = ReadUInt(item3, "ID");
						if (num3 != 0 && item3.TryGetProperty("Girl", out var value) && value.ValueKind == JsonValueKind.Array)
						{
							List<uint> list = (from e in value.EnumerateArray()
								select (e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out var value2)) ? value2 : 0u).ToList();
							if (list.Count >= 2 && list[1] != 0)
							{
								dictionary3[num3] = (list[1], (int)((list.Count <= 2) ? 1 : list[2]));
							}
						}
					}
				}
			}
			catch
			{
			}
			return new CastTable(dictionary, dictionary2, dictionary3);
		}

		public List<CastCharacter> CastFor(EraRow era)
		{
			List<CastCharacter> list = new List<CastCharacter>();
			if (era.TryIds.Length == 0)
			{
				return list;
			}
			Dictionary<string, string> dictionary = L10n("plotrolename.json");
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			uint[] tryIds = era.TryIds;
			foreach (uint groupId in tryIds)
			{
				foreach (KeyValuePair<uint, (uint, string)> item in _activities.Where((KeyValuePair<uint, (uint GroupId, string TitleKey)> p) => p.Value.GroupId == groupId))
				{
					uint key = item.Key;
					if (!_tryGirl.TryGetValue(key, out uint[] value))
					{
						continue;
					}
					uint[] array = value;
					foreach (uint key2 in array)
					{
						if (_girls.TryGetValue(key2, out (uint, int) value2))
						{
							string text = ((value2.Item2 > 2) ? ((char)(97 + value2.Item2 - 2)).ToString() : "");
							if (hashSet.Add(value2.Item1 + ":" + text))
							{
								string name = (dictionary.TryGetValue("Chara." + value2.Item1.ToString("00"), out var value3) ? value3 : "");
								list.Add(new CastCharacter("girl" + value2.Item1.ToString("000"), value2.Item1, text, name, "", key));
							}
						}
					}
				}
			}
			list.Sort((CastCharacter a, CastCharacter b) => a.Number.CompareTo(b.Number));
			return list;
		}
	}

	private static readonly DateTime StartedAt = DateTime.Now;

	private static readonly Dictionary<string, Dictionary<string, string>> L10nCache = new Dictionary<string, Dictionary<string, string>>();

	private static readonly object L10nGate = new object();

	private static readonly CastTable CastIndex = CastTable.Load();

	public static Response Handle(string method, string path, string query, string body)
	{
		try
		{
			// 上次因客户端占用没写成功的登录背景，趁这次请求补上
			LoginBgReplay.ApplyPending();
			string[] array = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
			string text = ((array.Length >= 2 && array[0] == "api") ? array[1] : "");
			if (1 == 0)
			{
			}
			Response result = text switch
			{
				"health" => Health(), 
				"overview" => Overview(), 
				"online" => Online(), 
				"accounts" => Accounts(method, array, query, body), 
				"players" => Player(method, array), 
				"currencies" => Currencies(), 
				"commands" => Commands(), 
				"console" => ConsoleCommand(body), 
				"replay" => Replay(method, array, query, body), 
				"loginbg" => LoginBg(method, body), 
				"eras" => Response.Json(ActivityWindowGuard.DescribeTrialIds()), 
				"data" => DataProbe(), 
				"setup" => Setup(), 
				_ => Response.Json("{\"code\":404,\"message\":\"not found\"}", 404), 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		catch (Exception ex)
		{
			return Response.Json(new JsonObject
			{
				["code"] = 500,
				["message"] = ex.Message
			}.ToJsonString(), 500);
		}
	}

	/// <summary>
	/// 第 424 节：Excel 体检。排查「活动显示配置错误 / 关卡解锁时间离谱」时需要知道
	/// 服务端到底加载了哪些行（StarWish 主表空 → StarWish_CheckOpenAct 回 bOpen=false）。
	/// 只读。
	/// </summary>
	private static Response DataProbe()
	{
		JsonObject root = new JsonObject();
		try
		{
			root["starWishMain"] = MikuSB.Data.GameData.StarWishMainData.Count;
			root["starWishJigsaw"] = MikuSB.Data.GameData.StarWishJigsawData.Count;
			root["starWishScoreAward"] = MikuSB.Data.GameData.StarWishScoreAwardData.Count;
			root["starWishStoryQuest"] = MikuSB.Data.GameData.StarWishStoryQuestData.Count;
			root["farmWarAct"] = MikuSB.Data.GameData.FarmWarActData.Count;
			root["farmWarLevel"] = MikuSB.Data.GameData.FarmWarLevelData.Count;
			root["eraFuncFarmwar"] = (long)DlcEraFunc.Current("Farmwar");
			root["eraFuncPuzzle"] = (long)DlcEraFunc.Current("Puzzle");
			root["eraFuncCelebrate"] = (long)DlcEraFunc.Current("Celebrate");
			JsonArray acts = new JsonArray();
			foreach (uint id in new uint[] { 390u, 417u, 100u, 103u, 34u })
			{
				if (MikuSB.Data.GameData.ActivityData.TryGetValue(id, out MikuSB.Data.Excel.ActivityExcel conf))
				{
					acts.Add(new JsonObject
					{
						["id"] = (long)id,
						["class"] = conf.Class,
						["start"] = conf.StartTime,
						["end"] = conf.EndTime
					});
				}
			}
			root["activities"] = acts;
		}
		catch (Exception ex)
		{
			root["error"] = ex.GetType().Name + ": " + ex.Message;
		}
		return Response.Json(root.ToJsonString());
	}

	private static Response Health()
	{		JsonObject jsonObject = new JsonObject
		{
			["ok"] = true,
			["name"] = "MikuSB",
			["version"] = "S1",
			["uptime_sec"] = (long)(DateTime.Now - StartedAt).TotalSeconds,
			["db_loaded"] = GameData.CardData.Count > 0,
			["online"] = OnlineCount()
		};
		return Response.Json(jsonObject.ToJsonString());
	}

	private static Response Overview()
	{
		JsonObject jsonObject = new JsonObject
		{
			["name"] = "MikuSB",
			["accounts"] = AllAccounts().Count,
			["online"] = OnlineCount(),
			["commands"] = CommandManager.Commands.Count
		};
		return Response.Json(jsonObject.ToJsonString());
	}

	private static Response Online()
	{
		JsonArray jsonArray = new JsonArray();
		foreach (Connection item in OnlineConnections())
		{
			jsonArray.Add(new JsonObject
			{
				["uid"] = item.Player.Uid,
				["name"] = item.Player.Data.Name,
				["level"] = item.Player.Data.Level,
				["remote_address"] = ""
			});
		}
		return Response.Json(new JsonObject
		{
			["items"] = jsonArray,
			["count"] = jsonArray.Count
		}.ToJsonString());
	}

	private static Response Accounts(string method, string[] segments, string query, string body)
	{
		if (method == "POST" && segments.Length == 2)
		{
			return CreateAccount(body);
		}
		if (method == "DELETE" && segments.Length >= 3 && int.TryParse(segments[2], out var result))
		{
			AccountData.DeleteAccount(result);
			return Response.Json("{\"code\":0}");
		}
		string q = QueryValue(query, "q").Trim();
		int num = Math.Max(1, ParseInt(QueryValue(query, "page"), 1));
		int num2 = Math.Clamp(ParseInt(QueryValue(query, "size"), 20), 1, 200);
		HashSet<int> hashSet = (from c in OnlineConnections()
			select c.Player.Uid).ToHashSet();
		List<AccountData> list = (from a in AllAccounts()
			where q.Length == 0 || a.Username.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Uid.ToString() == q
			orderby a.Uid
			select a).ToList();
		JsonArray jsonArray = new JsonArray();
		foreach (AccountData item in list.Skip((num - 1) * num2).Take(num2))
		{
			jsonArray.Add(new JsonObject
			{
				["uid"] = item.Uid,
				["username"] = item.Username,
				["online"] = hashSet.Contains(item.Uid),
				["ban_type"] = item.BanType.ToString(),
				["permissions"] = new JsonArray(((IEnumerable<PermEnum>)item.Permissions).Select((Func<PermEnum, JsonNode>)((PermEnum p) => p.ToString())).ToArray())
			});
		}
		return Response.Json(new JsonObject
		{
			["items"] = jsonArray,
			["total"] = list.Count,
			["page"] = num,
			["size"] = num2
		}.ToJsonString());
	}

	private static Response CreateAccount(string body)
	{
		string text = "";
		string text2 = "";
		int uid = 0;
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) is JsonObject jsonObject)
			{
				text = jsonObject["username"]?.ToString() ?? "";
				text2 = jsonObject["password"]?.ToString() ?? "";
				if (jsonObject["uid"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
				{
					uid = value;
				}
			}
		}
		catch
		{
			return Response.Json("{\"code\":1,\"message\":\"bad json\"}");
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return Response.Json("{\"code\":1,\"message\":\"username required\"}");
		}
		AccountData accountData = AccountData.CreateAccount(text, uid, text2 ?? "");
		return Response.Json(new JsonObject
		{
			["code"] = 0,
			["uid"] = accountData.Uid,
			["username"] = accountData.Username
		}.ToJsonString());
	}

	private static Response Player(string method, string[] segments)
	{
		if (segments.Length < 3 || !int.TryParse(segments[2], out var uid))
		{
			return Response.Json("{\"code\":1,\"message\":\"uid required\"}");
		}
		AccountData accountByUid = AccountData.GetAccountByUid(uid);
		if (accountByUid == null)
		{
			return Response.Json("{\"code\":1,\"message\":\"account not found\"}", 404);
		}
		bool flag = OnlineConnections().Any((Connection c) => c.Player.Uid == uid);
		PlayerGameData instance = DatabaseHelper.GetInstance<PlayerGameData>(uid);
		JsonObject value = new JsonObject
		{
			["name"] = instance?.Name ?? "",
			["level"] = instance?.Level ?? 0,
			["vigor"] = instance?.Vigor ?? 0,
			["register_time"] = instance?.RegisterTime ?? 0,
			["last_active_time"] = instance?.LastActiveTime ?? 0
		};
		JsonObject jsonObject = new JsonObject
		{
			["items"] = 0,
			["weapons"] = 0,
			["skins"] = 0
		};
		Connection connection = OnlineConnections().FirstOrDefault((Connection c) => c.Player.Uid == uid);
		if (connection != null)
		{
			InventoryData inventoryData = connection.Player.InventoryManager.InventoryData;
			jsonObject["items"] = inventoryData.Items.Count;
			jsonObject["weapons"] = inventoryData.Weapons.Count;
			jsonObject["skins"] = inventoryData.Skins.Count;
		}
		JsonObject jsonObject2 = new JsonObject
		{
			["account"] = new JsonObject
			{
				["uid"] = accountByUid.Uid,
				["username"] = accountByUid.Username,
				["online"] = flag,
				["ban_type"] = accountByUid.BanType.ToString()
			},
			["player"] = value,
			["inventory"] = jsonObject,
			["currencies"] = new JsonArray()
		};
		return Response.Json(jsonObject2.ToJsonString());
	}

	private static Response Currencies()
	{
		JsonArray jsonArray = new JsonArray
		{
			new JsonObject
			{
				["id"] = 1,
				["name"] = "金币"
			},
			new JsonObject
			{
				["id"] = 2,
				["name"] = "钻石"
			},
			new JsonObject
			{
				["id"] = 3,
				["name"] = "体力"
			},
			new JsonObject
			{
				["id"] = 4,
				["name"] = "合成玉"
			}
		};
		return Response.Json(jsonArray.ToJsonString());
	}

	private static Response Commands()
	{
		JsonArray jsonArray = new JsonArray();
		foreach (KeyValuePair<string, CommandInfoAttribute> item in CommandManager.CommandInfo.OrderBy((KeyValuePair<string, CommandInfoAttribute> x) => x.Key))
		{
			jsonArray.Add(new JsonObject
			{
				["name"] = item.Key,
				["usage"] = item.Value.Usage,
				["description"] = item.Value.Description
			});
		}
		return Response.Json(new JsonObject { ["items"] = jsonArray }.ToJsonString());
	}

	private static Response ConsoleCommand(string body)
	{
		string text = "";
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) is JsonObject jsonObject)
			{
				text = jsonObject["command"]?.ToString() ?? "";
			}
		}
		catch
		{
			return Response.Json("{\"code\":1,\"message\":\"bad json\"}");
		}
		text = text.Trim();
		if (text.Length == 0)
		{
			return Response.Json("{\"code\":1,\"message\":\"empty command\"}");
		}
		if (text.TrimStart('/').StartsWith("lua", StringComparison.OrdinalIgnoreCase))
		{
			return ConsoleHost.ExecuteLua(text);
		}
		List<string> list = new List<string>();
		CapturingSender sender = new CapturingSender(list);
		string input = (text.StartsWith('/') ? text.Substring(1) : text);
		CommandManager.HandleCommand(input, sender);
		for (int i = 0; i < 40; i++)
		{
			if (list.Count != 0)
			{
				break;
			}
			Thread.Sleep(25);
		}
		return Response.Json(new JsonObject
		{
			["code"] = 0,
			["messages"] = new JsonArray(((IEnumerable<string>)list).Select((Func<string, JsonNode>)((string m) => m)).ToArray())
		}.ToJsonString());
	}

	private static Response Replay(string method, string[] segments, string query, string body)
	{
		if (segments.Length >= 3 && segments[2] == "image")
		{
			return ReplayImage(segments);
		}
		if (method == "POST")
		{
			return ApplyReplay(body);
		}
		JsonArray jsonArray = new JsonArray();
		CastTable castIndex = CastIndex;
		foreach (EraRow item in ReadDlcActivities())
		{
			JsonArray jsonArray2 = new JsonArray();
			foreach (CastCharacter item2 in castIndex.CastFor(item))
			{
				jsonArray2.Add(new JsonObject
				{
					["key"] = item2.Key,
					["number"] = item2.Number,
					["name"] = item2.Name,
					["skin"] = item2.Skin,
					["variant"] = item2.Variant,
					["activity_id"] = item2.ActivityId
				});
			}
			jsonArray.Add(new JsonObject
			{
				["id"] = item.Id,
				["name"] = EraName(item),
				["start_unix"] = item.Start,
				["end_unix"] = item.End,
				["retired"] = false,
				["has_image"] = ImageFile(item.Id) != null,
				["characters"] = jsonArray2
			});
		}
		uint num = DlcPin.Override();
		JsonObject jsonObject = new JsonObject
		{
			["available"] = jsonArray.Count > 0,
			["root"] = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc"),
			["eras"] = jsonArray,
			["current"] = new JsonObject
			{
				["id"] = num,
				["runtime_pin"] = false,
				["persisted"] = num != 0
			}
		};
		return Response.Json(jsonObject.ToJsonString());
	}

	/// <summary>
	/// 第 527 节：GM 页面每个版本卡片下方的「登录背景」按钮。
	/// GET  返回 era→编号 映射与当前编号；POST { era: N } 按期映射切换，
	/// 或 { index: N } 直接按编号切换（0~30）。
	/// </summary>
	private static Response LoginBg(string method, string body)
	{
		if (method == "POST")
		{
			return ApplyLoginBg(body);
		}
		JsonObject map = new JsonObject();
		foreach (KeyValuePair<uint, int> item in LoginBgReplay.LoadMap())
		{
			map[item.Key.ToString()] = item.Value;
		}
		JsonObject jsonObject = new JsonObject
		{
			["ok"] = true,
			["current"] = LoginBgReplay.CurrentIndex(),
			["pending"] = LoginBgReplay.IsPending(),
			["map"] = map
		};
		return Response.Json(jsonObject.ToJsonString());
	}

	private static Response ApplyLoginBg(string body)
	{
		int era = 0;
		int index = -1;
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) is JsonObject jsonObject)
			{
				if (jsonObject["era"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
				{
					era = value;
				}
				if (jsonObject["index"] is JsonValue jsonValue2 && jsonValue2.TryGetValue<int>(out var value2))
				{
					index = value2;
				}
			}
		}
		catch
		{
			return Response.Json("{\"code\":1,\"message\":\"bad json\"}");
		}
		string message;
		if (index >= 0)
		{
			message = LoginBgReplay.ApplyIndex(index, (uint)Math.Max(0, era));
		}
		else
		{
			if (era <= 0)
			{
				return Response.Json("{\"code\":2,\"message\":\"缺少 era 或 index\"}");
			}
			if (!LoginBgReplay.LoadMap().TryGetValue((uint)era, out int idx))
			{
				return Response.Json($"{{\"code\":3,\"message\":\"第 {era} 期没有配置登录背景编号（桌面《登录背景-复刻期对照表》第 5 列留空）\"}}");
			}
			message = LoginBgReplay.ApplyIndex(idx, (uint)era);
		}
		JsonObject result = new JsonObject
		{
			["code"] = 0,
			["message"] = message,
			["current"] = LoginBgReplay.CurrentIndex(),
			["pending"] = LoginBgReplay.IsPending()
		};
		return Response.Json(result.ToJsonString());
	}

	private static Response ApplyReplay(string body)
	{
		int num = 0;
		bool flag = true;
		try
		{
			if (JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) is JsonObject jsonObject)
			{
				if (jsonObject["id"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
				{
					num = value;
				}
				if (jsonObject["save"] is JsonValue jsonValue2 && jsonValue2.TryGetValue<bool>(out var value2))
				{
					flag = value2;
				}
			}
		}
		catch
		{
			return Response.Json("{\"code\":1,\"message\":\"bad json\"}");
		}
		if (num != 0 && num != (int)DlcPin.DefaultEra && !GameData.DlcActivityData.ContainsKey((uint)num))
		{
			return Response.Json($"{{\"code\":2,\"message\":\"era {num} not found\"}}");
		}
		List<string> list = new List<string>();
		if (flag)
		{
			if (!DlcPin.Write((uint)Math.Max(0, num)))
			{
				return Response.Json("{\"code\":3,\"message\":\"写 Config.json 失败\"}");
			}
			list.Add(DlcPin.IsDefault((uint)Math.Max(0, num)) ? $"已切到游戏默认（pin={DlcPin.DefaultEra}）" : $"已写入 Config.json：PinDlcActivityId={num}");
		}
		// 第 527 节：切期不再自动换登录背景。背景改由 GM 页面每张卡片下方的
		// 「登录背景 N 号」按钮单独控制（POST /api/loginbg），切版本与换背景解耦，
		// 免得「只想换期、不想动背景」时被联动改掉。
		if (num != 0 && EraLite.Is((uint)num))
		{
			list.Add($"第 {num} 期为轻量复刻：只换卡池与活动列表（含试玩关），版本活动入口隐藏，其余 DLC 内容保持游戏默认");
		}
		int num2 = 0;
		foreach (Connection item in OnlineConnections())
		{
			try
			{
				// 轻量复刻期只换卡池和活动列表，存档里的版本期号仍写默认期，
				// 免得客户端按 Gid15/Sid1 把版本活动面板切成跑不通的老版本。
				// 默认态（pin=99）先还原成 0：「游戏默认」= 按玩家自己上报的期数走，
				// 不能把存档期号钉成 27（第 226 节）。
				DlcEraState.SetEra(item.Player, EraLite.DlcEra(DlcPin.ClientEra((uint)Math.Max(0, num))), null);
				num2++;
			}
			catch
			{
			}
		}
		if (num2 > 0)
		{
			list.Add($"在线 {num2} 人已同步");
		}
		// 第 418 节：切期不会重跑登录播种，18/1（噬神斗场期号）等属性会留在上一期，
		// 表现为「第 N 期噬神斗场 / 心意寄语」点进去暂未开放。这里先重播种并
		// NtfSyncPlayer 立刻把属性推给客户端，再走下面那套窗口/补丁重推。
		DlcEraSeeds.PushAllOnline($"replay {num}");
		EraGacha.ResendAll();
		DlcWindowPush.PushAllOnline($"replay {num}");
		DlcClientPatches.PushAllOnline($"replay {num}");
		DlcMainEntry.ResendAll($"replay {num}");
		ChapterCard.PushAllOnline();
		DlcEraGuards.PushAllOnline($"replay {num}");
		MainSurveyHide.PushAllOnline($"replay {num}");
		MainRedDotHide.PushAllOnline($"replay {num}");
		ActivityWindowGuard.PushAllOnline($"replay {num}");
		return Response.Json(new JsonObject
		{
			["code"] = 0,
			["id"] = num,
			["messages"] = new JsonArray(((IEnumerable<string>)list).Select((Func<string, JsonNode>)((string m) => m)).ToArray())
		}.ToJsonString());
	}

	private static uint[] ReadNumbers(JsonElement item, string name)
	{
		List<uint> list = new List<uint>();
		if (!item.TryGetProperty(name, out var value))
		{
			return list.ToArray();
		}
		if (value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				uint num = ((item2.ValueKind != JsonValueKind.Number) ? (uint.TryParse(item2.ToString().Trim().Trim(new char[3] { '[', ']', '"' }), out var result) ? result : 0u) : (item2.TryGetUInt32(out var value2) ? value2 : 0u));
				if (num != 0)
				{
					list.Add(num);
				}
			}
		}
		else
		{
			uint num2 = ReadUInt(item, name);
			if (num2 != 0)
			{
				list.Add(num2);
			}
		}
		return list.ToArray();
	}

	private static List<EraRow> ReadDlcActivities()
	{
		List<EraRow> list = new List<EraRow>();
		string path = Path.Combine(ResourceRoot(), "dlc", "dlc_activities.json");
		if (!File.Exists(path))
		{
			return list;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
			{
				return list;
			}
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				uint num = ReadUInt(item, "ID");
				if (num != 0)
				{
					list.Add(new EraRow(num, ReadString(item, "MainUI"), ToUnix(ReadString(item, "EnterStartTime")), ToUnix(ReadString(item, "CloseEndTime")), ReadNumbers(item, "TryID"), ReadUInt(item, "Icon")));
				}
			}
		}
		catch
		{
		}
		list.Sort((EraRow a, EraRow b) => a.Id.CompareTo(b.Id));
		return list;
	}

	private static uint ReadUInt(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var value))
		{
			return 0u;
		}
		JsonValueKind valueKind = value.ValueKind;
		if (1 == 0)
		{
		}
		uint result = valueKind switch
		{
			JsonValueKind.Number => value.TryGetUInt32(out var value2) ? value2 : 0u, 
			JsonValueKind.String => uint.TryParse(value.GetString()?.Trim().Trim(new char[3] { '[', ']', '"' }), out var result2) ? result2 : 0u, 
			_ => 0u, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string ReadString(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var value))
		{
			return "";
		}
		JsonValueKind valueKind = value.ValueKind;
		if (1 == 0)
		{
		}
		string result = valueKind switch
		{
			JsonValueKind.String => value.GetString() ?? "", 
			JsonValueKind.Number => value.ToString(), 
			_ => "", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static long ToUnix(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0L;
		}
		string s = raw.Trim().Trim(new char[3] { '[', ']', '"' });
		if (!DateTime.TryParseExact(s, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return 0L;
		}
		return new DateTimeOffset(result).ToUnixTimeSeconds();
	}

	private static string ResourceRoot()
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(Directory.GetCurrentDirectory());
		int num = 0;
		while (num < 5 && directoryInfo != null)
		{
			string text = Path.Combine(directoryInfo.FullName, "Resources");
			if (Directory.Exists(Path.Combine(text, "dlc")))
			{
				return text;
			}
			num++;
			directoryInfo = directoryInfo.Parent;
		}
		return Path.Combine(Directory.GetCurrentDirectory(), "Resources");
	}

	private static string ImageFile(uint id)
	{
		string text = Path.Combine(ResourceRoot(), "ui", "replay", $"dlc-{id}.png");
		return File.Exists(text) ? text : null;
	}

	private static Response ReplayImage(string[] segments)
	{
		string s = ((segments.Length >= 4) ? segments[3] : "");
		if (!uint.TryParse(s, out var result) || result == 0)
		{
			return Response.Json("{\"code\":\"bad_id\"}", 404);
		}
		string text = ImageFile(result);
		if (text == null)
		{
			return Response.Json("{\"code\":\"no_image\",\"id\":" + result + "}", 404);
		}
		try
		{
			return Response.Bytes(File.ReadAllBytes(text), "image/png");
		}
		catch
		{
			return Response.Json("{\"code\":\"read_failed\",\"id\":" + result + "}", 404);
		}
	}

	private static Dictionary<string, string> L10n(string file)
	{
		string path = Path.Combine(ResourceRoot(), "all_language", "ui", file);
		if (!File.Exists(path))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
		string key = file + ":" + File.GetLastWriteTimeUtc(path).Ticks;
		lock (L10nGate)
		{
			if (L10nCache.TryGetValue(key, out Dictionary<string, string> value))
			{
				return value;
			}
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
				{
					string text = ReadString(item, "Key");
					if (text.Length != 0)
					{
						string text2 = ReadString(item, "zh_CN");
						if (text2.Length == 0)
						{
							text2 = ReadString(item, "en_US");
						}
						dictionary[text] = text2;
					}
				}
			}
		}
		catch
		{
		}
		lock (L10nGate)
		{
			foreach (string item2 in L10nCache.Keys.Where((string k) => k.StartsWith(file + ":", StringComparison.Ordinal)).ToList())
			{
				L10nCache.Remove(item2);
			}
			L10nCache[key] = dictionary;
		}
		return dictionary;
	}

	private static string EraName(EraRow row)
	{
		Dictionary<string, string> dictionary = L10n("ui.json");
		string name = EraNameByKey(dictionary, row.Id);
		if (name != null)
		{
			return name;
		}
		// 复刻期的主题名在语言表里挂在原版期号下（如第 23 期复刻第 12 期「漠北寻风」→
		// 只有 TxtDLC12.BtnStory，没有 TxtDLC23.*）。dlc_activities.json 的 Icon 是
		// 2400000+原版期号（第 23 期 Icon=2400012），用尾数回查一轮。
		// 24~36 期先命中自己的键，走不到这里，不受影响。
		if (row.Icon >= 2400001u)
		{
			uint originId = row.Icon - 2400000u;
			if (originId != row.Id)
			{
				name = EraNameByKey(dictionary, originId);
				if (name != null)
				{
					return name;
				}
			}
		}
		if (dictionary.TryGetValue("TxtDLC" + row.Name, out var value2) && !string.IsNullOrWhiteSpace(value2))
		{
			return value2;
		}
		return row.Name;
	}

	/// <summary>按期号尝试语言表的三个名称键（BtnStory / Title / Name），命中返回。</summary>
	private static string EraNameByKey(Dictionary<string, string> dictionary, uint id)
	{
		string[] array = new string[3]
		{
			"TxtDLC" + id + ".BtnStory",
			"TxtDLC" + id + ".Title",
			"TxtDLC" + id + "Name"
		};
		foreach (string key in array)
		{
			if (dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}
		return null;
	}

	private static Response Setup()
	{
		JsonObject jsonObject = new JsonObject
		{
			["supported"] = false,
			["region"] = "global"
		};
		InlineArray2<JsonNode> buffer = default;
		buffer[0] = new JsonObject
		{
			["id"] = "global",
			["loader"] = "MikuSB"
		};
		buffer[1] = new JsonObject
		{
			["id"] = "cn",
			["loader"] = "MikuSB"
		};
		jsonObject["regions"] = new JsonArray(buffer);
		jsonObject["game_path"] = "";
		jsonObject["user_dir"] = "";
		jsonObject["arguments"] = new JsonArray();
		jsonObject["addresses"] = new JsonArray((JsonNode?)"127.0.0.1");
		jsonObject["public_address"] = "127.0.0.1";
		jsonObject["patch_present"] = false;
		jsonObject["elevated"] = false;
		jsonObject["cn_domains"] = new JsonArray();
		jsonObject["probe"] = new JsonObject
		{
			["valid"] = false,
			["checks"] = new JsonArray()
		};
		jsonObject["hosts"] = new JsonObject
		{
			["entries"] = new JsonArray(),
			["managed"] = false
		};
		jsonObject["proxy"] = new JsonObject
		{
			["enabled"] = false,
			["port"] = 0,
			["set_all_proxy"] = false
		};
		JsonObject jsonObject2 = jsonObject;
		return Response.Json(jsonObject2.ToJsonString());
	}

	private static List<AccountData> AllAccounts()
	{
		return DatabaseHelper.GetAllInstance<AccountData>() ?? new List<AccountData>();
	}

	private static IEnumerable<Connection> OnlineConnections()
	{
		return (from c in SocketListener.Connections.Values.OfType<Connection>()
			where c.Player != null
			select c).ToList();
	}

	private static int OnlineCount()
	{
		return OnlineConnections().Count();
	}

	private static string QueryValue(string query, string key)
	{
		if (string.IsNullOrEmpty(query))
		{
			return "";
		}
		string[] array = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			int num = text.IndexOf('=');
			if (num > 0 && string.Equals(text.Substring(0, num), key, StringComparison.OrdinalIgnoreCase))
			{
				return Uri.UnescapeDataString(text.Substring(num + 1).Replace('+', ' '));
			}
		}
		return "";
	}

	private static int ParseInt(string text, int fallback)
	{
		int result;
		return int.TryParse(text, out result) ? result : fallback;
	}
}
