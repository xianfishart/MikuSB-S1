using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Lineup;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.GachaTry;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;
using MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;
using MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;
using MikuSB.GameServer.Server.CallGS.Handlers.Settlement;
using MikuSB.GameServer.Server.CallGS.Handlers.Tower;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_DealLevelSettlement")]
public class Chapter_DealLevelSettlement : ICallGSHandler
{
	private static readonly Logger Logger = new Logger("Settle");

	private static Dictionary<uint, JsonObject>? _dailyLevels;

	private static Dictionary<uint, JsonObject>? _levelNodes;

	private static Dictionary<int, int>? _maxExp;

	private static HashSet<uint>? _bossLevels;

	private static Dictionary<uint, JsonObject>? _towerLevels;

	private static Dictionary<uint, JsonObject>? _roleLevels;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		DealLevelSettlementParam req = JsonSerializer.Deserialize<DealLevelSettlementParam>(param);
		Logger.Info($"[settle] sCmd={req?.SCmd} level={(req?.TbParam as JsonObject)?["nID"]}");
		if (string.Equals(req?.SCmd, "SeasonBoss_LevelSettlement", StringComparison.Ordinal))
		{
			JsonObject sbParam = req?.TbParam as JsonObject;
			NtfSyncPlayer sbSync = new NtfSyncPlayer();
			SeasonBossSettlementWriter.Write(connection, sbParam, sbSync);
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			JsonObject sbPayload = new JsonObject
			{
				["tbAward"] = new JsonArray(),
				["nNewScore"] = (int.TryParse(sbParam?["nDamage"]?.ToString(), out var sbScore) ? sbScore : 0)
			};
			JsonObject sbResponse = new JsonObject
			{
				["sCmd"] = "SeasonBoss_LevelSettlement",
				["tbParam"] = sbPayload
			};
			await CallGSRouter.SendScript(connection, "Chapter_DealLevelSettlement", sbResponse.ToJsonString(), sbSync);
		}
		else if (string.Equals(req?.SCmd, "Rogue3DScoreModel_ScoreLevelSettlement", StringComparison.Ordinal) || string.Equals(req?.SCmd, "Rogue3DScoreModel_BuffLevelSettlement", StringComparison.Ordinal))
		{
			JsonObject tb = req?.TbParam as JsonObject;
			int battleId = (tb?["nBattleId"]?.GetValue<int>()).GetValueOrDefault();
			int score = 0;
			try
			{
				JsonNode jsonNode = tb?["tbLog"]?["LevelFinish"];
				JsonArray levelLog = jsonNode as JsonArray;
				if (levelLog != null && levelLog.Count > 5)
				{
					int.TryParse(levelLog[5]?.ToString(), out score);
				}
			}
			catch
			{
			}
			PlayerInstance player = connection.Player;
			uint sid = (uint)(2400 + Math.Max(1, battleId));
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = 124u,
					Sid = sid,
					Val = 0u
				};
				player.Data.Attrs.Add(attr);
			}
			int oldScore = (int)attr.Val;
			if (score > oldScore)
			{
				attr.Val = (uint)score;
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.Custom[player.ToPackedAttrKey(124u, sid)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(124u, sid)] = attr.Val;
			JsonObject payload;
			if (string.Equals(req?.SCmd, "Rogue3DScoreModel_BuffLevelSettlement", StringComparison.Ordinal))
			{
				// 第 312 节：增益试炼原先这里只回 {"tbAward":[], "tbMonsterDropRiki":[]}，
				// 连 tbUavPart 都不带 —— 结算页部件栏空白、属性槽也没写入，等于通关零部件。
				// 改走 materialbuffdrop 真结算：抽部件写属性组 124（3105~3364），金币走货币/物品发放。
				payload = await Rogue3DScoreModel_BuffLevelSettlement.SettleAsync(player, tb, sync);
				payload["nNewScore"] = score;
				payload["nOldScore"] = oldScore;
			}
			else
			{
				payload = new JsonObject
				{
					["tbAward"] = new JsonArray(),
					["tbMonsterDropRiki"] = new JsonArray(),
					["nNewScore"] = score,
					["nOldScore"] = oldScore,
					["tbChallengeMap"] = new JsonObject()
				};
			}
			JsonObject settleResponse = new JsonObject
			{
				["sCmd"] = req?.SCmd,
				["tbParam"] = payload
			};
			await CallGSRouter.SendScript(connection, "Chapter_DealLevelSettlement", settleResponse.ToJsonString(), sync);
		}
		else if (string.Equals(req?.SCmd, "GachaTry_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonObject Response, NtfSyncPlayer Sync) tuple = await GachaTryService.Settle(actId: (req?.TbParam?["nActId"]?.GetValue<int>()).GetValueOrDefault(), girlIdx: (req?.TbParam?["nGirlIdx"]?.GetValue<int>()).GetValueOrDefault(), player: connection.Player);
			JsonObject award = tuple.Response;
			NtfSyncPlayer gachaSync = tuple.Sync;
			JsonObject ack = new JsonObject
			{
				["sCmd"] = "GachaTry_LevelSettlement",
				["tbParam"] = award.DeepClone()
			};
			await CallGSRouter.SendScript(connection, "Chapter_DealLevelSettlement", ack.ToJsonString(), gachaSync);
			JsonArray awardArray = (award["tbAward"] as JsonArray) ?? new JsonArray();
			await CallGSRouter.SendScript(connection, "GachaTry_LevelSettlement", awardArray.ToJsonString(), gachaSync);
		}
		else
		{
			SettlementContext settlementContext = new SettlementContext();
			string effectiveCmd = req?.SCmd;
			if (string.IsNullOrEmpty(effectiveCmd))
			{
				JsonNode jsonNode = req?.TbParam;
				effectiveCmd = ((jsonNode is JsonObject infer && infer["nRoleID"] != null) ? "Role_LevelSettlement" : "Chapter_LevelSettlement");
			}
			JsonNode settlementPayload = await BuildSettlementPayload(connection, effectiveCmd, req?.TbParam, settlementContext);
			if (settlementContext.Sync != null)
			{
				await connection.SendPacket(1004, settlementContext.Sync, 0);
			}
			JsonObject response = new JsonObject
			{
				["sCmd"] = req?.SCmd ?? "Chapter_LevelSettlement",
				["tbParam"] = settlementPayload
			};
			await CallGSRouter.SendScript(connection, "Chapter_DealLevelSettlement", response.ToJsonString(), settlementContext.Sync);
		}
	}

	private static async Task<JsonNode> BuildSettlementPayload(Connection connection, string? sCmd, JsonNode? tbParam, SettlementContext context)
	{
		context.Sync = null;
		// 第 370 节（2026-11-17）：爆爆菜园结算走统一入口（Reconnect.Send_SettleInfo → 本 API），
		// 此前没有 FarmWar 分支，落到 Chapter_LevelSettlement 默认路径，通关记录 (22, levelId)
		// 从不写入、从不落库。客户端 FarmWarLevelTemplate.GetPassTime 读
		// me:GetAttribute(Launch.GPASSID=22, nID)，恒为 0 → IsPass 恒 false →
		// 下一关/下一章（UnlockCondition）永不解锁，蔬菜伙伴培育也不推进。
		if (string.Equals(sCmd, "FarmWarLogic_LevelSettlement", StringComparison.Ordinal))
		{
			JsonArray fwAwards = new JsonArray();
			JsonObject fwSettle = tbParam as JsonObject;
			if (fwSettle != null && connection.Player != null && uint.TryParse(fwSettle["nID"]?.ToString(), out var fwLevelId) && fwLevelId != 0)
			{
				PlayerInstance fwPlayer = connection.Player;
				NtfSyncPlayer fwSync = new NtfSyncPlayer();
				// 第 380 节：结算包字段 F = 本局新增/购买的培育包状态，先落库（与关卡是否在表里无关）。
				JsonNode fwBuffs = fwSettle["F"];
				FarmWarActivityService.RecordBuffStates(fwPlayer, fwBuffs == null ? null : JToken.Parse(fwBuffs.ToJsonString()), fwSync);
				if (GameData.FarmWarLevelData.ContainsKey(fwLevelId))
				{
					uint fwHealth = (uint)Math.Max(0, ReadInt(fwSettle["H"]));
					uint fwWave = (uint)Math.Max(0, ReadInt(fwSettle["W"]));
					FarmWarActivityService.RecordClear(fwPlayer, fwLevelId, fwHealth, fwWave, fwSync);
					FarmWarActExcel fwAct = FarmWarActivityService.CurrentAct(fwPlayer) ?? FarmWarActivityService.SelectAct(DateTime.UtcNow);
					if (fwAct != null)
					{
						FarmWarActivityService.MarkQuestProgress(fwPlayer, fwAct, fwLevelId, fwSync);
					}
					JsonArray fwRows = await GrantFarmWarLevelRewardsAsync(fwPlayer, fwLevelId, fwSync);
					foreach (JsonNode fwRow in fwRows)
					{
						fwAwards.Add(fwRow?.DeepClone());
					}
					context.Sync = fwSync;
					DatabaseHelper.SaveDatabaseType(fwPlayer.Data);
					DatabaseHelper.SaveDatabaseType(fwPlayer.InventoryManager.InventoryData);
				}
				else
				{
					Logger.Warn($"[settle] farmwar unknown stage {fwLevelId}");
				}
			}
			return fwAwards;
		}
		if (string.Equals(sCmd, "Rogue3DScoreModel_ScoreLevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "Rogue3DScoreModel_BuffLevelSettlement", StringComparison.Ordinal))
		{
			NtfSyncPlayer rbSync = new NtfSyncPlayer();
			JsonObject rbParam = tbParam as JsonObject;
			(int, int, uint) tuple = Rogue3DScoreSettle.ReadPayload(rbParam);
			int score = tuple.Item1;
			int battleId = tuple.Item2;
			uint levelId = tuple.Item3;
			bool newRecord = false;
			uint best = 0u;
			if (connection.Player != null && levelId != 0)
			{
				PlayerInstance player = connection.Player;
				newRecord = Rogue3DScoreSettle.Record(player, battleId, levelId, score, rbSync);
				best = Rogue3DScoreSettle.ReadBest(player, battleId);
				context.Sync = rbSync;
				DatabaseHelper.SaveDatabaseType(player.Data);
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
			return new JsonObject
			{
				["nScore"] = score,
				["nBestScore"] = (int)best,
				["bNewRecord"] = newRecord,
				["tbAward"] = new JsonArray(),
				["tbReward"] = new JsonArray(),
				["bSuccess"] = true
			};
		}
		if (string.Equals(sCmd, "Repression_Chapter_LevelSettlement", StringComparison.Ordinal))
		{
			NtfSyncPlayer repSync = new NtfSyncPlayer();
			JsonArray repAwards = new JsonArray();
			int repScore = 0;
			JsonObject repParam = tbParam as JsonObject;
			if (repParam != null && connection.Player != null && uint.TryParse(repParam["nID"]?.ToString(), out var repLevelId) && repLevelId != 0)
			{
				PlayerInstance player2 = connection.Player;
				bool wasPassed = player2.IsLevelReallyPassed(repLevelId);
				uint levelScore = RepressionState.Settle(player2, repLevelId, repParam, repSync);
				repScore = (int)levelScore;
				ILevelRewardConfig repConfig = ResolveTypedLevelConfig(repLevelId);
				if (repConfig != null)
				{
					JsonArray repRows = await player2.RewardManager.GrantLevelRewardsAsync(repConfig, !wasPassed, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), repSync);
					JsonArray group = new JsonArray();
					foreach (JsonNode item in repRows)
					{
						group.Add(item?.DeepClone());
					}
					if (group.Count > 0)
					{
						repAwards.Add(group);
					}
				}
				else
				{
					JsonObject repLevel = LoadLevelNode(repLevelId);
					if (repLevel != null)
					{
						JArray rewardRows = await LevelRewardHelper.GrantAsync(player2, repLevel, repSync, wasPassed);
						JsonArray group2 = new JsonArray();
						foreach (JToken rowNode in rewardRows)
						{
							if (!(rowNode is JArray row))
							{
								continue;
							}
							JsonArray converted = new JsonArray();
							foreach (JToken value in row)
							{
								converted.Add((uint)value);
							}
							group2.Add(converted);
						}
						if (group2.Count > 0)
						{
							repAwards.Add(group2);
						}
					}
				}
				context.Sync = repSync;
				DatabaseHelper.SaveDatabaseType(player2.Data);
				DatabaseHelper.SaveDatabaseType(player2.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player2.InventoryManager.InventoryData);
			}
			return new JsonObject
			{
				["tbAward"] = repAwards,
				["nScore"] = repScore,
				["nNewRecord"] = false
			};
		}
		// 异绪消解（女武神突进）结算：客户端不直连 GirlRush_LevelSettlement，
		// 而是走本统一入口，sCmd = "GirlRush_LevelSettlement"，tbParam 带 nID / nScoreLevel。
		// 落库：属性 22 = 通关用时（客户端 IsPass 判断它 > 0），
		// 属性 158 的 bit26..30 = 历史评价等级（越小越好）。（修复记录第 151 节）
		if (string.Equals(sCmd, "GirlRush_LevelSettlement", StringComparison.Ordinal))
		{
			JsonObject girlRushParam = tbParam as JsonObject;
			int girlRushScoreLevel = ReadInt(girlRushParam?["nScoreLevel"]);
			uint girlRushLevelId = (uint)Math.Max(0, ReadInt(girlRushParam?["nID"]));
			uint girlRushPassTime = 0u;
			PlayerInstance girlRushPlayer = connection.Player;
			if (girlRushPlayer != null && girlRushLevelId != 0)
			{
				girlRushPassTime = GirlRushStore.TakePassTime(girlRushPlayer.Uid, girlRushLevelId);
				GirlRushStore.WritePassTime(girlRushPlayer, girlRushLevelId, girlRushPassTime);
				GirlRushStore.WriteRating(girlRushPlayer, girlRushLevelId, (uint)Math.Max(0, girlRushScoreLevel));
				NtfSyncPlayer grSync = new NtfSyncPlayer();
				uint grPass = girlRushPlayer.Attributes.GetValue(GirlRushStore.PassGid, girlRushLevelId);
				uint grData = girlRushPlayer.Attributes.GetValue(GirlRushStore.AttrGid, girlRushLevelId);
				grSync.Custom[girlRushPlayer.ToPackedAttrKey(GirlRushStore.PassGid, girlRushLevelId)] = grPass;
				grSync.Custom[girlRushPlayer.ToShiftedAttrKey(GirlRushStore.PassGid, girlRushLevelId)] = grPass;
				grSync.Custom[girlRushPlayer.ToPackedAttrKey(GirlRushStore.AttrGid, girlRushLevelId)] = grData;
				grSync.Custom[girlRushPlayer.ToShiftedAttrKey(GirlRushStore.AttrGid, girlRushLevelId)] = grData;
				context.Sync = grSync;
				DatabaseHelper.SaveDatabaseType(girlRushPlayer.Data);
				GirlRushStore.AppendDebug($"[Settlement] level={girlRushLevelId} passTime={girlRushPassTime} scoreLevel={girlRushScoreLevel} attr158={grData} attr22={grPass}");
			}
			return new JsonObject
			{
				["tbAward"] = new JsonArray(),
				["tbReward"] = new JsonArray(),
				["nScore"] = 0,
				["nScoreLevel"] = girlRushScoreLevel,
				["nPassTime"] = girlRushPassTime,
				["bSuccess"] = true
			};
		}
		if (string.Equals(sCmd, "Chapter_LevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "DLC_Chapter_LevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "Role_LevelSettlement", StringComparison.Ordinal))
		{
			JsonArray awards = new JsonArray();
			JsonObject settle = tbParam as JsonObject;
			if (settle != null && connection.Player != null && uint.TryParse(settle["nID"]?.ToString(), out var levelId2) && levelId2 != 0)
			{
				PlayerInstance player3 = connection.Player;
				NtfSyncPlayer rewardSync = new NtfSyncPlayer();
				bool wasPassed2 = player3.IsLevelReallyPassed(levelId2);
				uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				player3.SetAttr(22u, levelId2, now);
				rewardSync.Custom[player3.ToPackedAttrKey(22u, levelId2)] = now;
				rewardSync.Custom[player3.ToShiftedAttrKey(22u, levelId2)] = now;
				int starCount = 0;
				int.TryParse(settle["nStar"]?.ToString(), out starCount);
				if (starCount > 0)
				{
					uint mask = ((starCount >= 3) ? 7u : ((uint)((1 << starCount) - 1)));
					PlayerAttr starAttr = player3.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == levelId2);
					if (starAttr == null)
					{
						starAttr = new PlayerAttr
						{
							Gid = 21u,
							Sid = levelId2,
							Val = 0u
						};
						player3.Data.Attrs.Add(starAttr);
					}
					starAttr.Val |= mask;
					rewardSync.Custom[player3.ToPackedAttrKey(21u, levelId2)] = starAttr.Val;
					rewardSync.Custom[player3.ToShiftedAttrKey(21u, levelId2)] = starAttr.Val;
				}
				JsonObject roleNode = LoadRoleLevelNode(levelId2);
				JsonObject towerNode = LoadTowerEventLevelNode(levelId2);
				JsonObject levelNode = LoadLevelNode(levelId2) ?? roleNode ?? towerNode;
				AchievementProgress.MarkLevel(player3, levelId2, rewardSync);
				TaskEventService.OnLevelClear(player3, levelId2);
				TaskEventService.OnLevelSettlement(player3, levelNode, tbParam);
				if (towerNode != null)
				{
					PlayerAttr defendAttr = player3.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == 10609);
					if (defendAttr == null)
					{
						defendAttr = new PlayerAttr
						{
							Gid = 2u,
							Sid = 10609u,
							Val = 0u
						};
						player3.Data.Attrs.Add(defendAttr);
					}
					defendAttr.Val = (defendAttr.Val & 1) | 2;
					rewardSync.Custom[player3.ToPackedAttrKey(2u, 10609u)] = defendAttr.Val;
					rewardSync.Custom[player3.ToShiftedAttrKey(2u, 10609u)] = defendAttr.Val;
				}
				if (IsBossLevel(levelId2))
				{
					PlayerAttr bossAttr = player3.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == 10408);
					if (bossAttr == null)
					{
						bossAttr = new PlayerAttr
						{
							Gid = 2u,
							Sid = 10408u,
							Val = 0u
						};
						player3.Data.Attrs.Add(bossAttr);
					}
					bossAttr.Val = (bossAttr.Val & 1) | 2;
					rewardSync.Custom[player3.ToPackedAttrKey(2u, 10408u)] = bossAttr.Val;
					rewardSync.Custom[player3.ToShiftedAttrKey(2u, 10408u)] = bossAttr.Val;
				}
				if (roleNode != null)
				{
					PlayerAttr storyAttr = player3.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == 10306);
					if (storyAttr == null)
					{
						storyAttr = new PlayerAttr
						{
							Gid = 2u,
							Sid = 10306u,
							Val = 0u
						};
						player3.Data.Attrs.Add(storyAttr);
					}
					storyAttr.Val = (storyAttr.Val & 1) | 2;
					rewardSync.Custom[player3.ToPackedAttrKey(2u, 10306u)] = storyAttr.Val;
					rewardSync.Custom[player3.ToShiftedAttrKey(2u, 10306u)] = storyAttr.Val;
					// 第 438 节：活动任务「精进技巧 = 完成任意队员个人故事 1 次」（Type 68）。
					MikuSB.GameServer.Game.Activity.ActivityQuestService.Add(player3, 68, 1, rewardSync);
				}
				ILevelRewardConfig typedConfig = ResolveTypedLevelConfig(levelId2);
				if (typedConfig != null)
				{
					JsonArray typedRows = await player3.RewardManager.GrantLevelRewardsAsync(typedConfig, !wasPassed2, now, rewardSync);
					JsonArray rewardGroup = new JsonArray();
					foreach (JsonNode item2 in typedRows)
					{
						rewardGroup.Add(item2?.DeepClone());
					}
					if (rewardGroup.Count > 0)
					{
						awards.Add(rewardGroup);
					}
				}
				else if (levelNode != null)
				{
					int playerExp = ReadInt(levelNode["PlayerExp"]);
					int roleExp = ReadInt(levelNode["RoleExp"]);
					if (playerExp > 0)
					{
						AddPlayerExp(player3, (uint)playerExp, rewardSync);
					}
					if (roleExp > 0)
					{
						AddRoleExp(player3, (uint)roleExp, rewardSync);
					}
					JArray rewardRows2 = await LevelRewardHelper.GrantAsync(player3, levelNode, rewardSync, wasPassed2);
					JsonArray rewardGroup2 = new JsonArray();
					foreach (JToken rowNode2 in rewardRows2)
					{
						if (!(rowNode2 is JArray row2))
						{
							continue;
						}
						JsonArray converted2 = new JsonArray();
						foreach (JToken value2 in row2)
						{
							converted2.Add((uint)value2);
						}
						rewardGroup2.Add(converted2);
					}
					if (rewardGroup2.Count > 0)
					{
						awards.Add(rewardGroup2);
					}
				}
				context.Sync = rewardSync;
				DatabaseHelper.SaveDatabaseType(player3.Data);
				DatabaseHelper.SaveDatabaseType(player3.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player3.InventoryManager.InventoryData);
			}
			return awards;
		}
		if (string.Equals(sCmd, "Daily_LevelSettlement", StringComparison.Ordinal))
		{
			return await BuildDailySettlement(connection, tbParam, context);
		}
		if (string.Equals(sCmd, "Chapter_NewPrologueSettlement", StringComparison.Ordinal))
		{
			JsonObject result = new JsonObject();
			if (tbParam is JsonObject source && source.TryGetPropertyValue("bWaitServer", out JsonNode bWaitServer))
			{
				result["bWaitServer"] = bWaitServer?.DeepClone();
			}
			result["tbShowAward"] = new JsonArray();
			if (tbParam is JsonObject settle2 && connection.Player != null && uint.TryParse(settle2["nID"]?.ToString(), out var levelId3) && levelId3 != 0)
			{
				connection.Player.SetAttr(22u, levelId3, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
				DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			}
			return result;
		}
		if (string.Equals(sCmd, "BossPvpLogic_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonNode, NtfSyncPlayer) tuple2 = BossPvpService.HandleSettlement(param: NormalizeBossPvpSettlement(tbParam), player: connection.Player);
			JsonNode response = tuple2.Item1;
			NtfSyncPlayer sync = tuple2.Item2;
			context.Sync = sync;
			return response;
		}
		if (string.Equals(sCmd, "BossPvpLogic_LevelFail", StringComparison.Ordinal))
		{
			(JsonNode, NtfSyncPlayer) tuple2 = BossPvpService.HandleFail(connection.Player, tbParam);
			JsonNode response2 = tuple2.Item1;
			NtfSyncPlayer sync2 = tuple2.Item2;
			context.Sync = sync2;
			return response2;
		}
		if (string.Equals(sCmd, "TowerLevel_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonNode, NtfSyncPlayer) tuple2 = TowerLevel_LevelSettlement.HandleSettlement(connection.Player, tbParam);
			JsonNode response3 = tuple2.Item1;
			NtfSyncPlayer sync3 = tuple2.Item2;
			context.Sync = sync3;
			return response3;
		}
		if (string.Equals(sCmd, "TowerEventChapter_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonNode Response, NtfSyncPlayer Sync) tuple3 = await TowerEventChapter_LevelSettlement.HandleSettlementAsync(connection.Player, tbParam);
			JsonNode teResponse = tuple3.Response;
			NtfSyncPlayer teSync = tuple3.Sync;
			context.Sync = teSync;
			return teResponse;
		}
		if (string.Equals(sCmd, "DonoDuelArena_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonObject Response, NtfSyncPlayer Sync) tuple4 = await DonoDuelArenaSettlement.SettleAsync(connection.Player, tbParam);
			JsonObject ddResponse = tuple4.Response;
			NtfSyncPlayer ddSync = tuple4.Sync;
			context.Sync = ddSync;
			return ddResponse;
		}
		if (string.Equals(sCmd, "DonoDuelRogue_RoomSettlement", StringComparison.Ordinal))
		{
			// 第 457 节：探索多诺卡（肉鸽）房间结算。此前落到默认分支把 tbParam 原样回吐，
			// 客户端 s2c 处理里 tbAward 为 nil → 局内背包不加卡、Mgr.tbAwardMoney 不加币，
			// 关卡结算页就出现「获得毛球币 0 / 获得多诺卡 空」。这里按 donoduel_catch_battle
			// 的首次引导奖励发币发卡，并回 tbAward{tbCash,tbHeroCard}。
			NtfSyncPlayer ddrSync = new NtfSyncPlayer();
			JsonObject ddrAward = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelRogueSettlement.SettleRoom(connection.Player, tbParam as JsonObject, ddrSync);
			context.Sync = ddrSync;
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			DatabaseHelper.SaveDatabaseType(connection.Player.InventoryManager.InventoryData);
			// 第 471 节：没有实发奖励时连 tbAward 字段都不回 —— 客户端拿到非 nil 的 tbAward
			// 就一定会 UI.Open("DonoduelDrop")，空壳 tbAward 会弹出「获得奖励：暂无 / 0」。
			// 首个房间 5500（CatchID 1）没有配置引导奖励，这曾是「一进关卡什么都没做就弹奖励」的来源。
			if (ddrAward.Count == 0)
			{
				return new JsonObject();
			}
			return new JsonObject { ["tbAward"] = ddrAward };
		}
		if (string.Equals(sCmd, "DonoDuelRogue_LevelSettlement", StringComparison.Ordinal))
		{
			// 结算页本身从 Mgr.tbAwardMoney（房间累计）与局内背包取数，这里补一个字段齐全的空 tbAward。
			// 第 460 节：同时把「打完整局」记到剧情任务上（108 完成1次探索 / 103 到达第1阶第1区）。
			NtfSyncPlayer ddrLevelSync = new NtfSyncPlayer();
			JsonObject ddrLevelAward = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelRogueSettlement.SettleLevel(connection.Player, tbParam as JsonObject, ddrLevelSync);
			context.Sync = ddrLevelSync;
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			return new JsonObject { ["tbAward"] = ddrLevelAward };
		}
		if (string.Equals(sCmd, "VirCaptureTower_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonNode Response, NtfSyncPlayer Sync) tuple5 = await VirCaptureTower_LevelSettlement.HandleSettlementAsync(connection.Player, tbParam);
			JsonNode response4 = tuple5.Response;
			NtfSyncPlayer sync4 = tuple5.Sync;
			context.Sync = sync4;
			return response4;
		}
		if (string.Equals(sCmd, "VirCaptureTrial_LevelSettlement", StringComparison.Ordinal))
		{
			// 第 352 节：试炼吧结算。原先 sCmd 落到本方法末尾的默认分支（把 tbParam 原样回吐），
			// 结果页「本次测评 / 测评记录」恒为 0。这里显式接一个分支算分并落库最高分。
			NtfSyncPlayer trialSync = new NtfSyncPlayer();
			JsonObject trialPayload = VirCaptureTrialSettle.Settle(connection.Player, tbParam as JsonObject, trialSync);
			context.Sync = trialSync;
			return trialPayload;
		}
		if (string.Equals(sCmd, "WorldBossLogic_LevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "WorldBossLogic_LevelFail", StringComparison.Ordinal))
		{
			// 第 441 节：冬日接力战（WorldBoss，属性组 179）打完后的成绩落库。
			// 原先这两个 sCmd 各自挂着一个 Generic* 空壳处理器，被下面的「通用处理器不接管」判定跳过，
			// 最后落到默认分支把 tbParam 原样回吐 —— nDamageNum 从没写进属性组 179，
			// 界面「已有阵容最高积分」一直为空、永远「暂未上榜」。这里显式接一个分支：
			// 解析 tbParam 里的 nLevelID / nDamageNum，写进进入关卡时分配的挑战块（179/块*10+2）。
			NtfSyncPlayer wbSync = new NtfSyncPlayer();
			uint wbDamage = WorldBossSettlementWriter.Apply(connection.Player, tbParam as JsonObject, wbSync);
			DatabaseHelper.SaveDatabaseType(connection.Player.Data);
			context.Sync = wbSync;
			return new JsonObject
			{
				["tbAward"] = new JsonArray(),
				["nDamageNum"] = (int)wbDamage
			};
		}
		if (string.Equals(sCmd, "DreamCard_LevelSettlement", StringComparison.Ordinal))
		{
			(JsonObject, NtfSyncPlayer) tuple6 = DreamCard_LevelSettlement.HandleSettlement(connection.Player, tbParam);
			JsonObject response5 = tuple6.Item1;
			NtfSyncPlayer sync5 = tuple6.Item2;
			context.Sync = sync5;
			return response5;
		}
		if (string.Equals(sCmd, "Rogue3DScoreModel_ScoreLevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "Rogue3DScoreModel_BuffLevelSettlement", StringComparison.Ordinal))
		{
			// 正常情况下走不到这里：Handle 里已经有这两个 sCmd 的专用分支（见 BattleId/最高分那段）。
			// 保留兜底，避免别的入口带同样的 sCmd 进来时空转。
			return new JsonObject
			{
				["tbAward"] = new JsonArray(),
				["tbMonsterDropRiki"] = new JsonArray(),
				["nNewScore"] = 0,
				["nOldScore"] = 0,
				["tbChallengeMap"] = new JsonObject()
			};
		}
		if (!string.IsNullOrWhiteSpace(sCmd) && CallGSRouter.TryGetHandler(sCmd, out ICallGSHandler sCmdHandler) && !(sCmdHandler is GenericSettlementHandler) && !(sCmdHandler is GenericLevelFailHandler) && !sCmdHandler.GetType().Name.Contains("Compat_", StringComparison.Ordinal) && sCmdHandler.GetType().Name != "Chapter_DealLevelSettlement")
		{
			try
			{
				await sCmdHandler.Handle(connection, tbParam?.ToJsonString() ?? "{}", 0);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Logger.Warn($"dedicated sCmd handler failed: sCmd={sCmd} ex={ex2}");
			}
			return new JsonObject { ["sCmd"] = sCmd };
		}
		if (string.Equals(sCmd, "Rogue_LevelSettlement", StringComparison.Ordinal))
		{
			var (roguePayload, rogueSync) = await RogueSettlement.HandleSettlementAsync(connection.Player, tbParam);
			if (rogueSync != null)
			{
				context.Sync = rogueSync;
			}
			return roguePayload;
		}
		if (string.Equals(sCmd, "BagRogue_LevelSettlement", StringComparison.Ordinal))
		{
			var (bagPayload, bagSync) = await BagRogueSettlement.HandleSettlementAsync(connection.Player, tbParam);
			if (bagSync != null)
			{
				context.Sync = bagSync;
			}
			return bagPayload;
		}
		if (string.Equals(sCmd, "Rogue3D_LevelSettlement", StringComparison.Ordinal) || string.Equals(sCmd, "Rogue3D_SeasonLevelSettlement", StringComparison.Ordinal))
		{
			(JsonObject Payload, NtfSyncPlayer Sync) tuple9 = await Rogue3DProgress.HandleSettlementAsync(connection.Player, tbParam, string.Equals(sCmd, "Rogue3D_SeasonLevelSettlement", StringComparison.Ordinal));
			JsonObject rogue3dPayload = tuple9.Payload;
			NtfSyncPlayer rogue3dSync = tuple9.Sync;
			context.Sync = rogue3dSync;
			return rogue3dPayload;
		}
		if (connection.Player != null && tbParam is JsonObject genericSettle && uint.TryParse(genericSettle["nID"]?.ToString(), out var genericLevelId) && genericLevelId != 0)
		{
			JsonObject genericLevel = LoadLevelNode(genericLevelId);
			ILevelRewardConfig genericConfig = ResolveTypedLevelConfig(genericLevelId);
			if (genericLevel != null || genericConfig != null)
			{
				PlayerInstance player4 = connection.Player;
				NtfSyncPlayer rewardSync2 = new NtfSyncPlayer();
				bool wasPassed3 = player4.IsLevelReallyPassed(genericLevelId);
				uint now2 = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				player4.SetAttr(22u, genericLevelId, now2);
				rewardSync2.Custom[player4.ToPackedAttrKey(22u, genericLevelId)] = now2;
				rewardSync2.Custom[player4.ToShiftedAttrKey(22u, genericLevelId)] = now2;
				int playerExp2 = ReadInt(genericLevel?["PlayerExp"]);
				int roleExp2 = ReadInt(genericLevel?["RoleExp"]);
				if (playerExp2 > 0)
				{
					AddPlayerExp(player4, (uint)playerExp2, rewardSync2);
				}
				if (roleExp2 > 0)
				{
					AddRoleExp(player4, (uint)roleExp2, rewardSync2);
				}
				JsonArray awardGroup = new JsonArray();
				if (genericConfig != null)
				{
					foreach (JsonNode item3 in await player4.RewardManager.GrantLevelRewardsAsync(genericConfig, !wasPassed3, now2, rewardSync2, 0u, grantExperience: false))
					{
						awardGroup.Add(item3?.DeepClone());
					}
				}
				else if (genericLevel != null)
				{
					foreach (JToken rowNode3 in await LevelRewardHelper.GrantAsync(player4, genericLevel, rewardSync2, wasPassed3))
					{
						if (!(rowNode3 is JArray row3))
						{
							continue;
						}
						JsonArray converted3 = new JsonArray();
						foreach (JToken value3 in row3)
						{
							converted3.Add((uint)value3);
						}
						awardGroup.Add(converted3);
					}
				}
				JsonArray genericAwards = new JsonArray();
				if (awardGroup.Count > 0)
				{
					genericAwards.Add(awardGroup);
				}
				context.Sync = rewardSync2;
				DatabaseHelper.SaveDatabaseType(player4.Data);
				DatabaseHelper.SaveDatabaseType(player4.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player4.InventoryManager.InventoryData);
				return new JsonObject { ["tbAward"] = genericAwards };
			}
		}
		return tbParam?.DeepClone() ?? new JsonObject();
	}

	private static async Task<JsonNode> BuildDailySettlement(Connection connection, JsonNode? tbParam, SettlementContext context)
	{
		JsonArray awards = new JsonArray();
		PlayerInstance player = connection.Player;
		JsonObject param = default;
		int num;
		if (player != null)
		{
			param = tbParam as JsonObject;
			num = ((param == null) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		if (num != 0)
		{
			return awards;
		}
		int levelId = ReadInt(param["nID"]);
		if (!GameData.DailyLevelData.TryGetValue((uint)levelId, out DailyLevelExcel dailyConfig))
		{
			return awards;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool wasPassed = player.IsLevelReallyPassed((uint)levelId);
		JsonArray dailyRows = await player.RewardManager.GrantLevelRewardsAsync(dailyConfig, !wasPassed, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync);
		JsonArray dailyGroup = new JsonArray();
		foreach (JsonNode item in dailyRows)
		{
			dailyGroup.Add(item?.DeepClone());
		}
		if (dailyGroup.Count > 0)
		{
			awards.Add(dailyGroup);
		}
		PlayerAttr pass = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 22 && x.Sid == (uint)levelId);
		if (pass == null)
		{
			pass = new PlayerAttr
			{
				Gid = 22u,
				Sid = (uint)levelId,
				Val = 1u
			};
			player.Data.Attrs.Add(pass);
		}
		else
		{
			pass.Val++;
		}
		sync.Custom[player.ToPackedAttrKey(22u, (uint)levelId)] = pass.Val;
		sync.Custom[player.ToShiftedAttrKey(22u, (uint)levelId)] = pass.Val;
		if (dailyConfig.Guarantee != 0)
		{
			PlayerAttr guaranteeAttr = player.Attributes.GetOrCreate(102u, (uint)levelId);
			uint counter = guaranteeAttr.Val & 0xFFFF;
			if (counter < dailyConfig.Guarantee)
			{
				guaranteeAttr.Val = (guaranteeAttr.Val & 0xFFFF0000u) | (counter + 1);
				sync.Custom[player.ToPackedAttrKey(102u, (uint)levelId)] = guaranteeAttr.Val;
				sync.Custom[player.ToShiftedAttrKey(102u, (uint)levelId)] = guaranteeAttr.Val;
			}
		}
		TaskEventService.OnLevelClear(player, (uint)levelId);
		context.Sync = sync;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return awards;
	}

	private static ILevelRewardConfig? ResolveTypedLevelConfig(uint levelId)
	{
		if (GameData.ChapterLevelData.TryGetValue(levelId, out ChapterLevelExcel value))
		{
			return value;
		}
		if (GameData.RoleLevelData.TryGetValue(levelId, out RoleLevelExcel value2))
		{
			return value2;
		}
		if (GameData.TowerEventLevelData.TryGetValue(levelId, out TowerEventLevelExcel value3))
		{
			return value3;
		}
		if (GameData.TowerLevelData.TryGetValue(levelId, out TowerLevelExcel value4))
		{
			return value4;
		}
		if (GameData.DailyLevelData.TryGetValue(levelId, out DailyLevelExcel value5))
		{
			return value5;
		}
		if (GameData.DlcChapterLevelData.TryGetValue(levelId, out DlcChapterLevelExcel value6))
		{
			return value6;
		}
		if (GameData.RepressionLevelData.TryGetValue(levelId, out RepressionLevelExcel value7))
		{
			return value7;
		}
		if (GameData.VirCaptureLevelData.TryGetValue(levelId, out VirCaptureLevelExcel value8))
		{
			return value8;
		}
		if (GameData.ElementLevelData.TryGetValue(levelId, out ElementLevelExcel value9))
		{
			return value9;
		}
		if (GameData.DlcClimbTowerLevelData.TryGetValue(levelId, out DlcClimbTowerLevelExcel value10))
		{
			return value10;
		}
		if (GameData.BagRogueLevelData.TryGetValue(levelId, out BagRogueLevelExcel value11))
		{
			return value11;
		}
		return null;
	}

	private static JsonObject? LoadDailyLevel(uint levelId)
	{
		if (_dailyLevels == null)
		{
			_dailyLevels = new Dictionary<uint, JsonObject>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "daily", "level.json");
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							uint num = (uint)ReadInt(jsonObject["ID"]);
							if (num != 0)
							{
								_dailyLevels[num] = jsonObject;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _dailyLevels.GetValueOrDefault(levelId);
	}

	private static JsonObject? LoadLevelNode(uint levelId)
	{
		if (_levelNodes == null)
		{
			_levelNodes = new Dictionary<uint, JsonObject>();
			try
			{
				string[] array = new string[4] { "chapter/level.json", "dlc/dlc1/chapter/level.json", "dlc/Repression/level.json", "daily/level.json" };
				foreach (string text in array)
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", text.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(path))
					{
						continue;
					}
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray == null)
					{
						continue;
					}
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							uint num = (uint)ReadInt(jsonObject["ID"]);
							if (num != 0 && !_levelNodes.ContainsKey(num))
							{
								_levelNodes[num] = jsonObject;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _levelNodes.GetValueOrDefault(levelId);
	}

	private static void AddRoleExp(PlayerInstance player, uint amount, NtfSyncPlayer sync)
	{
		HashSet<uint> hashSet = new HashSet<uint>();
		foreach (LineupDataInfo value2 in player.LineupManager.LineupData.LineupInfo.Values)
		{
			if (value2.Member1 != 0)
			{
				hashSet.Add(value2.Member1);
			}
			if (value2.Member2 != 0)
			{
				hashSet.Add(value2.Member2);
			}
			if (value2.Member3 != 0)
			{
				hashSet.Add(value2.Member3);
			}
		}
		int num = 0;
		foreach (uint guid in hashSet)
		{
			CharacterInfo characterInfo = player.CharacterManager.CharacterData.Characters.FirstOrDefault((CharacterInfo x) => x.Guid == guid);
			if (characterInfo != null && characterInfo.Level < 80)
			{
				int num2 = characterInfo.Exp + (int)amount;
				UpgradeExpExcel value;
				while (characterInfo.Level < 80 && GameData.UpgradeExpData.TryGetValue((int)characterInfo.Level, out value) && value.CardNeedExp != 0 && num2 >= value.CardNeedExp)
				{
					num2 -= (int)value.CardNeedExp;
					characterInfo.Level++;
					num++;
				}
				characterInfo.Exp = num2;
				sync.Items.Add(characterInfo.ToProto());
			}
		}
		TaskEventService.OnCharacterLevelUp(player, num);
	}

	private static void AddPlayerExp(PlayerInstance player, uint amount, NtfSyncPlayer sync)
	{
		if (player.Data.Level < 60)
		{
			amount = amount * 3 / 2;
		}
		if (_maxExp == null)
		{
			_maxExp = LoadPlayerMaxExp();
		}
		int num = player.Data.Exp + (int)amount;
		int value;
		while (_maxExp.TryGetValue((int)player.Data.Level, out value) && value > 0 && num >= value)
		{
			num -= value;
			player.Data.Level++;
		}
		// 满级（levels.json 里查不到当前等级的 MaxExp）时经验归零，
		// 否则经验会一直累积，客户端每次同步都以为还能升级而弹「账号等级提升」（第 230 节）。
		if (!_maxExp.ContainsKey((int)player.Data.Level))
		{
			num = 0;
		}
		player.Data.Exp = num;
		SevenDayProgress.EnsurePlayerLevelTasks(player);
		sync.Core[0u] = player.Data.Level;
		sync.Core[1u] = (uint)Math.Max(0, player.Data.Exp);
	}

	private static bool IsBossLevel(uint levelId)
	{
		if (_bossLevels == null)
		{
			_bossLevels = new HashSet<uint>();
			string[] array = new string[2] { "challenge/boss/level.json", "challenge/bosspvp/boss.json" };
			foreach (string text in array)
			{
				try
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", text.Replace('/', Path.DirectorySeparatorChar));
					if (!File.Exists(path))
					{
						continue;
					}
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray == null)
					{
						continue;
					}
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject && uint.TryParse(jsonObject["ID"]?.ToString(), out var result))
						{
							_bossLevels.Add(result);
						}
					}
				}
				catch
				{
				}
			}
		}
		return _bossLevels.Contains(levelId);
	}

	private static JsonObject? LoadTowerEventLevelNode(uint levelId)
	{
		if (_towerLevels == null)
		{
			_towerLevels = new Dictionary<uint, JsonObject>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "tower_event";
				buffer[4] = "level.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject && uint.TryParse(jsonObject["ID"]?.ToString(), out var result))
							{
								_towerLevels[result] = jsonObject;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _towerLevels.GetValueOrDefault(levelId);
	}

	private static JsonObject? LoadRoleLevelNode(uint levelId)
	{
		if (_roleLevels == null)
		{
			_roleLevels = new Dictionary<uint, JsonObject>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "role";
				buffer[4] = "level.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject && uint.TryParse(jsonObject["ID"]?.ToString(), out var result))
							{
								_roleLevels[result] = jsonObject;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _roleLevels.GetValueOrDefault(levelId);
	}

	private static Dictionary<int, int> LoadPlayerMaxExp()
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "player", "levels.json");
			if (!File.Exists(path))
			{
				return dictionary;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return dictionary;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					int num = ReadInt(jsonObject["Level"]);
					int num2 = ReadInt(jsonObject["MaxExp"]);
					if (num > 0 && num2 > 0)
					{
						dictionary[num] = num2;
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static Dictionary<uint, JsonObject>? _farmWarLevels;

	// 第 370 节：farmwarout_level.json 的 Rewards 行（[genre, ?, itemId, count, ?]）按首通/重复
	// 通关发放。直接读 Resources 下的 JSON，避免依赖 MikuCommon 里 FarmWarLevelExcel 的字段类型。
	private static async Task<JsonArray> GrantFarmWarLevelRewardsAsync(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		try
		{
			if (_farmWarLevels == null)
			{
				Dictionary<uint, JsonObject> dict = new Dictionary<uint, JsonObject>();
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "farmwar", "farmwarout_level.json");
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject && uint.TryParse(jsonObject["ID"]?.ToString(), out var rid) && rid != 0)
							{
								dict[rid] = jsonObject;
							}
						}
					}
				}
				_farmWarLevels = dict;
			}
			if (!_farmWarLevels.TryGetValue(levelId, out JsonObject level))
			{
				return granted;
			}
			if (level["Rewards"] is JsonArray rewards)
			{
				List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>();
				foreach (JsonNode row in rewards)
				{
					if (row is JsonArray ra)
					{
						List<uint> vals = new List<uint>();
						foreach (JsonNode v in ra)
						{
							vals.Add((uint)Math.Max(0, ReadInt(v)));
						}
						if (vals.Count >= 5)
						{
							rows.Add(vals);
						}
					}
				}
				if (rows.Count > 0)
				{
					JsonArray grantedRows = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
					foreach (JsonNode g in grantedRows)
					{
						granted.Add(g?.DeepClone());
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("farmwar rewards grant failed: " + ex.Message);
		}
		return granted;
	}

	private static int ReadInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<long>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<double>(out var value3))
			{
				return (int)value3;
			}
		}
		return 0;
	}

	private static JsonNode? NormalizeBossPvpSettlement(JsonNode? tbParam)
	{
		if (!(tbParam is JsonObject jsonObject))
		{
			return tbParam;
		}
		JsonObject jsonObject2 = (jsonObject.DeepClone() as JsonObject) ?? jsonObject;
		if (jsonObject2.TryGetPropertyValue("ResidueTime", out JsonNode jsonNode) && jsonNode is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var value))
		{
			jsonObject2["ResidueTime"] = (int)Math.Max(0.0, Math.Round(value, MidpointRounding.AwayFromZero));
		}
		return jsonObject2;
	}
}
