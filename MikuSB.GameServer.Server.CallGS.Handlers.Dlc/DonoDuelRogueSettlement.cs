using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 457 节：探索多诺卡（肉鸽）房间结算奖励。
///
/// 客户端链路（字节码确认）：
///   Req_RoomSettlement → Reconnect.Send_SettleInfo("DonoDuelRogue_RoomSettlement", tbParam)
///   → s2c.tbCmds.DonoDuelRogue_RoomSettlement:
///        tbAward.tbHeroCard[] → DonoDuelRogue:AddFightBagItem(uid)   // 进局内背包
///        EventSystem.Trigger(OnDonoRogueAward, tbAward)              // Mgr.tbAwardMoney += tbAward.tbCash[2]
///        打开 DonoduelDrop 掉落窗
///   关卡结算（DonoDuelRogue_LevelSettlement）只负责打开 DonoDuelSettlement 结算页，
///   页面上「获得毛球币」= Mgr.tbAwardMoney + tbAward.tbCash[2]，「获得多诺卡」= 局内背包。
///
/// 也就是说：房间上一次结算没回 tbAward，才会出现「毛球币 0 / 没有多诺卡」。
/// 奖励数据来自 donoduel_catch_battle（按 donorogue_level 的 CatchID 索引）：
///   Guide_Reward1 = {货币ID, 数量}      首次引导奖励（配置 21：引导 64107 未完成时掉落）
///   Guide_Reward2 = {{卡ID,星级,品质}…} 首次引导多诺卡
/// </summary>
internal static class DonoDuelRogueSettlement
{
	private static readonly Logger Log = new Logger("DonoDuelRogueSettlement");

	/// <summary>首次引导奖励是否已发：属性组 185 的 600+CatchID 位。</summary>
	private const uint GuideFlagSidBase = 600u;

	private sealed class RoomInfo
	{
		public uint CatchId;
		public uint RoomType;

		/// <summary>第几阶（donorogue_level.Stage），对应任务 Param[0]。</summary>
		public uint Stage;

		/// <summary>第几区（donorogue_level.RoomID），对应任务 Param[1]。</summary>
		public uint RoomId;

		/// <summary>本房间打完是否换阶（GoToNext 非空 = 阶 Boss 房，打完这局就结束）。</summary>
		public bool StageEnd;
	}

	private sealed class CatchInfo
	{
		public readonly List<uint> GuideCash = new List<uint>();
		public readonly List<(uint TemplateId, uint Star, uint Quality)> GuideCards = new List<(uint, uint, uint)>();
		public readonly List<uint> Loop = new List<uint>();
	}

	private sealed class PoolInfo
	{
		public uint Number;

		public readonly List<uint> StarWeight = new List<uint>();

		public readonly List<uint> QualityWeight = new List<uint>();

		public readonly List<uint> Currency = new List<uint>();

		public readonly List<(uint BondId, uint Weight)> BondWeight = new List<(uint, uint)>();
	}

	private static Dictionary<uint, RoomInfo>? _rooms;

	private static Dictionary<uint, CatchInfo>? _catch;

	private static Dictionary<(uint Phase, uint RoomType), PoolInfo>? _pools;

	private static List<(uint Id, uint Stars, List<uint> Bonds)>? _heroCards;

	private static readonly Random Rng = new Random();

	/// <summary>上一次结算的房间属于第几阶（185/690），用于判断是否换阶/新开一局。</summary>
	private const uint RoomStageSid = 690u;

	/// <summary>本阶已经结算过几间房（185/691）= 客户端界面上的「第几区」。</summary>
	private const uint RoomCounterSid = 691u;

	/// <summary>房间结算：发首次引导奖励（毛球币 + 多诺卡），返回给客户端的 tbAward。</summary>
	public static JsonObject SettleRoom(PlayerInstance player, JsonObject? param, NtfSyncPlayer sync)
	{
		JsonObject award = new JsonObject();
		List<uint> heroCards = new List<uint>();
		uint cash = 0u;
		try
		{
			// 第 467 节：客户端房间结算里 nLevelID 是「进关时的关卡号」（整局不变，首局都是 5500），
			// 真正换房的是 nRoomId（5500 → 5501 → …）。原先优先取 nLevelID，
			// 打 5501 时会被当成 5500（CatchID 1），于是首通奖励被判成「已经发过」，一个卡/币都不给。
			// 必须以 nRoomId 为准，只有它不在 donorogue_level 里时才退回 nLevelID。
			uint roomId = ReadUint(param?["nRoomId"]) ?? 0u;
			uint enterLevelId = ReadUint(param?["nLevelID"]) ?? 0u;
			uint levelId = roomId;
			if (levelId == 0 || !Rooms().ContainsKey(levelId))
			{
				levelId = enterLevelId;
			}
			uint catchId = ReadUint(param?["nCatchId"]) ?? 0u;
			RoomInfo? room = null;
			if (levelId != 0 && Rooms().TryGetValue(levelId, out RoomInfo? found))
			{
				room = found;
			}
			if (catchId == 0 && room != null)
			{
				catchId = room.CatchId;
			}
			bool guideGiven = false;
			// 第 479 节：catch 表 33 行里只有 CatchID 2（5501 教学战）真的配了 Guide_Reward，
			// 其余全是空。原先只要「有 CatchID 且没发过」就进这个分支，于是第一次进的房间
			// 拿走一个 0 币 0 卡的「引导奖励」并打上已发标记，紧接着的普通掉落被 guideGiven 挡掉。
			// 第 2 阶 6213/6225/6241/6251 日志里的 `guide reward: 0 cash, 0 card(s)` 就是这个。
			// 现在只有策划真配了奖励才走引导，空配置直接落到下面的普通掉落。
			bool hasGuideReward = false;
			CatchInfo? row = null;
			if (catchId != 0 && Catches().TryGetValue(catchId, out row))
			{
				hasGuideReward = row.GuideCards.Count > 0 || (row.GuideCash.Count >= 2 && row.GuideCash[1] != 0);
			}
			if (hasGuideReward && row != null && !IsGuideGiven(player, catchId))
			{
				guideGiven = true;
				MarkGuideGiven(player, catchId, sync);
				if (row.GuideCards.Count > 0)
				{
					heroCards.AddRange(DonoDuelBag.GrantCards(player, row.GuideCards, sync));
				}
				if (row.GuideCash.Count >= 2 && row.GuideCash[1] != 0)
				{
					cash = row.GuideCash[1];
					PortCurrency.Add(player, row.GuideCash[0] == 0 ? DonoDuelGrowth.SeasonCurrency(player) : row.GuideCash[0], cash, sync);
				}
				Log.Info($"donoduel rogue room {levelId} (catch {catchId}) guide reward: {cash} cash, {heroCards.Count} card(s) for uid={player.Uid}.");
			}
			// 第 478 节：除了「首次引导」送死的固定奖励，普通房间清完一波也要按房间类型掉落。
			// 官方表现：普通战斗房 4 卡 + 600 毛球币（1 阶 1 房），精英 6 卡 + 1200，头目 8~10 卡 + 2000。
			// 配置来源 donoduel_catch_room_heropool（按 Phase=关卡 Stage、RoomType 索引）：
			//   Number 掉落张数 / StarWeight、QualityWeight 星级品质权重 / Currency {货币ID,数量}
			//   BondWeight 羁绊权重 → 先抽羁绊，再从带该羁绊的卡里随机取模板。
			// 引导房（Catch 2 首次）仍只给策划配死的固定奖励，避免首通多发。
			if (!guideGiven && room != null && HasKills(param))
			{
				if (Pools().TryGetValue((room.Stage, room.RoomType), out PoolInfo? pool))
				{
					if (pool.Currency.Count >= 2 && pool.Currency[1] != 0)
					{
						uint currencyId = (pool.Currency[0] == 0) ? DonoDuelGrowth.SeasonCurrency(player) : pool.Currency[0];
						PortCurrency.Add(player, currencyId, pool.Currency[1], sync);
						cash += pool.Currency[1];
					}
					if (pool.Number > 0)
					{
						List<(uint TemplateId, uint Star, uint Quality)> picks = RollCards(pool, pool.Number);
						if (picks.Count > 0)
						{
							heroCards.AddRange(DonoDuelBag.GrantCards(player, picks, sync));
						}
					}
					Log.Info($"donoduel rogue room {levelId} (stage {room.Stage}, type {room.RoomType}) drop: {cash} cash, {heroCards.Count} card(s) for uid={player.Uid}.");
				}
			}
			// 第 471 节：没有任何实发奖励时不要回 tbAward。
			// 客户端 s2c（s2_tbCmds_DonoDuelRogue_RoomSettlement.luac）是「tbAward 非 nil 就
			// UI.Open("DonoduelDrop", tbAward)」，所以回一个 tbCash=[币,0] + 空 tbHeroCard 的空壳，
			// 进关卡第一个房间（5500，CatchID 1 没有 Guide_Reward 配置）就会直接弹
			// 「获得奖励：暂无 / 毛球币 0」。之前 || room != null 就是为凑字段，反而是病根。
			if (heroCards.Count > 0 || cash != 0)
			{
				uint currency = DonoDuelGrowth.SeasonCurrency(player);
				award["tbCash"] = new JsonArray(currency, cash);
				JsonArray cards = new JsonArray();
				foreach (uint uid in heroCards)
				{
					cards.Add(uid);
				}
				award["tbHeroCard"] = cards;
			}
			if (room != null)
			{
				TrackRoom(player, room);
			}
			DonoDuelBag.Save(player);
			// 任务进度只在关卡结算（打完整局）推，房间结算不推：
			// 103「前往获取多诺卡」= 到达第1阶第1区，等到一局结束、毛球币/多诺卡到手时再完成，
			// 紧接着的 104 会播 phase1_03（讲解毛球币），正好对得上。
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel rogue room settlement failed: " + ex);
		}
		return award;
	}

	/// <summary>
	/// 第 487 节：统计「这是本阶的第几区」。
	///
	/// donorogue_level 的 RoomID 只够用来区分第 1~3 阶（每阶 4 间房），第 4 阶
	/// 是一局 10 间房、内部房全部复用 RoomID=2 的 12 个关卡（6412/6422/…/6432），
	/// 所以「打完第4阶第10区」用 RoomID 永远判断不出来——203「探索达人3」的
	/// Param=[4,10] 就是这么被卡死的（进度恒 0，结算完既不完成也不弹奖励）。
	/// 官方口径的「区」是这一局里第几间房，因此这里按房间结算次数累加：
	///   RoomID=0（起始房 5500，每局开头都会结算一次）→ 清空计数，视为新的一局；
	///   换阶（Stage 变化）→ 从第 1 区重新数；
	///   同阶 → 计数 +1。
	/// 关卡结算（一局打完）时用「配置 RoomID」和「计数」取大者当区号，
	/// 前 3 阶两者本来就相等，第 4 阶才靠计数拿到 1~10。
	/// </summary>
	private static void TrackRoom(PlayerInstance player, RoomInfo room)
	{
		if (room.RoomId == 0u)
		{
			player.Attributes.Set(185u, RoomStageSid, 0u);
			player.Attributes.Set(185u, RoomCounterSid, 0u);
			return;
		}
		uint stage = player.Attributes.GetValue(185u, RoomStageSid);
		uint counter = player.Attributes.GetValue(185u, RoomCounterSid);
		uint next;
		if (stage != room.Stage)
		{
			next = 1u;
			player.Attributes.Set(185u, RoomStageSid, room.Stage);
		}
		else
		{
			next = counter + 1u;
		}
		player.Attributes.Set(185u, RoomCounterSid, next);
	}

	/// <summary>当前这一阶已经打到第几区；阶对不上返回 0（说明还没结算过这阶的房间）。</summary>
	private static uint CurrentArea(PlayerInstance player, uint stage)
	{
		if (player.Attributes.GetValue(185u, RoomStageSid) != stage)
		{
			return 0u;
		}
		return player.Attributes.GetValue(185u, RoomCounterSid);
	}

	/// <summary>关卡结算：结算页从 Mgr 累加值 + 局内背包取数，这里只补一个空 tbAward 保证字段齐全。</summary>
	public static JsonObject SettleLevel(PlayerInstance? player, JsonObject? param, NtfSyncPlayer sync)
	{
		// 第 467 节：bFail=true 表示这一局是失败的（团灭/中途退出也会走到这里）。
		// 失败的局不算「打到第1阶第1区」，否则死一次就把 103 推成已完成，
		// 首通奖励还没拿到，教程却已经满足条件了。
		bool failed = param?["bFail"] is JsonValue failValue
			&& failValue.TryGetValue<bool>(out bool failFlag) && failFlag;
		if (player != null && !failed)
		{
			AdvanceTasks(player, param, sync);
		}
		uint currency = (player == null) ? DonoDuelGrowth.FallbackSeasonCurrency : DonoDuelGrowth.SeasonCurrency(player);
		return new JsonObject
		{
			["tbCash"] = new JsonArray(currency, 0),
			["tbHeroCard"] = new JsonArray()
		};
	}

	/// <summary>
	/// 第 460 节：把「打到第几阶第几区 / 有没有打完一局」记到剧情任务（Objectives=2）上。
	///
	/// donorogue_level 的 Stage / RoomID 就是任务的 Param[0] / Param[1]：
	///   103「前往获取多诺卡」= [1,1]
	///   108「完成1次多诺卡探索」= [-1,1]（Param[0] = -1 表示只看是否打完一局）
	/// 103 的进度上不去，客户端 CheckMissionAutoGetAward 就不会去领奖，
	/// 紧随其后的 104（AcceptAuto=1，AcceptStory=DLC35_event/DonoDuel_phase1_03）
	/// 也就不会自动接取，于是「打完探索多诺卡没有剧情」。
	/// </summary>
	public static int AdvanceTasks(PlayerInstance player, JsonObject? param, NtfSyncPlayer sync)
	{
		try
		{
			uint levelId = ReadUint(param?["nRoomId"]) ?? ReadUint(param?["nLevelID"]) ?? 0u;
			if (levelId == 0 || !Rooms().TryGetValue(levelId, out RoomInfo? room))
			{
				return 0;
			}
			// 第 487 节：区号 = max(配置 RoomID, 本阶已结算的房间数)。
			// 第 4 阶一局 10 间房但内部房全是 RoomID=2，只靠配置永远到不了第10区，
			// 203「探索达人3」（Param=[4,10]）因此一直完不成。
			uint area = room.RoomId;
			uint counted = (player == null) ? 0u : CurrentArea(player, room.Stage);
			if (counted > area)
			{
				area = counted;
			}
			int num = DonoDuelTaskState.AdvanceRogueMissions(player, room.Stage, area, room.StageEnd, sync);
			if (num > 0)
			{
				Log.Info($"donoduel rogue stage {room.Stage} room {room.RoomId} (area {area}, level {levelId}, stageEnd={room.StageEnd}) advanced {num} mission(s).");
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel rogue task advance failed: " + ex.Message);
			return 0;
		}
	}

	/// <summary>房间结算里 tbKill 非空 = 这一波怪确实被清了，只有清怪房才给掉落。</summary>
	private static bool HasKills(JsonObject? param)
	{
		return param?["tbKill"] is JsonArray kill && kill.Count > 0;
	}

	/// <summary>
	/// 第 478 节：按 heropool 的权重抽卡。
	/// 先按 BondWeight 抽羁绊，再从带这个羁绊的卡里等概率取模板；
	/// 星级/品质按 StarWeight / QualityWeight 抽（下标 +1 即为星级/品质）。
	/// </summary>
	private static List<(uint TemplateId, uint Star, uint Quality)> RollCards(PoolInfo pool, uint count)
	{
		List<(uint, uint, uint)> list = new List<(uint, uint, uint)>();
		List<(uint Id, uint Stars, List<uint> Bonds)> heroCards = HeroCards();
		if (heroCards.Count == 0)
		{
			return list;
		}
		for (uint i = 0; i < count; i++)
		{
			List<(uint Id, uint Stars, List<uint> Bonds)> pool2 = heroCards;
			uint bond = PickWeighted(pool.BondWeight);
			if (bond != 0)
			{
				List<(uint Id, uint Stars, List<uint> Bonds)> matched = heroCards.Where(c => c.Bonds.Contains(bond)).ToList();
				if (matched.Count > 0)
				{
					pool2 = matched;
				}
			}
			var pick = pool2[Rng.Next(pool2.Count)];
			uint star = (uint)(PickIndex(pool.StarWeight) + 1);
			uint quality = (uint)(PickIndex(pool.QualityWeight) + 1);
			star = System.Math.Clamp(star, 1u, System.Math.Min(pick.Stars == 0 ? 5u : pick.Stars, 5u));
			quality = System.Math.Clamp(quality, 1u, 6u);
			list.Add((pick.Id, star, quality));
		}
		return list;
	}

	private static uint PickWeighted(List<(uint BondId, uint Weight)> weights)
	{
		uint total = 0u;
		foreach (var (_, weight) in weights)
		{
			total += weight;
		}
		if (total == 0u)
		{
			return 0u;
		}
		uint roll = (uint)Rng.Next((int)System.Math.Min(total, int.MaxValue));
		foreach (var (bondId, weight) in weights)
		{
			if (roll < weight)
			{
				return bondId;
			}
			roll -= weight;
		}
		return weights[^1].BondId;
	}

	/// <summary>返回抽中的下标；权重全 0 时返回 0（即最低档）。</summary>
	private static int PickIndex(List<uint> weights)
	{
		uint total = 0u;
		foreach (uint weight in weights)
		{
			total += weight;
		}
		if (total == 0u)
		{
			return 0;
		}
		uint roll = (uint)Rng.Next((int)System.Math.Min(total, int.MaxValue));
		for (int i = 0; i < weights.Count; i++)
		{
			if (roll < weights[i])
			{
				return i;
			}
			roll -= weights[i];
		}
		return 0;
	}

	private static List<(uint Id, uint Stars, List<uint> Bonds)> HeroCards()
	{
		if (_heroCards != null)
		{
			return _heroCards;
		}
		List<(uint, uint, List<uint>)> list = new List<(uint, uint, List<uint>)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DonoDuel", "donoduel_herocard.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
			{
				foreach (JsonNode? node in rows)
				{
					if (!(node is JsonObject card) || ReadUint(card["ID"]) is not uint id || id == 0)
					{
						continue;
					}
					List<uint> bonds = new List<uint>();
					if (card["BondID"] is JsonArray bondArray)
					{
						foreach (JsonNode? b in bondArray)
						{
							uint bondId = ReadUint(b) ?? 0u;
							if (bondId != 0)
							{
								bonds.Add(bondId);
							}
						}
					}
					list.Add((id, ReadUint(card["Stars"]) ?? 5u, bonds));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel_herocard.json parse failed: " + ex.Message);
		}
		_heroCards = list;
		return list;
	}

	private static Dictionary<(uint Phase, uint RoomType), PoolInfo> Pools()
	{
		if (_pools != null)
		{
			return _pools;
		}
		Dictionary<(uint, uint), PoolInfo> dict = new Dictionary<(uint, uint), PoolInfo>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DonoDuel", "donoduel_catch_room_heropool.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
			{
				foreach (JsonNode? node in rows)
				{
					if (!(node is JsonObject row) || ReadUint(row["RoomType"]) is not uint roomType)
					{
						continue;
					}
					PoolInfo info = new PoolInfo
					{
						Number = ReadUint(row["Number"]) ?? 0u
					};
					foreach (string key in new string[3] { "StarWeight", "QualityWeight", "Currency" })
					{
						if (!(row[key] is JsonArray values))
						{
							continue;
						}
						List<uint> target = key switch
						{
							"StarWeight" => info.StarWeight,
							"QualityWeight" => info.QualityWeight,
							_ => info.Currency
						};
						foreach (JsonNode? v in values)
						{
							target.Add(ReadUint(v) ?? 0u);
						}
					}
					if (row["BondWeight"] is JsonObject bonds)
					{
						foreach (KeyValuePair<string, JsonNode?> pair in bonds)
						{
							if (uint.TryParse(pair.Key, out uint bondId))
							{
								info.BondWeight.Add((bondId, ReadUint(pair.Value) ?? 0u));
							}
						}
					}
					uint phase = ReadUint(row["Phase"]) ?? 0u;
					dict[(phase, roomType)] = info;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel_catch_room_heropool.json parse failed: " + ex.Message);
		}
		_pools = dict;
		return dict;
	}

	private static bool IsGuideGiven(PlayerInstance player, uint catchId)
	{
		uint sid = GuideFlagSidBase + catchId;
		return player.Attributes.GetValue(185u, sid) != 0;
	}

	private static void MarkGuideGiven(PlayerInstance player, uint catchId, NtfSyncPlayer sync)
	{
		uint sid = GuideFlagSidBase + catchId;
		PlayerAttr attr = player.Attributes.Set(185u, sid, 1u);
		player.Attributes.SyncTo(sync, attr);
	}

	private static Dictionary<uint, RoomInfo> Rooms()
	{
		if (_rooms != null)
		{
			return _rooms;
		}
		Dictionary<uint, RoomInfo> dict = new Dictionary<uint, RoomInfo>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DonoDuel", "DonoRogue", "donorogue_level.json");
			if (File.Exists(path))
			{
				JsonArray? rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (rows != null)
				{
					foreach (JsonNode? node in rows)
					{
						if (node is JsonObject row && ReadUint(row["LevelID"]) is uint levelId && levelId != 0)
						{
							dict[levelId] = new RoomInfo
							{
								CatchId = ReadUint(row["CatchID"]) ?? 0u,
								RoomType = ReadUint(row["RoomType"]) ?? 0u,
								Stage = ReadUint(row["Stage"]) ?? 0u,
								RoomId = ReadUint(row["RoomID"]) ?? 0u,
								StageEnd = !string.IsNullOrWhiteSpace(row["GoToNext"]?.ToString())
							};
						}
					}
				}
			}
			else
			{
				Log.Warn("donorogue_level.json not found; rogue room rewards are disabled.");
			}
		}
		catch (Exception ex)
		{
			Log.Warn("donorogue_level.json parse failed: " + ex.Message);
		}
		_rooms = dict;
		return dict;
	}

	private static Dictionary<uint, CatchInfo> Catches()
	{
		if (_catch != null)
		{
			return _catch;
		}
		Dictionary<uint, CatchInfo> dict = new Dictionary<uint, CatchInfo>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DonoDuel", "donoduel_catch_battle.json");
			if (File.Exists(path))
			{
				JsonArray? rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (rows != null)
				{
					foreach (JsonNode? node in rows)
					{
						if (!(node is JsonObject row) || ReadUint(row["CatchID"]) is not uint catchId || catchId == 0)
						{
							continue;
						}
						CatchInfo info = new CatchInfo();
						if (row["Guide_Reward1"] is JsonArray cash)
						{
							foreach (JsonNode? v in cash)
							{
								info.GuideCash.Add(ReadUint(v) ?? 0u);
							}
						}
						if (row["Guide_Reward2"] is JsonArray cards)
						{
							foreach (JsonNode? card in cards)
							{
								if (card is JsonArray triple && triple.Count >= 3)
								{
									info.GuideCards.Add((ReadUint(triple[0]) ?? 0u, ReadUint(triple[1]) ?? 0u, ReadUint(triple[2]) ?? 0u));
								}
							}
						}
						if (row["Loop"] is JsonArray loop)
						{
							foreach (JsonNode? v in loop)
							{
								info.Loop.Add(ReadUint(v) ?? 0u);
							}
						}
						dict[catchId] = info;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel_catch_battle.json parse failed: " + ex.Message);
		}
		_catch = dict;
		return dict;
	}

	private static uint? ReadUint(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			if (value.TryGetValue<uint>(out uint u))
			{
				return u;
			}
			if (value.TryGetValue<int>(out int i))
			{
				return (uint)Math.Max(0, i);
			}
			if (value.TryGetValue<long>(out long l))
			{
				return (uint)Math.Max(0L, l);
			}
			if (value.TryGetValue<string>(out string? s) && uint.TryParse(s, out uint parsed))
			{
				return parsed;
			}
		}
		return null;
	}
}
