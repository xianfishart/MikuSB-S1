using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

/// <summary>
/// 第 519 节：恢复「队员时装随机」前的原始皮肤。
/// 客户端 Fashion.ResetRandomChangeSkinReq（Fashion.lua:419-428）：
/// GetStrAttribute(58,2) 为空时发 me:CallGS("GirlSkin_ResetRandomChange", "Fashion")。
/// 服务端读 (58,2) 里记录的 {卡GUID: 原皮肤ID} 逐卡恢复并清空属性。
/// </summary>
[CallGSApi("GirlSkin_ResetRandomChange")]
public class GirlSkin_ResetRandomChange : ICallGSHandler
{
	private const uint RandomAttrGid = 58u;

	private const uint RandomAttrSid = 2u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		string existing = player.Attributes.GetStringValue(RandomAttrGid, RandomAttrSid);
		if (!string.IsNullOrWhiteSpace(existing))
		{
			bool changed = false;
			try
			{
				using JsonDocument doc = JsonDocument.Parse(existing);
				foreach (var p in doc.RootElement.EnumerateObject())
				{
					if (!uint.TryParse(p.Name, out uint cardId) || !p.Value.TryGetUInt32(out uint skinId))
					{
						continue;
					}
					var card = player.CharacterManager.GetCharacterByGUID(cardId);
					if (card != null && card.SkinId != skinId)
					{
						card.SkinId = skinId;
						sync.Items.Add(card.ToProto());
						changed = true;
					}
				}
			}
			catch
			{
			}
			if (changed)
			{
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			}
			var attr = player.Attributes.SetString(RandomAttrGid, RandomAttrSid, "");
			player.Attributes.SyncTo(sync, attr);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}

		await CallGSRouter.SendScript(connection, "GirlSkin_ResetRandomChange", "{}", sync);
	}
}
