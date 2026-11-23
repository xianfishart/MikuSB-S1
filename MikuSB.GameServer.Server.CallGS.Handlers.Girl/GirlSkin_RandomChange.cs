using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

/// <summary>
/// 第 519 节（2026-11-22）：「队员时装随机」（作战准备界面）的随机换肤接口。
/// 客户端 Fashion.RandomChangeSkinReq（Fashion.lua:337-406）：编队含试用卡时，
/// 点「开始作战」会先收集试用卡、随机挑一件时装，发
///   me:CallGS("GirlSkin_RandomChange", {"tbCardId":[GUID...], "tbId":[skinId...]})
/// 并 UI.ShowConnection() 转圈，等服务端回包才 CloseConnection + 进战斗。
/// 服务端此前没有此接口 → 请求被静默丢弃，转圈永不消失，无法进战斗。
/// 随机前的原始皮肤记到字符串属性 (58,2)（客户端 Fashion.GID=58、SubID=2，
/// ResetRandomChangeSkinReq 会 GetStrAttribute(58,2) 判断/恢复）。
/// </summary>
[CallGSApi("GirlSkin_RandomChange")]
public class GirlSkin_RandomChange : ICallGSHandler
{
	private const uint RandomAttrGid = 58u;

	private const uint RandomAttrSid = 2u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<uint> cardIds = new List<uint>();
		List<uint> skinIds = new List<uint>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(param);
			if (doc.RootElement.ValueKind == JsonValueKind.Object)
			{
				if (doc.RootElement.TryGetProperty("tbCardId", out var cardsNode) && cardsNode.ValueKind == JsonValueKind.Array)
				{
					cardIds.AddRange(cardsNode.EnumerateArray().Select(x => x.TryGetUInt32(out var v) ? v : 0u));
				}
				if (doc.RootElement.TryGetProperty("tbId", out var skinsNode) && skinsNode.ValueKind == JsonValueKind.Array)
				{
					skinIds.AddRange(skinsNode.EnumerateArray().Select(x => x.TryGetUInt32(out var v) ? v : 0u));
				}
			}
		}
		catch
		{
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool changed = false;
		int n = System.Math.Min(cardIds.Count, skinIds.Count);
		if (n > 0)
		{
			// 已有的随机前皮肤映射（合并，避免连续两次随机覆盖掉最原始的皮肤）
			Dictionary<string, uint> orig = new Dictionary<string, uint>();
			string existing = player.Attributes.GetStringValue(RandomAttrGid, RandomAttrSid);
			if (!string.IsNullOrWhiteSpace(existing))
			{
				try
				{
					using JsonDocument od = JsonDocument.Parse(existing);
					foreach (var p in od.RootElement.EnumerateObject())
					{
						if (p.Value.TryGetUInt32(out var v))
						{
							orig[p.Name] = v;
						}
					}
				}
				catch
				{
				}
			}

			for (int i = 0; i < n; i++)
			{
				uint cardId = cardIds[i];
				uint skinId = skinIds[i];
				if (cardId == 0u || skinId == 0u)
				{
					continue;
				}
				var card = player.CharacterManager.GetCharacterByGUID(cardId);
				if (card == null)
				{
					continue;
				}
				if (card.SkinId != skinId)
				{
					if (!orig.ContainsKey(cardId.ToString()))
					{
						orig[cardId.ToString()] = card.SkinId;
					}
					card.SkinId = skinId;
					sync.Items.Add(card.ToProto());
					changed = true;
				}
			}

			if (changed)
			{
				string json = JsonSerializer.Serialize(orig);
				var attr = player.Attributes.SetString(RandomAttrGid, RandomAttrSid, json);
				player.Attributes.SyncTo(sync, attr);
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}

		// 回包只需到达即可触发客户端 CloseConnection + 进战斗回调
		await CallGSRouter.SendScript(connection, "GirlSkin_RandomChange", "{}", sync);
	}
}
