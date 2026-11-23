using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class EraGacha
{
	private sealed class PoolInfo
	{
		public int Id;

		public string Kind = "";

		public string Name = "";

		public string Card = "";

		public int Pt;
	}

	private sealed class EraInfo
	{
		public int Dlc;

		public string Title = "";

		public List<PoolInfo> Pools = new List<PoolInfo>();
	}

	private sealed class Group
	{
		public int G;

		public int Icon;

		public string Name = "";
	}

	private static readonly (int G, int Icon, string Name)[] TailGroups = new (int, int, string)[3]
	{
		(1, 0, "gacha.gacha4"),
		(2, 0, "gacha.gacha4"),
		(3, 0, "gacha.gacha5")
	};

	private const int DefaultDlc = 11;

	// 进程盐：参与卡池分组签名，重启后签名必然变化，客户端会重建一次分组。
	private static readonly long Salt = DateTime.Now.Ticks % 100000L;

	private static readonly object Gate = new object();

	private static readonly Dictionary<long, string> LastSig = new Dictionary<long, string>();

	private static readonly Dictionary<long, Connection> LastConn = new Dictionary<long, Connection>();

	private static bool _started;

	public static void Start()
	{
		lock (Gate)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		Thread thread = new Thread(Loop)
		{
			IsBackground = true,
			Name = "S1AdminEraGacha"
		};
		thread.Start();
	}

	private static void Loop()
	{
		Thread.Sleep(3000);
		while (true)
		{
			try
			{
				Tick();
			}
			catch
			{
			}
			Thread.Sleep(2000);
		}
	}

	private static void Tick()
	{
		string text = BuildSignature();
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			if (!(item.Value is Connection { Player: not null } connection))
			{
				continue;
			}
			int uid = connection.Player.Uid;
			lock (Gate)
			{
				if (LastSig.TryGetValue(uid, out string value) && value == text && LastConn.TryGetValue(uid, out Connection value2) && value2 == connection)
				{
					continue;
				}
			}
			try
			{
				Push(connection, quiet: false);
				MarkPushed(uid, connection, text);
			}
			catch (Exception ex)
			{
				Log($"push uid={uid} failed: {ex.Message}");
				lock (Gate)
				{
					LastSig.Remove(uid);
				}
			}
		}
	}

	private static void MarkPushed(long uid, Connection conn, string sig)
	{
		lock (Gate)
		{
			LastSig[uid] = sig;
			LastConn[uid] = conn;
		}
	}

	public static void OnLogin(Connection connection)
	{
		if (connection.Player == null)
		{
			return;
		}
		int uid = connection.Player.Uid;
		Task.Run(() =>
		{
			int[] array = new int[3] { 2500, 9000, 20000 };
			foreach (int millisecondsTimeout in array)
			{
				Thread.Sleep(millisecondsTimeout);
				if (connection.Player == null)
				{
					break;
				}
				try
				{
					Push(connection, quiet: false);
					MarkPushed(uid, connection, BuildSignature());
				}
				catch (Exception ex)
				{
					Log($"login push uid={uid} failed: {ex.Message}");
				}
			}
		});
	}

	private static string BuildSignature()
	{
		string value2 = StampOf(Path.Combine(GameDir(), "工具", "卡池版本表.json"));
		string value3 = StampOf(Path.Combine(GameDir(), "工具", "版本卡池固定表.json"));
		// 默认模式单独一个签名头，否则会和上一期算出的签名相同而不重推。
		if (DefaultMode())
		{
			return $"pm|{value2}|{value3}";
		}
		int value = CurrentDlc();
		return $"{value}|{value2}|{value3}";
	}

	/// <summary>
	/// 是否处于「游戏默认」（没有 pin 任何版本活动）。
	/// </summary>
	private static bool DefaultMode()
	{
		return DlcPin.IsDefault(DlcPin.Override());
	}

	/// <summary>
	/// 游戏默认模式的卡池：只挂常驻共鸣（常守之誓 / 中庭炉心）+ 启程集结。
	/// 池 id 1/2/3 是客户端原生表里的常驻与新手池，窗口钉死到远期，避免被时间窗过滤。
	/// </summary>
	private static void PushPermanent(Connection conn, bool quiet)
	{
		Send(conn, "_G.GIDS={1,2,3} print(#GIDS)");
		Send(conn, "if Gacha and Gacha.tbGacha then for i=1,#GIDS do local c=Gacha.tbGacha[GIDS[i]] if c and c.tbPoolTime then c.tbPoolTime[1]=202107061830 c.tbPoolTime[2]=203007261159 end end end");
		// 只把 1/2/3 的时间窗钉远期还不够：第 11 期那 16 个活动池（27/28/31/32/
		// 41~44/127/128/131/132/141~144）在配置里的时间窗本来就覆盖当前时间，
		// 共鸣界面按 tbPoolTime 遍历时会照旧把它们列出来，看起来「卡池没变」。
		// 这里在默认模式下把非常驻池全部收到 2020 年（=已过期），首次注入时
		// 把各池原始时间窗备份进 _G.MikuPoolBak，切到具体期时再还原（第 225 节）。
		Send(conn, "if Gacha and Gacha.tbGacha then _G.MikuPoolBak=_G.MikuPoolBak or {} local bak=_G.MikuPoolBak local n=0 for id,c in pairs(Gacha.tbGacha) do local pt=c and c.tbPoolTime if type(pt)==[[table]] and pt[1] and pt[2] then if bak[id]==nil then bak[id]={pt[1],pt[2]} end if id~=1 and id~=2 and id~=3 then pt[1]=202001010000 pt[2]=202001020000 n=n+1 end end end print(n) end");
		PushGroupRebuild(conn, 0, new List<Group>
		{
			new Group
			{
				G = 1,
				Icon = 0,
				Name = "gacha.gacha4"
			},
			new Group
			{
				G = 2,
				Icon = 0,
				Name = "gacha.gacha4"
			},
			new Group
			{
				G = 3,
				Icon = 0,
				Name = "gacha.gacha5"
			}
		});
		if (!quiet)
		{
			Log("已注入：游戏默认（常守之誓 / 中庭炉心 / 启程集结）");
		}
	}

	private static string StampOf(string path)
	{
		try
		{
			return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "-";
		}
		catch
		{
			return "-";
		}
	}

	private static string GameDir()
	{
		return Directory.GetCurrentDirectory();
	}

	public static int CurrentDlc()
	{
		uint num = DlcPin.Override();
		if (DlcPin.IsDefault(num))
		{
			// 游戏默认：绝不能回落到第 11 期。2026-11 之前这里的兜底就是 return 11，
			// 只要有一条路径漏判默认态，玩家的卡池就会悄悄变回第 11 期（第 226 节）。
			return (int)DlcPin.DefaultEra;
		}
		return (int)num;
	}

	private static List<EraInfo> LoadVersionTable()
	{
		List<EraInfo> list = new List<EraInfo>();
		string path = Path.Combine(GameDir(), "工具", "卡池版本表.json");
		if (!File.Exists(path))
		{
			return list;
		}
		try
		{
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray))
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				EraInfo eraInfo = new EraInfo
				{
					Dlc = Int(jsonObject, "dlc"),
					Title = Str(jsonObject, "title")
				};
				if (jsonObject["pools"] is JsonArray jsonArray2)
				{
					foreach (JsonNode item2 in jsonArray2)
					{
						if (item2 is JsonObject o)
						{
							eraInfo.Pools.Add(new PoolInfo
							{
								Id = Int(o, "id"),
								Kind = Str(o, "kind"),
								Name = Str(o, "name"),
								Card = Str(o, "card"),
								Pt = Int(o, "pt")
							});
						}
					}
				}
				list.Add(eraInfo);
			}
		}
		catch (Exception ex)
		{
			Log("read 卡池版本表.json failed: " + ex.Message);
		}
		return list;
	}

	private static List<PoolInfo>? LoadFixedPools(int dlc, out string title)
	{
		title = "";
		string path = Path.Combine(GameDir(), "工具", "版本卡池固定表.json");
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray))
			{
				return null;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || Int(jsonObject, "dlc") != dlc)
				{
					continue;
				}
				title = Str(jsonObject, "title");
				List<PoolInfo> list = new List<PoolInfo>();
				if (jsonObject["pools"] is JsonArray jsonArray2)
				{
					foreach (JsonNode item2 in jsonArray2)
					{
						if (item2 is JsonObject o)
						{
							list.Add(new PoolInfo
							{
								Id = Int(o, "id"),
								Kind = Str(o, "kind"),
								Name = Str(o, "name"),
								Card = Str(o, "card"),
								Pt = Int(o, "pt")
							});
						}
					}
				}
				return list;
			}
		}
		catch (Exception ex)
		{
			Log("read 版本卡池固定表.json failed: " + ex.Message);
		}
		return null;
	}

	private static int Int(JsonObject o, string key)
	{
		try
		{
			return o[key]?.GetValue<int>() ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	private static string Str(JsonObject o, string key)
	{
		try
		{
			return o[key]?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static List<Group> BuildGroups(List<PoolInfo> pools)
	{
		List<Group> list = new List<Group>();
		int[] array = new int[2] { 1, 2 };
		foreach (int pt in array)
		{
			foreach (PoolInfo item in pools.Where((PoolInfo x) => x.Pt == pt))
			{
				list.Add(new Group
				{
					G = item.Id,
					Icon = 0,
					Name = $"gacha.ProtectType{pt}"
				});
			}
		}
		foreach (PoolInfo item2 in pools.Where((PoolInfo x) => x.Pt != 1 && x.Pt != 2))
		{
			list.Add(new Group
			{
				G = item2.Id,
				Icon = 0,
				Name = (string.IsNullOrEmpty(item2.Card) ? item2.Name : item2.Card)
			});
		}
		(int, int, string)[] tailGroups = TailGroups;
		foreach ((int, int, string) tuple in tailGroups)
		{
			Group obj = new Group();
			(obj.G, obj.Icon, obj.Name) = tuple;
			list.Add(obj);
		}
		return list;
	}

	public static void Push(Connection conn, bool quiet)
	{
		// 「游戏默认」不等于第 11 期：不 pin 版本时只挂常驻三池（第 171 节）。
		if (DefaultMode())
		{
			PushPermanent(conn, quiet);
			return;
		}
		int dlc = CurrentDlc();
		List<PoolInfo> list = LoadFixedPools(dlc, out string title);
		List<EraInfo> source = LoadVersionTable();
		EraInfo eraInfo = source.FirstOrDefault((EraInfo x) => x.Dlc == dlc);
		List<PoolInfo> list2;
		string value;
		if (list != null)
		{
			list2 = list;
			value = ((!string.IsNullOrEmpty(title)) ? title : (eraInfo?.Title ?? ""));
		}
		else
		{
			if (eraInfo == null)
			{
				if (!quiet)
				{
					Log($"第 {dlc} 期既不在对照表里、也没有人工指定，跳过");
				}
				return;
			}
			list2 = eraInfo.Pools;
			value = eraInfo.Title;
		}
		var (value2, value3) = TargetWindow();
		if (list2.Count > 0)
		{
			// 先还原默认模式压掉的池时间，再按本期重设，否则从「游戏默认」
			// 切到某一期时，上一轮被收到 2020 年的池会一直不显示（第 225 节）。
			Send(conn, "if Gacha and Gacha.tbGacha and _G.MikuPoolBak then for id,b in pairs(_G.MikuPoolBak) do local c=Gacha.tbGacha[id] if c and c.tbPoolTime then c.tbPoolTime[1]=b[1] c.tbPoolTime[2]=b[2] end end end");
			string text = string.Join(",", list2.Select((PoolInfo p) => p.Id));
			Send(conn, "_G.GIDS={" + text + "} print(#GIDS)");
			Send(conn, "if Gacha and Gacha.tbGacha then for i=1,#GIDS do local c=Gacha.tbGacha[GIDS[i]] " + $"if c then c.tbPoolTime[1]={value2} c.tbPoolTime[2]={value3} end end end");
		}
		List<Group> list3 = BuildGroups(list2);
		PushGroupRebuild(conn, dlc, list3);
		if (!quiet)
		{
			Log($"已注入：第 {dlc} 期 {value}（{list2.Count} 个活动池 / {list3.Count} 组）");
		}
	}

	private static void PushGroupRebuild(Connection conn, int dlc, List<Group> groups)
	{
		int count = groups.Count;
		if (count == 0)
		{
			return;
		}
		long num = 0L;
		foreach (Group group in groups)
		{
			num = (num * 131 + group.G) % 1000003;
		}
		// 签名带进程盐：服务端每次重启都会强制重建一次，客户端残留的旧分组能自愈。
		string text = $"g7-{dlc}-{count}-{num}-{Salt}";
		string text2 = "if _G.__GVS~=[[" + text + "]] then ";
		// 每个签名一份独立的暂存表 DGB[签名]，不再共用全局 DG。
		// 原来每次推送第一块都写 DG={}，两次推送撞在一起时，后一块会把前一次
		// 已经填好的 DG 清空，前一次带着只剩 6 项的表提交并把签名写死，
		// 之后所有推送都被 __GVS 挡住 —— 表现就是「卡池只剩特选共鸣」（第 241 节）。
		string text3 = "_G.DGB=_G.DGB or {} local _s=[[" + text + "]] local DG=_G.DGB[_s] if not DG then DG={} _G.DGB[_s]=DG end ";
		for (int i = 0; i < count; i += 6)
		{
			int num2 = Math.Min(i + 5, count - 1);
			List<string> list = new List<string>();
			for (int j = i; j <= num2; j++)
			{
				Group obj = groups[j];
				int value = 1001 + j;
				list.Add($"DG[{j + 1}]={{{value},{obj.G},{obj.Icon},[[{obj.Name}]]}}");
			}
			Send(conn, text2 + text3 + string.Join(" ", list) + " end");
			Thread.Sleep(200);
		}
		// 提交前先数一遍，凑不齐 count 项就不动客户端，避免写进半截列表。
		string text4 = "if not Gacha then return end local _s=[[" + text + "]] local DG=(_G.DGB or {})[_s] local n=0 if DG then for i=1," + count.ToString() + " do if DG[i] then n=n-(-1) end end end if n~=" + count.ToString() + " then return end if Gacha.tbDisplayGroup==nil then Gacha.tbDisplayGroup={} end local d=Gacha.tbDisplayGroup for i=#d,1,-1 do d[i]=nil end " + $"for i=1,{count} do local t=DG[i] if t then local ic=t[3] if ic==0 then ic=nil end " + "d[i]={nId=t[1],nGachaId=t[2],nGroupIcon=ic,sGroupName=t[4]} end end _G.__GVS=[[" + text + "]] print(#d)";
		Send(conn, text2 + text4 + " end");
	}

	private static (long Start, long End) TargetWindow()
	{
		DateTime now = DateTime.Now;
		DateTime t = now.AddDays(-1.0).Date.AddHours(4.0);
		DateTime t2 = now.AddDays(120.0).Date.AddHours(4.0);
		return (Start: Fmt(t), End: Fmt(t2));
	}

	private static long Fmt(DateTime t)
	{
		return long.Parse(t.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}

	private static void Send(Connection conn, string lua)
	{
		string arg = new JsonObject { ["code"] = lua }.ToJsonString();
		CallGSRouter.SendScript(conn, "GM", arg).GetAwaiter().GetResult();
	}

	public static void ResendAll()
	{
		lock (Gate)
		{
			LastSig.Clear();
		}
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			if (!(item.Value is Connection { Player: not null } connection))
			{
				continue;
			}
			int uid = connection.Player.Uid;
			try
			{
				Push(connection, quiet: false);
				MarkPushed(uid, connection, BuildSignature());
			}
			catch (Exception ex)
			{
				Log($"resend uid={uid} failed: {ex.Message}");
				lock (Gate)
				{
					LastSig.Remove(uid);
				}
			}
		}
	}

	public static void Forget(long uid)
	{
		lock (Gate)
		{
			LastSig.Remove(uid);
			LastConn.Remove(uid);
		}
	}

	public static string Describe()
	{
		int value3 = Enumerable.Count(SocketListener.Connections.Values, (SocketConnection x) => x is Connection connection && connection.Player != null);
		if (DefaultMode())
		{
			return $"当前为游戏默认（常守之誓 / 中庭炉心 / 启程集结）｜在线 {value3}";
		}
		int dlc = CurrentDlc();
		List<PoolInfo> list = LoadFixedPools(dlc, out string title);
		EraInfo eraInfo = LoadVersionTable().FirstOrDefault((EraInfo x) => x.Dlc == dlc);
		List<PoolInfo> list2 = list ?? eraInfo?.Pools ?? new List<PoolInfo>();
		string value = ((!string.IsNullOrEmpty(title)) ? title : (eraInfo?.Title ?? ""));
		string value2 = ((list != null) ? "人工指定" : "自动推导");
		List<Group> list3 = BuildGroups(list2);
		return $"当前期数 {dlc}（{value}）｜池 {list2.Count} 个 / 界面 {list3.Count} 组｜来源 {value2}｜在线 {value3}";
	}

	private static void Log(string message)
	{
		try
		{
			string text = Path.Combine(GameDir(), "Config", "Logs");
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "eragacha.log"), DateTime.Now.ToString("HH:mm:ss") + "\t" + message + "\r\n");
		}
		catch
		{
		}
	}
}
