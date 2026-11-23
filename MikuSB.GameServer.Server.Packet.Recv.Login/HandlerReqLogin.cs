using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Progress;
using MikuSB.GameServer.Game.Support;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.Girl;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.GameServer.Server.CallGS.Handlers.Riki;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.GameServer.Server.Packet.Send.Friend;
using MikuSB.GameServer.Server.Packet.Send.Login;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using S1Admin;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(1)]
public class HandlerReqLogin : Handler
{
	private static readonly Logger Logger = new Logger("ReqLogin");

	private const int SupportCardLoginSplitThreshold = 100;

	private static string? ExtractSdkAuthToken(string? token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}
		try
		{
			string text = Uri.UnescapeDataString(token).Trim();
			int num = text.Length % 4;
			if (num > 0)
			{
				text = text.PadRight(text.Length + (4 - num), '=');
			}
			string json = Encoding.UTF8.GetString(Convert.FromBase64String(text));
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty("authToken", out value) ? value.GetString() : null;
		}
		catch
		{
			return null;
		}
	}

	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		ReqLogin req = ReqLogin.Parser.ParseFrom(data);
		string sdkAuthToken = ExtractSdkAuthToken(req.Token);
		AccountData account = AccountData.GetAccountByComboToken(req.Token) ?? AccountData.GetAccountByDispatchToken(req.Token) ?? AccountData.GetAccountByComboToken(sdkAuthToken ?? "") ?? AccountData.GetAccountByDispatchToken(sdkAuthToken ?? "");
		if (account == null && (string.Equals(req.Provider, "development", StringComparison.OrdinalIgnoreCase) || req.Token.StartsWith("Tgame_", StringComparison.OrdinalIgnoreCase)))
		{
			account = AccountData.GetAccountByUid(1) ?? AccountData.GetAccountByUid(ConfigManager.Config.ServerOption.ServerProfile.Uid) ?? AccountData.CreateAccount("development", 1, "123456");
		}
		if (account == null)
		{
			Logger.Warn($"Rejected login: provider={req.Provider}, token={req.Token}, authToken={sdkAuthToken}");
			await connection.SendPacket(1009);
		}
		else
		{
			if (!ResourceManager.IsLoaded)
			{
				return;
			}
			Connection prev = Listener.GetActiveConnection(account.Uid);
			if (prev != null)
			{
				await connection.SendPacket(1009);
				prev.Stop();
			}
			connection.State = SessionStateEnum.WAITING_FOR_LOGIN;
			PlayerGameData pd = DatabaseHelper.GetInstance<PlayerGameData>(account.Uid);
			connection.Player = ((pd == null) ? new PlayerInstance(account.Uid) : new PlayerInstance(pd));
			if (connection.Player.Data.EnsureDisplayName())
			{
				DatabaseHelper.UpdateInstance(connection.Player.Data);
			}
			connection.DebugFile = Path.Combine(ConfigManager.Config.Path.LogPath, "Debug/", $"{account.Uid}/", $"Debug-{DateTime.Now:yyyy-MM-dd HH-mm-ss}.log");
			await connection.Player.OnEnterGame();
			connection.Player.Connection = connection;
			try
			{
				await GrantAllItems.GrantMissing(connection.Player, sendPackets: false);
			}
			catch (Exception value)
			{
				Logger.Error($"GrantAllItems failed: {value}");
			}
			SupportRikiService.Sync(connection.Player);
			try
			{
				RikiHandbookService.Ensure(connection.Player, null);
			}
			catch (Exception ex)
			{
				Logger.Warn("Riki handbook unlock failed: " + ex.Message);
			}
			try
			{
				BagRogueService.UnlockAllRiki(connection.Player, null);
			}
			catch (Exception ex2)
			{
				Logger.Warn("BagRogue riki full unlock failed: " + ex2.Message);
			}
			try
			{
				int rikiUnlocked = RikiHandbookService.UnlockForceTypes(connection.Player, null);
				if (rikiUnlocked > 0)
				{
					Logger.Info($"[RikiHandbook] force unlocked {rikiUnlocked} monster/gift entries");
				}
			}
			catch (Exception ex3)
			{
				Logger.Warn("Riki handbook force unlock failed: " + ex3.Message);
			}
			try
			{
				ActivityWindowGuard.Apply(connection.Player);
			}
			catch (Exception ex4)
			{
				Logger.Warn("ActivityWindowGuard failed: " + ex4.Message);
			}
			ActivityWindowGuard.OnLogin(connection);
			DormDailyEvents.Ensure(connection.Player);
			DormPuzzleService.Ensure(connection.Player);
			// 第 248 节：心意寄语当期期号写进属性 72/1。
			// 客户端 GetActId() = me:GetAttribute(72,1)，为 0 时 tbActConf[0]=nil，
			// 心意寄语界面白屏。必须在下面发 PacketRspLogin（183 行）之前写，
			// 属性才能随登录包一起下发。
			try
			{
				DormActSeed.Apply(connection.Player);
			}
			catch (Exception exDorm)
			{
				Logger.Warn("DormActSeed failed: " + exDorm.Message);
			}
			// 第 249 节：噬神斗场当期期号写进属性 18/1、18/2。
			// 缺了这段，客户端 SeasonBoss.GetCurTimeCfg() 返回 nil，
			// dlc24main 打开时 SeasonBoss.lua:1064 抛错中断，
			// 连带所有入口点击都变成「暂未开放」。
			try
			{
				SeasonBossActSeed.Apply(connection.Player);
			}
			catch (Exception exBoss)
			{
				Logger.Warn("SeasonBossActSeed failed: " + exBoss.Message);
			}
			// 第 366 节（2026-11-16）：捞金派对（chess）当期期号写进属性 12/0。
			// 客户端 ChessLogic.GetOpenID() = me:GetAttribute(12, 0)，
			// 这一步此前从来没在登录流程里执行过，登录后 actId 恒为 0，
			// 「捞金派对」界面因此拿不到地图配置（章节灰、标题空、倒计时空）。
			try
			{
				MikuSB.GameServer.Game.Chess.ChessActivityService.Seed(connection.Player, null);
			}
			catch (Exception exChess)
			{
				Logger.Warn("ChessActivitySeed failed: " + exChess.Message);
			}
			// 第 370 节（2026-11-17）：爆爆菜园（farmwar）当期期号写进属性 164/1。
			// 客户端 FarmWarActivity.GetActivityId() = me:GetAttribute(164, 1)，
			// FarmWarActivityService.Seed 此前全项目零调用，登录后恒为 0，
			// GetItemCfg/GetPlantCfg/GetChapterCfg 全按 tbXxx[0] 查空 →
			// 道具面板全是「道具描述/9999/300」占位、章节进度与解锁判断全部失效。
			try
			{
				MikuSB.GameServer.Game.FarmWar.FarmWarActivityService.Seed(connection.Player, null);
			}
			catch (Exception exFarm)
			{
				Logger.Warn("FarmWarActivitySeed failed: " + exFarm.Message);
			}
			SevenDayProgress.Ensure(connection.Player);
			TaskProgressService.Ensure(connection.Player);
			OnlineMissionAutoComplete.Ensure(connection.Player);
			ActivityQuestProgress.Ensure(connection.Player);
			// 第 450 节：绘梦沙盘「繁荣嘉奖 → 绘梦档案」的任务进度按已通关/星级补齐。
			MikuSB.GameServer.Server.CallGS.Handlers.Dlc.RmolTaskService.Ensure(connection.Player);
			// 第 456 节：多诺-巅峰对决的剧情任务补记。老存档里竞技场关卡早通了、
			// 任务进度却停在 0，导致客户端不会提交任务，也就不会播「正式学习1」之后的剧情。
			try
			{
				if (connection.Player.Attributes.GetValue(185u, 1u) != 0)
				{
					bool donoChanged = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.BackfillPassedArena(connection.Player, null) > 0;
					// 第 470 节：已经在培养界面做过潜能提升的老存档，把 104「前往进行潜能提升」
					// 补记成已完成，否则剧情会一直卡在 104（105 不接取、phase1_04 不播）。
					// 第 476 节：判据放宽成「动过手」（升星 / 品质 / 属性任一），
					// 因为培养教程结束时玩家通常只做了升星和嵌技能。
					if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.HasGrownCards(connection.Player)
						&& MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceObjectiveMissions(
							connection.Player,
							MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialObjectiveId,
							null, 1u,
							MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialUpgradeKind) > 0)
					{
						donoChanged = true;
					}
					// 第 477 节：已经激活过探索加速包（技能树节点）、但 107 还停在 0/1 的老存档，
					// 登录时补记，否则 108 不接取、phase1_07 剧情不播。
					if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.HasBoostPack(connection.Player)
						&& MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceObjectiveMissions(
							connection.Player,
							MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.BoostPackObjectiveId,
							null) > 0)
					{
						donoChanged = true;
					}
					// 第 492 节：收集任务「累计嵌入技能 / 提升星级 / 提升潜能 N 次」
					// 按背包补记下限，否则收集详情整页 0/N、收集奖励点不动。
					if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.BackfillCounterTasks(connection.Player, null) > 0)
					{
						donoChanged = true;
					}
					// 第 494 节：纷愿绮梦奖励页
					//   · 挑战期号写进 152/0（客户端 luckyreward.OnOpen 据此开「纷彩世界」页签）；
					//   · 按属性 152 的通关次数补记「累计完成任意关卡」，否则老存档整页 0/N。
					if (MikuSB.GameServer.Game.Activity.DreamCardAchievementService.EnsureChallPeriod(connection.Player, null) > 0)
					{
						donoChanged = true;
					}
					if (MikuSB.GameServer.Game.Activity.DreamCardAchievementService.BackfillFromLevelRecords(connection.Player, null) > 0)
					{
						donoChanged = true;
					}
					// 第 495 节：已经通关过的关卡补发潜质点（货币 52 → 属性 1/105）。
					// 之前结算只回 nRewardID 不回发货币，天赋树一直是 0 点。152/9 做一次性标记。
					if (MikuSB.GameServer.Server.CallGS.Handlers.DreamCard.DreamCardLevelRewardTable.BackfillLevelPoints(connection.Player, null) > 0)
					{
						donoChanged = true;
					}
					// 第 497 节：新星开拓蓝图岛任务（属性组 163）的引导占位条目（100 号）
					// 置成「已领」，否则主线 101 前置不成立，蓝图岛任务列表整页为空。
					// 第 502 节：包含引导占位（497）与「建筑已建好」的 Type 2 主线任务补完成。
					if (MikuSB.GameServer.Server.CallGS.Handlers.VirCapture.VirCaptureBuildTaskState.SyncAll(connection.Player, null) > 0)
					{
						donoChanged = true;
					}
					if (donoChanged)
					{
						DatabaseHelper.SaveDatabaseType(connection.Player.Data);
					}
				}
			}
			catch (Exception exDono)
			{
				Logger.Warn("DonoDuel story backfill failed: " + exDono.Message);
			}
			// 第 462 节：老存档里多诺引导 64100~64112 被初始化写成了 999（已跳过），
			// 导致新手教程不播、首次进入肉鸽的配死奖励与退出房间也不触发。
			// 这里做一次性迁移，把它们清零（迁移标记 4/64199）。
			try
			{
				MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelGuideState.Ensure(connection.Player, null);
			}
			catch (Exception exDonoGuide)
			{
				Logger.Warn("DonoDuel guide reset failed: " + exDonoGuide.Message);
			}
			// 第 463 节：首次进入探索多诺卡没有初始卡、肉鸽编队为空，进关卡后
			// 局内背包是空的（编队界面「暂无多诺卡」），引导 64107 无卡可选。
			// 登录时补发任务 101 的 3 张初始卡并自动填一次肉鸽编队（185/10~13）。
			try
			{
				MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelStarterDeck.Ensure(connection.Player, null);
			}
			catch (Exception exDonoDeck)
			{
				Logger.Warn("DonoDuel starter deck failed: " + exDonoDeck.Message);
			}
			// 回归任务（player_return）：此前只有领奖 handler 才会 Ensure，登录时没人设置
			// 0/15（回归活动 id）与 50/(actId*10+3)（个人回归起点），客户端因此判定
			// 「不是回归玩家」而不显示入口。这里在登录流程里补一次：新号/老号登录即成为
			// 回归账号，起点按配置 ActiveDay 回拨，之后随自然时间推进、领完奖励自然消失。
			try
			{
				PlayerReturnService.RollDailyState(connection.Player);
			}
			catch (Exception exReturn)
			{
				Logger.Warn("PlayerReturn ensure failed: " + exReturn.Message);
			}
			await FirstRechargeService.Ensure(connection);
			bool splitSupportCards = connection.Player.InventoryManager.InventoryData.SupportCards.Count > 100;
			BagRogueService.Seed(connection.Player, null);
			BagRogueScheduleExcel rogueSchedule = BagRogueService.ScheduleOf(connection.Player);
			if (rogueSchedule != null)
			{
				BagRogueService.AlignEraMap(connection.Player, rogueSchedule, null);
			}
			// 体力恢复：溢出归一 + 按 5 分钟 1 点补齐，锚点存 9900/1（第 180 节）
			if (VigorRecovery.Apply(connection.Player))
			{
				DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			}
			await connection.SendPacket(new PacketRspLogin(connection.Player, !splitSupportCards), 0);
			// 第 435 节：登录时补推一次个人签名（opcode 24 / RspResign）。
			// 客户端本地 me.Sign 只在收到 24 时才会被写值，而登录流程里客户端自己不发
			// ReqResign（实测 12:19 那次登录全程没有 ReqResign），所以重登后签名回落到默认。
			// 这里把库里存的签名主动下发一次，报文格式与 HandlerReqResign 的响应完全一致
			// （RspResign 只有一个 string 字段，和 StringValue 线格式相同）。
			string loginSign = connection.Player.Data.Signature ?? string.Empty;
			if (loginSign.Length > 0)
			{
				await connection.SendPacket(24, new StringValue { Value = loginSign }, 0);
			}
			NtfSyncPlayer vigorSync = new NtfSyncPlayer();
			vigorSync.Core[0u] = connection.Player.Data.Level;
			vigorSync.Core[1u] = (uint)Math.Max(0, connection.Player.Data.Exp);
			vigorSync.Core[2u] = connection.Player.Data.Vigor;
			vigorSync.Core[4u] = connection.Player.Attributes.GetValue(9900u, 1u);
			await connection.SendPacket(1004, vigorSync, 0);
			if (splitSupportCards)
			{
				await SendSupportCardsOnLogin(connection);
			}
			await SendPlayerSync1004(connection);
			// 第 432 节：每天登录自动签到并领奖（signin_monthly 全部活动，过期的不碰）。
			try
			{
				NtfSyncPlayer signSync = await ActivityDaySignService.AutoSignOnLogin(connection.Player);
				if (signSync != null)
				{
					await connection.SendPacket(1004, signSync, 0);
				}
			}
			catch (Exception exSign)
			{
				Logger.Warn("每日签到自动领取失败: " + exSign.Message);
			}
			// 第 439 节：星祈心愿「希冀点缀」每天 04:00 换日，清任务进度 + 今日已领标记。
			try
			{
				NtfSyncPlayer wishSync = StarWishDailyService.EnsureReset(connection.Player, save: true);
				if (wishSync != null)
				{
					await connection.SendPacket(1004, wishSync, 0);
				}
			}
			catch (Exception exWish)
			{
				Logger.Warn("星祈心愿换日重置失败: " + exWish.Message);
			}
			// 第 442 节：冬日接力战（WorldBoss）登录同步 —— 每日挑战次数、期数，
			// 顺带把老存档里写错的阵容编码（角色 Guid → detail*100+particular）修正，
			// 并把已有成绩补进本服单机排行榜（WorldBoss_Rank_<ReferRank1>）。
			try
			{
				NtfSyncPlayer wbSync = new NtfSyncPlayer();
				MikuSB.GameServer.Game.WorldBoss.WorldBossService.Seed(connection.Player, wbSync);
				if (wbSync.Custom.Count > 0)
				{
					await connection.SendPacket(1004, wbSync, 0);
				}
			}
			catch (Exception exWb)
			{
				Logger.Warn("冬日接力战登录同步失败: " + exWb.Message);
			}
			await SendDebugLoginState(connection);
			EraGacha.OnLogin(connection);
			DlcWindowPush.OnLogin(connection);
			DlcClientPatches.OnLogin(connection);
			DlcMainEntry.OnLogin(connection);
			ChapterCard.OnLogin(connection);
			DlcEraGuards.OnLogin(connection);
			MainSurveyHide.OnLogin(connection);
			MainRedDotHide.OnLogin(connection);
			await MailService.SendMailsOnLogin(connection);
			// 2026-11-13（修复记录第 283 节）：补发历史「已赠送但好感没加上」的礼物（幂等）。
			await RepairGiftFavorOnLogin(connection);
			await connection.Player.OnHeartBeat();
			await connection.SendPacket(new PacketNtfUpdateFriend(connection.Player), 0);
			await connection.SendPacket(new PacketNtfFriendVigor(connection.Player), 0);
			ApplySavedGirlSkinTypes(connection.Player);
			await SendGirlSkinTypeOnLogin(connection);
		}
	}

	/// <summary>
	/// 2026-11-13 追加（修复记录第 283 节）：登录时补发历史「已赠送礼物」的好感。
	/// 旧版送礼把礼物消耗、房间家具位也置了，但好感没加；客户端又用家具位判断「已赠送」，
	/// 这些礼物再也送不出去 → 这里按已置位槽位一次性补发（幂等，标记写 Config/Database/giftfavor_&lt;uid&gt;.json）。
	/// </summary>
	private static async Task RepairGiftFavorOnLogin(Connection connection)
	{
		try
		{
			NtfSyncPlayer giftSync = new NtfSyncPlayer();
			(int Applied, uint Love, uint House, int Girls) repair = await HouseGiftRepair.RepairAsync(connection, giftSync);
			if (repair.Applied <= 0)
			{
				return;
			}
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			await connection.SendPacket(1004, giftSync, 0);
			Logger.Info($"gift favor repair on login: {repair.Applied} gifts, +{repair.Love} love ({repair.Girls} girls), +{repair.House} house");
		}
		catch (Exception ex)
		{
			Logger.Error("gift favor repair failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 第 425 节：支援卡分批同步原来每块发一条 1010 NtfCallScript，Api 是空串，
	/// 客户端 S2C.lua:29-32 查 s2c.tbCmds[""] 拿不到处理器就抛
	/// "s2c.Dispatch error. No command named: "（一次登录 5 条 LogUnLua Error）。
	/// 这些包的包体本质就是 NtfSyncPlayer（ExtraSync），改成 1004 直发，
	/// C++ 收到 1004 直接应用同步，不会进 Lua 的 Dispatch。
	/// </summary>
	private static async Task SendSupportCardsOnLogin(Connection connection)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		List<GameSupportCardInfo> supportCards = player.InventoryManager.InventoryData.SupportCards.Values.ToList();
		Logger.Info($"Split support card sync on login: total={supportCards.Count}, chunkSize={100}");
		foreach (GameSupportCardInfo[] chunk in supportCards.Chunk(100))
		{
			NtfSyncPlayer cardSync = new NtfSyncPlayer();
			foreach (GameSupportCardInfo card in chunk)
			{
				cardSync.Items.Add(ToSupportCardProto(card));
			}
			await connection.SendPacket(1004, cardSync, 0);
		}
	}

	/// <summary>第 425 节：与 PacketNtfCallScript.ToSupportCardProto 同一逻辑（那份是私有）。</summary>
	private static Item ToSupportCardProto(GameSupportCardInfo card)
	{
		SupportAffixStateService.NormalizePendingState(card);
		Item item = card.ToProto();
		item.Slots[1u] = SupportAffixStateService.GetVisibleInitialAffixIndex(card);
		return item;
	}

	/// <summary>
	/// 第 425 节：登录时最后一包把玩家全量属性 + 货币推给客户端，原来也是 1010 空 Api，
	/// 是 6 条空名包里最后一条。同样改成 1004，内容不变（Custom + Money）。
	/// </summary>
	private static async Task SendPlayerSync1004(Connection connection)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		try
		{
			player.BuildPlayerAttr();
			NtfSyncPlayer sync = new NtfSyncPlayer();
			foreach (PlayerAttr attr in player.Data.Attrs)
			{
				uint gid = attr.Gid;
				uint sid = attr.Sid;
				uint val = attr.Val;
				if (gid == 0)
				{
					sync.Custom[sid] = val;
					continue;
				}
				sync.Custom[player.ToPackedAttrKey(gid, sid)] = val;
				sync.Custom[player.ToShiftedAttrKey(gid, sid)] = val;
			}
			foreach (var (key, value) in player.BuildMoneySync())
			{
				sync.Money[key] = value;
			}
			await connection.SendPacket(1004, sync, 0);
		}
		catch (Exception ex)
		{
			Logger.Error("player sync on login failed: " + ex.Message);
		}
	}

	private static void ApplySavedGirlSkinTypes(PlayerInstance player)
	{
		InventoryData inventoryData = player.InventoryManager.InventoryData;
		InventoryData inventoryData2 = inventoryData;
		if (inventoryData2.SkinTypesBySkinId == null)
		{
			Dictionary<uint, uint> dictionary = (inventoryData2.SkinTypesBySkinId = new Dictionary<uint, uint>());
		}
		bool flag = false;
		KeyValuePair<uint, uint>[] array = inventoryData.SkinTypesBySkinId.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			var (num3, num4) = array[i];
			uint num5 = GirlSkin_ChangeSkinType.ClampClientSkinType(num4);
			if (num5 != num4)
			{
				inventoryData.SkinTypesBySkinId[num3] = num5;
				flag = true;
			}
			GameSkinInfo orCreateSkinItem = GirlSkin_ChangeSkinType.GetOrCreateSkinItem(player, num3);
			if (orCreateSkinItem != null && orCreateSkinItem.SkinType != num5)
			{
				orCreateSkinItem.SkinType = num5;
				flag = true;
			}
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(inventoryData);
		}
	}

	private static async Task SendGirlSkinTypeOnLogin(Connection connection)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		InventoryData inventoryData = player.InventoryManager.InventoryData;
		InventoryData inventoryData2 = inventoryData;
		if (inventoryData2.SkinTypesBySkinId == null)
		{
			inventoryData2.SkinTypesBySkinId = new Dictionary<uint, uint>();
		}
		foreach (var (skinId, skinType) in inventoryData.SkinTypesBySkinId)
		{
			uint clamped = GirlSkin_ChangeSkinType.ClampClientSkinType(skinType);
			GameSkinInfo skinData = GirlSkin_ChangeSkinType.GetOrCreateSkinItem(player, skinId);
			JsonObject response = new JsonObject
			{
				["nType"] = clamped,
				["nSkinId"] = skinId
			};
			if (skinData == null)
			{
				await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString());
				continue;
			}
			await CallGSRouter.SendScript(connection, "GirlSkin_ChangeSkinType", response.ToJsonString(), new NtfSyncPlayer
			{
				Items = { skinData.ToProto() }
			});
		}
	}

	private static async Task SendDebugLoginState(Connection connection)
	{
		JsonObject response = new JsonObject { ["IsDebug"] = ConfigManager.Config.ServerOption.EnableGmMenu };
		await CallGSRouter.SendScript(connection, "gm.notifylogin", response.ToJsonString());
	}
}
