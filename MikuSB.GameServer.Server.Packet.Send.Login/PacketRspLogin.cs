using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Server.Packet.Send.Login;

public class PacketRspLogin : BasePacket
{
	private static readonly Logger Logger = new Logger("RspLogin");

	public PacketRspLogin(PlayerInstance player, bool includeSupportCards = true)
		: base(2)
	{
		int count = player.CharacterManager.CharacterData.Characters.Count;
		int count2 = player.InventoryManager.InventoryData.Items.Count;
		int count3 = player.InventoryManager.InventoryData.Skins.Count;
		int count4 = player.InventoryManager.InventoryData.Weapons.Count;
		int count5 = player.InventoryManager.InventoryData.SupportCards.Count;
		int count6 = player.Data.Attrs.Count;
		int count7 = player.Data.StrAttrs.Count;
		int count8 = player.Data.ShowItems.Count;
		RspLogin rspLogin = new RspLogin
		{
			Timestamp = (uint)Extensions.GetUnixSec(),
			WorldChannel = 1u,
			AreaId = 1u,
			Data = player.ToPlayerProto(includeSupportCards),
			NeedRename = false
		};
		byte[] array = rspLogin.ToByteArray();
		Logger.Info("RspLogin content: " + $"characters={count}, " + $"items={count2}, " + $"skins={count3}, " + $"weapons={count4}, " + $"supportCards={count5}, " + $"supportCardsInRspLogin={(includeSupportCards ? count5 : 0)}, " + $"attrs={count6}, " + $"strAttrs={count7}, " + $"showItems={count8}, " + $"protoItems={rspLogin.Data.Items.Count}, " + $"protoAttrs={rspLogin.Data.Attrs.Count}, " + $"protoStrAttrs={rspLogin.Data.StrAttrs.Count}, " + "money=" + string.Join(",", rspLogin.Data.Money.Select((KeyValuePair<string, int> kv) => $"{kv.Key}={kv.Value}")) + ", channel=" + rspLogin.Data.Channel + "." + rspLogin.Data.Subchannel);
		Logger.Info($"RspLogin proto size: {array.Length} bytes");
		SetData(array);
	}
}
