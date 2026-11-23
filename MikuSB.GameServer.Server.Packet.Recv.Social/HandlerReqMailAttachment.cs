using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

[Opcode(13)]
public class HandlerReqMailAttachment : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		ulong mid = MailService.ReadMid(data);
		List<Mail> mails = MailService.Load(player);
		Mail mail = mails.FirstOrDefault((Mail m) => m.Mid == mid);
		if (mail == null)
		{
			await connection.SendPacket(14, new Mail(), 0);
			return;
		}
		if (mail.Stat != MailStat.Geted)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			foreach (SimpleItem att in mail.Attachments)
			{
				uint attCount = Math.Max(1u, att.Count);
				// 第 299 节：货币载体物品（记忆补嵌包 / 智略点）直接换货币，不走物品发放。
				if (MailService.TryGrantAttachmentCurrency(player, sync, att, attCount))
				{
					continue;
				}
				List<uint> reward = new List<uint>
				{
					att.G,
					att.D,
					att.P,
					att.L,
					attCount
				};
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			}
			mail.Stat = MailStat.Geted;
			MailService.Save(player, mails);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			await connection.SendPacket(1004, sync, 0);
		}
		await connection.SendPacket(14, mail, 0);
	}
}
