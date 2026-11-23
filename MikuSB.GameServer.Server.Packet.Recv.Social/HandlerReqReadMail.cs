using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

[Opcode(11)]
public class HandlerReqReadMail : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			ulong mid = MailService.ReadMid(data);
			List<Mail> mails = MailService.Load(player);
			Mail mail = mails.FirstOrDefault((Mail m) => m.Mid == mid);
			if (mail != null && mail.Stat == MailStat.New)
			{
				mail.Stat = MailStat.Readed;
			}
			MailService.Save(player, mails);
			await connection.SendPacket(12, mail ?? new Mail(), 0);
		}
	}
}
