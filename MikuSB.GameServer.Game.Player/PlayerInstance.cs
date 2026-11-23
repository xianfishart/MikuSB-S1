using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Google.Protobuf;
using MikuSB.Configuration;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Lineup;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Game.Character;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Lineup;
using MikuSB.GameServer.Game.Reward;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS.Handlers.Scene;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Player;

public class PlayerInstance(PlayerGameData data)
{
	public static readonly List<PlayerInstance> _playerInstances = new List<PlayerInstance>();

	public const uint LegacyUnlockPassTime = 1700000000u;

	public Connection? Connection { get; set; }

	public int Uid { get; set; }

	public bool Initialized { get; set; }

	public bool IsNewPlayer { get; set; }

	public PlayerGameData Data { get; set; } = data;

	public PlayerAttributes Attributes { get; } = new PlayerAttributes(data);

	public CharacterManager CharacterManager { get; set; } = null;

	public InventoryManager InventoryManager { get; set; } = null;

	public LineupManager LineupManager { get; set; } = null;

	public RewardManager RewardManager { get; set; } = null;

	public MikuSB.GameServer.Game.Quest.QuestManager QuestManager { get; set; } = null;

	public PlayerInstance(int uid)
		: this(new PlayerGameData
		{
			Uid = uid
		})
	{
		IsNewPlayer = true;
		Data.Name = PlayerGameData.NormalizeDisplayName(AccountData.GetAccountByUid(uid)?.Username);
		DatabaseHelper.CreateInstance(Data);
		Task task = Task.Run(async () =>
		{
			await InitialPlayerManager();
			if (ConfigManager.Config.ServerOption.VanillaStart)
			{
				Data.Level = 1u;
				Data.Exp = 0;
				Data.Vigor = 240u;
				(uint Detail, uint Particular)[] starterGdpl = new (uint, uint)[3]
				{
					(1u, 1u),
					(2u, 1u),
					(8u, 1u)
				};
				(uint Detail, uint Particular)[] array = starterGdpl;
				for (int i = 0; i < array.Length; i++)
				{
					(uint, uint) tuple = array[i];
					uint detail = tuple.Item1;
					uint particular = tuple.Item2;
					CardExcel starter = GameData.CardData.Values.FirstOrDefault((CardExcel x) => x.Genre == 1 && x.Detail == detail && x.Particular == particular);
					if (starter != null)
					{
						CharacterInfo character = await CharacterManager.AddCharacter(ItemTypeEnum.TYPE_CARD, detail, particular, 1u, 1u, sendPacket: false);
						if (character != null)
						{
							character.Level = 1u;
						}
					}
				}
				foreach (CardSkinExcel skin in GameData.CardSkinData.Values)
				{
					await InventoryManager.AddSkinItem(ItemTypeEnum.TYPE_CARD_SKIN, skin.Detail, skin.Particular, skin.Level, sendPacket: false);
				}
				foreach (CardSkinPartsExcel part in GameData.CardSkinPartsData.Values)
				{
					await InventoryManager.AddSkinPartItem(ItemTypeEnum.TYPE_CARD_SKIN_PART, part.Detail, part.Particular, part.Level, sendPacket: false);
				}
				foreach (WeaponSkinExcel weaponSkin in GameData.WeaponSkinData.Values)
				{
					await InventoryManager.AddWeaponSkinItem(ItemTypeEnum.TYPE_WEAPON_SKIN, weaponSkin.Detail, weaponSkin.Particular, weaponSkin.Level, sendPacket: false);
				}
				foreach (ManifestationExcel manifestation in GameData.ManifestationData.Values)
				{
					await InventoryManager.AddManifestationItem(ItemTypeEnum.TYPE_MANIFESTATION, manifestation.Detail, manifestation.Particular, manifestation.Level, sendPacket: false);
				}
				await ProfileUnlock.Ensure(this, sendPacket: false);
				DatabaseHelper.SaveDatabaseType(CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(InventoryManager.InventoryData);
				List<uint> selected = CharacterManager.CharacterData.Characters.Select((CharacterInfo x) => x.Guid).Take(3).ToList();
				if (selected.Count >= 3)
				{
					await LineupManager.UpdateLineup(1, selected[0], selected[1], selected[2], sendPacket: false);
				}
			}
			else
			{
				foreach (CardSkinExcel skinCard in GameData.CardSkinData.Values)
				{
					await InventoryManager.AddSkinItem((ItemTypeEnum)skinCard.Genre, skinCard.Detail, skinCard.Particular, skinCard.Level, sendPacket: false);
				}
				foreach (ArItemExcel ar in GameData.ArItemData.Values)
				{
					await InventoryManager.AddArItem((ItemTypeEnum)ar.Genre, ar.Detail, ar.Particular, ar.Level, sendPacket: false);
				}
				foreach (ManifestationExcel manifest in GameData.ManifestationData.Values)
				{
					await InventoryManager.AddManifestationItem((ItemTypeEnum)manifest.Genre, manifest.Detail, manifest.Particular, manifest.Level, sendPacket: false);
				}
				foreach (CardExcel card in GameData.CardData.Values)
				{
					await CharacterManager.AddCharacter((ItemTypeEnum)card.Genre, card.Detail, card.Particular, card.Level, 1u, sendPacket: false);
				}
				await ProfileUnlock.Ensure(this, sendPacket: false);
				List<uint> selected2 = (from x in CharacterManager.CharacterData.Characters.OrderBy((CharacterInfo _) => Guid.NewGuid()).Take(3)
					select x.Guid).ToList();
				if (selected2.Count >= 3)
				{
					await LineupManager.UpdateLineup(1, selected2[0], selected2[1], selected2[2], sendPacket: false);
				}
			}
		});
		task.Wait();
		Initialized = true;
	}

	private async ValueTask InitialPlayerManager()
	{
		Uid = Data.Uid;
		Data.LastActiveTime = Extensions.GetUnixSec();
		InventoryManager = new InventoryManager(this);
		LineupManager = new LineupManager(this);
		CharacterManager = new CharacterManager(this);
		QuestManager = new MikuSB.GameServer.Game.Quest.QuestManager(this);
		RewardManager = new RewardManager(this);
		await Task.CompletedTask;
	}

	public T InitializeDatabase<T>() where T : BaseDatabaseDataHelper, new()
	{
		return DatabaseHelper.GetInstanceOrCreateNew<T>(Uid);
	}

	public async ValueTask OnEnterGame()
	{
		if (!Initialized)
		{
			await InitialPlayerManager();
		}
		Data.EnsureDisplayName();
		await CharacterManager.RepairCharacterWeapons();
		ItemSanitizer.Cleanup(this);
		// 固定大厅场景（尘白之间 / 世界树营地）在登录时把 139/401~414（14 格）全部对齐到 132/1，
		// 否则重登会被候选池随机漂移成别的 3D 情景。（第 140 节；第 350 节把 13 格改成 14 格）
		if (ChangeMainScene.AlignFixedMainScene(this))
		{
			DatabaseHelper.SaveDatabaseType(Data);
		}
		await ProfileUnlock.Ensure(this, sendPacket: true);
	}

	public IEnumerable<BaseGameItemInfo> GetSupplyItems()
	{
		return InventoryManager.InventoryData.Items.Values.Where((BaseGameItemInfo x) => (x.TemplateId & 0xFFFF) == 5);
	}

	public async ValueTask OnLogin()
	{
		_playerInstances.Add(this);
		await Task.CompletedTask;
	}

	public static PlayerInstance? GetPlayerInstanceByUid(long uid)
	{
		return _playerInstances.FirstOrDefault((PlayerInstance player) => player.Uid == uid);
	}

	public void OnLogoutAsync()
	{
		_playerInstances.Remove(this);
	}

	public async ValueTask SendPacket(BasePacket packet)
	{
		if (Connection?.IsOnline ?? false)
		{
			await Connection.SendPacket(packet, 0);
		}
	}

	public async ValueTask SendPacket(int cmdId, IMessage msg)
	{
		if (Connection?.IsOnline ?? false)
		{
			await Connection.SendPacket(cmdId, msg, 0);
		}
	}

	public async ValueTask OnHeartBeat()
	{
		DatabaseHelper.ToSaveUidList.SafeAdd(Uid);
		await Task.CompletedTask;
	}

	public async ValueTask ReceiveMessage(uint sendUid, uint recvUid, string? message = null, uint? emojiId = null)
	{
		ChatMsg data = new ChatMsg
		{
			Type = ChatType.Friend,
			Sender = sendUid,
			Recver = recvUid,
			Emoji = emojiId.GetValueOrDefault(),
			Text = ChatMessageHelper.NormalizeForClient(message),
			Profile = Data.ToProfileProto(),
			TimeStamp = ChatMessageHelper.BuildClientTimestamp()
		};
		await SendPacket(1042, data);
		if (recvUid == ConfigManager.Config.ServerOption.ServerProfile.Uid && message != null)
		{
			if (message.StartsWith("/"))
			{
				message = message.Substring(1).Trim();
			}
			CommandExecutor.ExecuteCommand(new PlayerCommandSender(this), message);
		}
	}

	public PlayerProfile ToServerFriendProto()
	{
		ServerProfile serverProfile = ConfigManager.Config.ServerOption.ServerProfile;
		return new PlayerProfile
		{
			Pid = (uint)serverProfile.Uid,
			Account = serverProfile.Name,
			Name = serverProfile.Name,
			Sex = serverProfile.Gender,
			Level = (uint)serverProfile.Level,
			Sign = serverProfile.Signature
		};
	}

	public MikuSB.Proto.Player ToPlayerProto(bool includeSupportCards = true)
	{
		BuildPlayerAttr();
		NormalizeGirlFavor();
		EnsurePlayerChargeDefaults();
		// 怪物卡要带 Affixs（攻/防/血档位）下发，详情面板才不是空白
		InventoryManager.EnsureMonsterCardInfos();
		// 开拓目标：客户端读 (133, MissionID) 属性能否领取（第 343 节）。
		// 登录时先记一次登录天数、用现有伙伴数把"累计劝降"补个下限，再全量刷新进度。
		VirCaptureMissionProgress.MarkLoginDay(this);
		VirCaptureMissionProgress.SeedFromInventory(this);
		VirCaptureMissionProgress.SyncAll(this, null);
		string text = PlayerGameData.NormalizeDisplayName(Data.Name);
		MikuSB.Proto.Player player = new MikuSB.Proto.Player
		{
			Pid = (ulong)Data.Uid,
			Account = text,
			Provider = text,
			Channel = "gm",
			Subchannel = "gm",
			Name = text,
			Level = Data.Level,
			Exp = (uint)Math.Max(0, Data.Exp),
			Sex = Data.Gender,
			Vigor = Data.Vigor,
			Charged = GetAttrValue(0u, 18u),
			Solutions = { LineupManager.LineupData.LineupInfo.Values.Select((LineupDataInfo x) => x.ToProto()) },
			Badges = { InventoryManager.InventoryData.Items.Values.Where((BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_BADGE).Select((Func<BaseGameItemInfo, ulong>)((BaseGameItemInfo x) => x.UniqueId)) }
		};
		foreach (CharacterInfo character in CharacterManager.CharacterData.Characters)
		{
			player.Items.Add(character.ToProto());
		}
		foreach (BaseGameItemInfo value2 in InventoryManager.InventoryData.Items.Values)
		{
			player.Items.Add(value2.ToProto());
		}
		foreach (GameSkinInfo value3 in InventoryManager.InventoryData.Skins.Values)
		{
			player.Items.Add(value3.ToProto());
		}
		foreach (GameWeaponInfo value4 in InventoryManager.InventoryData.Weapons.Values)
		{
			player.Items.Add(value4.ToProto());
		}
		if (includeSupportCards)
		{
			foreach (GameSupportCardInfo value5 in InventoryManager.InventoryData.SupportCards.Values)
			{
				player.Items.Add(value5.ToProto());
			}
		}
		foreach (PlayerAttr attr in Data.Attrs)
		{
			uint gid = attr.Gid;
			uint sid = attr.Sid;
			uint val = attr.Val;
			if ((gid == 0 || val != 0) && (gid != 139 || sid < 600 || sid >= 900))
			{
				if (gid == 0)
				{
					player.Attrs[sid] = val;
					continue;
				}
				// 登录包只发 shifted 键：packed 与 shifted 双写会让 RspLogin 膨胀到 314KB，
				// 顶穿客户端解压缓冲（zlib -5）导致白屏。客户端按 shifted 取值。（第 178 节）
				player.Attrs[ToShiftedAttrKey(gid, sid)] = val;
			}
		}
		ShopLogic_GetGoodsList.EnsureShopMetadata(this);
		foreach (PlayerStrAttr strAttr in Data.StrAttrs)
		{
			if (strAttr.Gid == 1)
			{
				try
				{
					if (JsonNode.Parse(strAttr.Val) is JsonObject jsonObject)
					{
						jsonObject["tbgoods"] = new JsonArray();
						player.StrAttrs[ToShiftedAttrKey(strAttr.Gid, strAttr.Sid)] = jsonObject.ToJsonString();
						continue;
					}
				}
				catch
				{
				}
			}
			player.StrAttrs[ToShiftedAttrKey(strAttr.Gid, strAttr.Sid)] = strAttr.Val;
		}
		foreach (var (key, value) in BuildMoneySync())
		{
			player.Money[key] = value;
		}
		player.ShowItems.AddRange(Data.ShowItems);
		player.ShowAttrs.AddRange(BuildShowAttrs());
		foreach (Mail item in MailService.Load(this))
		{
			player.MailBox[item.Mid] = new PlayerMail
			{
				Mid = item.Mid,
				Stat = item.Stat,
				Time = item.Time,
				Expiration = item.Expiration
			};
		}
		return player;
	}

	public void SetDisplayName(string? name)
	{
		Data.Name = PlayerGameData.NormalizeDisplayName(name);
	}

	public void SetShowItem(int index, ulong itemId)
	{
		if (index > 0)
		{
			while (Data.ShowItems.Count < index)
			{
				Data.ShowItems.Add(0uL);
			}
			Data.ShowItems[index - 1] = itemId;
		}
	}

	public void SetStrAttr(uint gid, uint sid, string value)
	{
		PlayerStrAttr playerStrAttr = Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerStrAttr == null)
		{
			playerStrAttr = new PlayerStrAttr
			{
				Gid = gid,
				Sid = sid
			};
			Data.StrAttrs.Add(playerStrAttr);
		}
		playerStrAttr.Val = value;
	}

	public uint ToPackedAttrKey(uint gid, uint sid)
	{
		if (gid == 0)
		{
			return sid;
		}
		return gid * 10000 + sid;
	}

	public uint ToShiftedAttrKey(uint gid, uint sid)
	{
		if (gid == 0)
		{
			return sid;
		}
		return (gid << 16) | sid;
	}

	public Dictionary<string, int> BuildMoneySync()
	{
		int value = (int)Math.Min(2147483647u, GetAttrValue(1u, 3u));
		return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["."] = value,
			["jinshan.jinshan"] = value,
			["gm-gm"] = value,
			["gm-gm.gm-gm"] = value,
			["official"] = value,
			["official.official"] = value,
			["pc_jinshan"] = value,
			["pc_jinshan.pc_jinshan"] = value,
			["gm.gm"] = value,
			["gm."] = value,
			["gm"] = value,
			["gm-gm."] = value,
			["jinshan."] = value,
			["pc_jinshan."] = value,
			["official."] = value
		};
	}

	public IReadOnlyList<PlayerLevelExcel> AddPlayerExperience(uint amount, NtfSyncPlayer sync)
	{
		if (amount == 0)
		{
			return Array.Empty<PlayerLevelExcel>();
		}
		if (Data.Level == 0)
		{
			Data.Level = 1u;
		}
		List<PlayerLevelExcel> list = new List<PlayerLevelExcel>();
		ulong num = (ulong)(Math.Max(0, Data.Exp) + amount);
		PlayerLevelExcel value;
		while (GameData.PlayerLevelData.TryGetValue(Data.Level, out value) && value.MaxExp != 0 && num >= value.MaxExp && GameData.PlayerLevelData.ContainsKey(Data.Level + 1))
		{
			num -= value.MaxExp;
			Data.Level++;
			if (GameData.PlayerLevelData.TryGetValue(Data.Level, out PlayerLevelExcel value2))
			{
				list.Add(value2);
			}
		}
		// 满级后不再累积经验：levels.json 里 100 级的 MaxExp 是 "-"（解析成 0），
		// 客户端拿到「等级 100 + 经验 1160」会把残留经验当成还能升一级，
		// 每次同步都触发 LevelUp → 反复弹「账号等级提升」（第 230 节）。
		if (!GameData.PlayerLevelData.TryGetValue(Data.Level, out PlayerLevelExcel currentLevel) || currentLevel.MaxExp == 0)
		{
			num = 0;
		}
		Data.Exp = (int)Math.Min(2147483647uL, num);
		sync.Core[0u] = Data.Level;
		sync.Core[1u] = (uint)Data.Exp;
		return list;
	}

	public void AddCurrency(uint moneyType, uint amount, NtfSyncPlayer sync, bool allowVigorOverflow = false)
	{
		if (amount == 0)
		{
			return;
		}
		if (moneyType == 4)
		{
			// 体力加值按 levels.json 的 MaxVigor 封顶（第 180 节）
			Data.Vigor = VigorRecovery.AddCapped(this, amount);
			sync.Core[2u] = Data.Vigor;
			sync.Core[4u] = Attributes.GetValue(9900u, 1u);
			return;
		}
		uint sid = AttrIds.Currency.GetSid(moneyType);
		PlayerAttr attr = Attributes.Add(1u, sid, amount);
		Attributes.SyncTo(sync, attr);
		if (moneyType != 1)
		{
			return;
		}
		foreach (var (key, value) in BuildMoneySync())
		{
			sync.Money[key] = value;
		}
	}

	private uint GetAttrValue(uint gid, uint sid)
	{
		return Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid)?.Val ?? 0;
	}

	public uint GetAttr(uint gid, uint sid)
	{
		return GetAttrValue(gid, sid);
	}

	public bool IsLevelReallyPassed(uint levelId)
	{
		uint attrValue = GetAttrValue(22u, levelId);
		return attrValue != 0 && attrValue != 1700000000;
	}

	public void SetAttr(uint gid, uint sid, uint value)
	{
		PlayerAttr playerAttr = Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			Data.Attrs.Add(new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			});
		}
		else
		{
			playerAttr.Val = value;
		}
	}

	public uint[] BuildShowAttrs()
	{
		return new uint[25]
		{
			(uint)CharacterManager.CharacterData.Characters.Select((CharacterInfo x) => x.TemplateId).Distinct().Count(),
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			0u,
			(uint)InventoryManager.InventoryData.Skins.Count,
			(uint)InventoryManager.InventoryData.Weapons.Count,
			(uint)Enumerable.Count(InventoryManager.InventoryData.Items.Values, (BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_HOUSE),
			GetAttrValue(200u, 1u),
			GetAttrValue(200u, 2u),
			GetAttrValue(200u, 3u),
			1u
		};
	}

	private void NormalizeGirlFavor()
	{
		bool flag = false;
		foreach (PlayerAttr attr in Data.Attrs)
		{
			if (attr.Gid == 101 && attr.Sid % 50 == 0)
			{
				uint num = attr.Val & 0x7FF;
				if (num > 50)
				{
					attr.Val = (attr.Val & 0xFFFFF800u) | 0x32;
					flag = true;
				}
			}
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(Data);
		}
	}

	private void EnsurePlayerChargeDefaults()
	{
		bool flag = false;
		uint attrValue = GetAttrValue(0u, 19u);
		if ((attrValue & 0x80000000u) == 0)
		{
			SetAttr(0u, 19u, attrValue | 0x80000000u);
			flag = true;
		}
		if (GetAttrValue(0u, 20u) == 0)
		{
			SetAttr(0u, 20u, 1u);
			flag = true;
		}
		if (flag)
		{
			DatabaseHelper.SaveDatabaseType(Data);
		}
	}

	public void BuildPlayerAttr(bool additional = false)
	{
		List<(uint, uint, uint)> list = (ConfigManager.Config.ServerOption.VanillaStart ? BuildVanillaBootstrapAttrs().ToList() : BuildLobbyBootstrapAttrs().ToList());
		if (additional)
		{
			list.AddRange(BuildGirlFurnitureAttrs());
		}
		Dictionary<(uint, uint), PlayerAttr> dictionary = new Dictionary<(uint, uint), PlayerAttr>();
		foreach (PlayerAttr attr in Data.Attrs)
		{
			dictionary[(attr.Gid, attr.Sid)] = attr;
		}
		if (dictionary.Count != Data.Attrs.Count)
		{
			Data.Attrs = dictionary.Values.ToList();
			DatabaseHelper.SaveDatabaseType(Data);
		}
		HashSet<(uint, uint)> hashSet = new HashSet<(uint, uint)>();
		foreach (var (num, num2, num3) in list)
		{
			if (!hashSet.Add((num, num2)))
			{
				continue;
			}
			if (dictionary.TryGetValue((num, num2), out var value))
			{
				if ((num != 1 || (num3 != 999999999 && !ConfigManager.Config.ServerOption.VanillaStart)) && value.Val < num3)
				{
					value.Val = num3;
				}
				continue;
			}
			PlayerAttr playerAttr = new PlayerAttr
			{
				Gid = num,
				Sid = num2,
				Val = num3
			};
			Data.Attrs.Add(playerAttr);
			dictionary[(num, num2)] = playerAttr;
		}
	}

	private static IEnumerable<(uint Gid, uint Sid, uint Value)> BuildGirlFurnitureAttrs()
	{
		Dictionary<uint, uint> groupFurnitureByArea = new Dictionary<uint, uint>();
		foreach (HouseFurniturePosExcel pos in GameData.HouseFurniturePosData.Values)
		{
			uint areaId = pos.AreaId;
			uint groupId = pos.GroupId;
			uint selectedIndex = 1u;
			uint shift = (groupId - 1) * 3;
			if (!groupFurnitureByArea.TryGetValue(areaId, out var packed))
			{
				packed = 0u;
			}
			packed = (groupFurnitureByArea[areaId] = packed | (selectedIndex << (int)shift));
		}
		for (uint girlId = 0u; girlId <= 50; girlId++)
		{
			uint baseSid = girlId * 50;
			for (uint offset = 10u; offset <= 19; offset++)
			{
				yield return (Gid: 101u, Sid: baseSid + offset, Value: 153391689u);
			}
			if (groupFurnitureByArea.TryGetValue(girlId, out var groupValue))
			{
				yield return (Gid: 101u, Sid: baseSid + 20, Value: groupValue);
			}
		}
		for (uint sid = 10010u; sid <= 10019; sid++)
		{
			yield return (Gid: 101u, Sid: sid, Value: 153391689u);
		}
		yield return (Gid: 101u, Sid: 10020u, Value: 1u);
		for (uint sid2 = 15001u; sid2 <= 15010; sid2++)
		{
			yield return (Gid: 101u, Sid: sid2, Value: 153391689u);
		}
		for (uint sid3 = 17101u; sid3 <= 17110; sid3++)
		{
			yield return (Gid: 101u, Sid: sid3, Value: 153391689u);
		}
		for (uint sid4 = 30000u; sid4 < 31000; sid4++)
		{
			yield return (Gid: 101u, Sid: sid4, Value: 153391689u);
		}
	}

	private static IEnumerable<(uint Gid, uint Sid, uint Value)> BuildVanillaBootstrapAttrs()
	{
		yield return (Gid: 11u, Sid: 1u, Value: 1u);
		yield return (Gid: 57u, Sid: 0u, Value: 1u);
		yield return (Gid: 99u, Sid: 3u, Value: 30u);
		yield return (Gid: 110u, Sid: 1u, Value: 1u);
		yield return (Gid: 178u, Sid: 1u, Value: 1700000000u);
		yield return (Gid: 187u, Sid: 1u, Value: 1u);
		yield return (Gid: 1u, Sid: 3u, Value: 10000u);
		yield return (Gid: 1u, Sid: 5u, Value: 0u);
		yield return (Gid: 1u, Sid: 27u, Value: 5000u);
		yield return (Gid: 132u, Sid: 1u, Value: 0u);
	}

	private static IEnumerable<(uint Gid, uint Sid, uint Value)> BuildLobbyBootstrapAttrs()
	{
		yield return (Gid: 4u, Sid: 0u, Value: 5u);
		yield return (Gid: 11u, Sid: 1u, Value: 1u);
		yield return (Gid: 57u, Sid: 0u, Value: 1u);
		yield return (Gid: 99u, Sid: 3u, Value: 30u);
		yield return (Gid: 110u, Sid: 1u, Value: 1u);
		yield return (Gid: 178u, Sid: 1u, Value: 1700000000u);
		yield return (Gid: 187u, Sid: 1u, Value: 2u);
		yield return (Gid: 1u, Sid: 27u, Value: 5000u);
		for (uint guideId = 1u; guideId <= 150; guideId++)
		{
			yield return (Gid: 4u, Sid: guideId, Value: 999u);
		}
		for (uint guideId2 = 10000u; guideId2 <= 10300; guideId2++)
		{
			yield return (Gid: 4u, Sid: guideId2, Value: 999u);
		}
		for (uint guideId3 = 11000u; guideId3 <= 11300; guideId3++)
		{
			yield return (Gid: 4u, Sid: guideId3, Value: 999u);
		}
		for (uint guideId4 = 12000u; guideId4 <= 12100; guideId4++)
		{
			yield return (Gid: 4u, Sid: guideId4, Value: 999u);
		}
		for (uint guideId5 = 22000u; guideId5 <= 22100; guideId5++)
		{
			yield return (Gid: 4u, Sid: guideId5, Value: 999u);
		}
		uint[] array = new uint[10] { 10031u, 10041u, 10061u, 10081u, 10101u, 10224u, 11006u, 11202u, 11210u, 22002u };
		foreach (uint guideId6 in array)
		{
			yield return (Gid: 4u, Sid: guideId6, Value: 999u);
		}
		foreach (uint levelId in GameData.ChapterLevelData.Keys)
		{
			yield return (Gid: 21u, Sid: levelId, Value: 7u);
			yield return (Gid: 22u, Sid: levelId, Value: 1700000000u);
		}
		foreach (uint levelId2 in GameData.DailyLevelData.Keys)
		{
			yield return (Gid: 21u, Sid: levelId2, Value: 7u);
			yield return (Gid: 22u, Sid: levelId2, Value: 1700000000u);
		}
		foreach (uint levelId3 in GameData.RoleLevelData.Keys)
		{
			yield return (Gid: 21u, Sid: levelId3, Value: 7u);
			yield return (Gid: 22u, Sid: levelId3, Value: 1700000000u);
		}
		foreach (GuideExcel guide in GameData.GuideData.Values)
		{
			// 第 462 节：多诺（DLC35）引导 64100~64199 不能预置 999（=已跳过）。
			// 客户端 GuideLogic.CanOpenGuide 见到 999 直接返回 false，教程不播；
			// 同时 donoduel_config 的 ConfigID 21 靠「64107 是否完成」判定首次进入肉鸽，
			// 被写成 999 后首次奖励与退出房间都不再触发。
			if (guide.ID >= 64100u && guide.ID < 64200u)
			{
				continue;
			}
			yield return (Gid: 4u, Sid: guide.ID, Value: 999u);
		}
		for (uint favor = 0u; favor <= 50; favor++)
		{
			yield return (Gid: 101u, Sid: favor * 50, Value: 50u);
		}
		yield return (Gid: 132u, Sid: 1u, Value: 0u);
	}
}
