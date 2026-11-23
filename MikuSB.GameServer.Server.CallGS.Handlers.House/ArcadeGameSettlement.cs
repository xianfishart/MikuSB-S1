using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ArcadeGameSettlement")]
public class ArcadeGameSettlement : IHouseFuncHandler
{
	private struct ArcadePropUseSlot
	{
		public int Type { get; set; }

		public int Id { get; set; }

		public ushort Count { get; set; }
	}

	private const uint HouseArcadeInfoStart = 18000u;

	private const uint ArcadeAttrExpOffset = 2u;

	private const uint ArcadeAttrEndlessScoreOffset = 4u;

	private const uint ArcadeAttrGirlEndlessModeStateOffset = 5u;

	private const uint ArcadeAttrGirlNormalModeStateOffset = 10u;

	private const uint ArcadeAttrGirlNormalModeStateEndOffset = 35u;

	private const uint ArcadeAttrConditionValStartOffset = 36u;

	private const uint ArcadeAttrConditionValEndOffset = 55u;

	private const uint ArcadeAttrPropUseStartOffset = 56u;

	private const uint ArcadeAttrPropUseEndOffset = 250u;

	private const int ArcadeGameModeNormal = 1;

	private const int ArcadeGameModeEndless = 2;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int modeType = HouseJson.NumField(root, "nModeType");
		int finishRound = Math.Max(0, HouseJson.NumField(root, "nFinishRound"));
		int maxScore = Math.Max(0, HouseJson.NumField(root, "nMaxScore"));
		JsonNode jsonNode = root["tbConditionVal"];
		if (jsonNode is JsonArray conditions)
		{
			foreach (JsonNode conditionNode in conditions)
			{
				JsonArray condition = conditionNode as JsonArray;
				if (condition == null || condition.Count < 2)
				{
					continue;
				}
				int id = HouseJson.ToInt(condition[0]);
				int value = Math.Max(0, HouseJson.ToInt(condition[1]));
				if (id < 0)
				{
					continue;
				}
				uint sid = (uint)(18036 + id);
				if (sid <= 18055)
				{
					uint prev = HouseAttr.Read(player, sid);
					if ((uint)value > prev)
					{
						await HouseAttr.SetAsync(connection, sid, (uint)value, sync);
					}
				}
			}
		}
		jsonNode = root["tbPropUse"];
		if (jsonNode is JsonArray propUses)
		{
			uint slotStart = 18056u;
			uint slotEnd = 18250u;
			Dictionary<uint, ArcadePropUseSlot> slotState = new Dictionary<uint, ArcadePropUseSlot>();
			Queue<uint> emptySlots = new Queue<uint>();
			uint sid2 = slotStart;
			ArcadePropUseSlot value3;
			uint key2;
			while (sid2 <= slotEnd)
			{
				uint value2 = HouseAttr.Read(player, sid2);
				if (value2 == 0)
				{
					emptySlots.Enqueue(sid2);
				}
				else
				{
					uint key = sid2;
					value3 = new ArcadePropUseSlot
					{
						Type = (byte)(value2 & 0xFF),
						Id = (byte)((value2 >> 8) & 0xFF),
						Count = (ushort)((value2 >> 16) & 0xFFFF)
					};
					slotState[key] = value3;
				}
				key2 = sid2++;
			}
			foreach (JsonNode propUseNode in propUses)
			{
				JsonArray propUse = propUseNode as JsonArray;
				if (propUse == null || propUse.Count < 3)
				{
					continue;
				}
				int type = HouseJson.ToInt(propUse[0]) & 0xFF;
				int id2 = HouseJson.ToInt(propUse[1]) & 0xFF;
				int count = HouseJson.ToInt(propUse[2]);
				if (count <= 0)
				{
					continue;
				}
				uint? foundSid = null;
				foreach (KeyValuePair<uint, ArcadePropUseSlot> item in slotState)
				{
					item.Deconstruct(out key2, out value3);
					uint sid3 = key2;
					ArcadePropUseSlot slot = value3;
					if (slot.Type == type && slot.Id == id2)
					{
						foundSid = sid3;
						break;
					}
				}
				if (foundSid.HasValue)
				{
					ArcadePropUseSlot slot2 = slotState[foundSid.Value];
					slot2.Count = (ushort)Math.Min(65535, slot2.Count + count);
					slotState[foundSid.Value] = slot2;
					await HouseAttr.SetAsync(connection, foundSid.Value, HouseAttr.PackArcadePropUse(slot2.Type, slot2.Id, slot2.Count), sync);
				}
				else if (emptySlots.Count > 0)
				{
					uint sid4 = emptySlots.Dequeue();
					ushort packedCount = (ushort)Math.Min(65535, count);
					value3 = new ArcadePropUseSlot
					{
						Type = type,
						Id = id2,
						Count = packedCount
					};
					slotState[sid4] = value3;
					await HouseAttr.SetAsync(connection, sid4, HouseAttr.PackArcadePropUse(type, id2, packedCount), sync);
				}
			}
		}
		JsonArray girlList = root["tbGirlDataList"] as JsonArray;
		int nAddExp = 0;
		switch (modeType)
		{
		case 1:
			if (girlList != null)
			{
				foreach (JsonNode girlNode2 in girlList)
				{
					int girlId2 = HouseJson.ToInt(girlNode2);
					if (girlId2 > 0)
					{
						uint sid5 = (uint)(18010 + girlId2);
						if (sid5 <= 18035)
						{
							uint prev2 = HouseAttr.Read(player, sid5);
							uint total = prev2 + (uint)finishRound;
							await HouseAttr.SetAsync(connection, sid5, total, sync);
							// 2026-11-13 追加（修复记录第 270 节）：普通模式累计回合达标 → 该少女的无尽模式解锁。
							// 客户端 GetEndlessUnlocked 只读 101/18005 的少女位，旧实现在无尽结算里才置位，
							// 形成「进不去无尽 → 永远没有位」的死锁。官方口径是「和她完成普通模式 5 回合
							// 才能在无尽模式里相遇」，所以这里按 ChallengeUnlock（默认 5）置位。
							if (total >= (uint)ArcadeGameHelper.ChallengeUnlockFor(girlId2) && !ArcadeGameHelper.IsEndlessUnlocked(player, girlId2))
							{
								uint bitsSid2 = ArcadeGameHelper.GirlEndlessStateSid;
								uint bitsVal = HouseAttr.Read(player, bitsSid2);
								uint newBits = bitsVal;
								if (girlId2 < 31)
								{
									newBits |= (uint)(1 << girlId2);
								}
								if (newBits != bitsVal)
								{
									await HouseAttr.SetAsync(connection, bitsSid2, newBits, sync);
								}
							}
						}
					}
				}
			}
			nAddExp = finishRound * 10;
			break;
		case 2:
		{
			uint scoreSid = 18004u;
			uint prevScore = HouseAttr.Read(player, scoreSid);
			if ((uint)maxScore > prevScore)
			{
				await HouseAttr.SetAsync(connection, scoreSid, (uint)maxScore, sync);
			}
			uint bitsSid = 18005u;
			uint bits = HouseAttr.Read(player, bitsSid);
			uint originalBits = bits;
			if (girlList != null)
			{
				foreach (JsonNode girlNode in girlList)
				{
					int girlId = HouseJson.ToInt(girlNode);
					if (girlId > 0 && girlId < 31)
					{
						bits |= (uint)(1 << girlId);
					}
				}
			}
			if (bits != originalBits)
			{
				await HouseAttr.SetAsync(connection, bitsSid, bits, sync);
			}
			nAddExp = maxScore / 10;
			break;
		}
		}
		if (nAddExp > 0)
		{
			uint expSid = 18002u;
			uint prevExp = HouseAttr.Read(player, expSid);
			await HouseAttr.SetAsync(connection, expSid, prevExp + (uint)nAddExp, sync);
		}
		// 2026-11-13 追加（修复记录第 269 节）：把本局上报换算成 24001~24020 的成就进度，
		// 写进 Gid 7 / Val 的 bit1..31（客户端 Achievement.GetProgres 读的就是它）。
		// 之前这里完全没接，进度恒 0，成就页全灰、一键领取也领不到。
		ArcadeGameAchievement.Apply(connection, root, sync);
		JsonObject rsp = new JsonObject
		{
			["nAddExp"] = nAddExp,
			["FuncName"] = "ArcadeGameSettlement"
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
