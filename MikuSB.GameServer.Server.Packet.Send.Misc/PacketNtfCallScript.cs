using System.Collections.Generic;
using System.Linq;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Send.Misc;

public class PacketNtfCallScript : BasePacket
{
	public PacketNtfCallScript(List<CharacterInfo> characters)
		: base(1010)
	{
		NtfCallScript data = new NtfCallScript
		{
			Api = "",
			Arg = "{}",
			ExtraSync = new NtfSyncPlayer
			{
				Items = { characters.Select((CharacterInfo x) => x.ToProto()) }
			}
		};
		SetData(data);
	}

	public PacketNtfCallScript(List<GameWeaponInfo> weapons)
		: base(1010)
	{
		NtfCallScript data = new NtfCallScript
		{
			Api = "",
			Arg = "{}",
			ExtraSync = new NtfSyncPlayer
			{
				Items = { weapons.Select((GameWeaponInfo x) => x.ToProto()) }
			}
		};
		SetData(data);
	}

	public PacketNtfCallScript(List<BaseGameItemInfo> items)
		: base(1010)
	{
		NtfCallScript data = new NtfCallScript
		{
			Api = "",
			Arg = "{}",
			ExtraSync = new NtfSyncPlayer
			{
				Items = { items.Select((BaseGameItemInfo x) => x.ToProto()) }
			}
		};
		SetData(data);
	}

	public PacketNtfCallScript(List<GameSupportCardInfo> cards)
		: base(1010)
	{
		NtfCallScript data = new NtfCallScript
		{
			Api = "",
			Arg = "{}",
			ExtraSync = new NtfSyncPlayer
			{
				Items = { cards.Select(ToSupportCardProto) }
			}
		};
		SetData(data);
	}

	public PacketNtfCallScript(List<GameSkinInfo> skins)
		: base(1010)
	{
		NtfCallScript data = new NtfCallScript
		{
			Api = "",
			Arg = "{}",
			ExtraSync = new NtfSyncPlayer
			{
				Items = { skins.Select((GameSkinInfo x) => x.ToProto()) }
			}
		};
		SetData(data);
	}

	public PacketNtfCallScript(InventoryData inventory)
		: base(1010)
	{
		NtfCallScript ntfCallScript = new NtfCallScript
		{
			Api = "",
			Arg = "{}"
		};
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		foreach (BaseGameItemInfo value in inventory.Items.Values)
		{
			ntfSyncPlayer.Items.Add(value.ToProto());
		}
		foreach (GameSkinInfo value2 in inventory.Skins.Values)
		{
			ntfSyncPlayer.Items.Add(value2.ToProto());
		}
		foreach (GameWeaponInfo value3 in inventory.Weapons.Values)
		{
			ntfSyncPlayer.Items.Add(value3.ToProto());
		}
		foreach (GameSupportCardInfo value4 in inventory.SupportCards.Values)
		{
			ntfSyncPlayer.Items.Add(ToSupportCardProto(value4));
		}
		ntfCallScript.ExtraSync = ntfSyncPlayer;
		SetData(ntfCallScript);
	}

	public PacketNtfCallScript(PlayerInstance Player)
		: base(1010)
	{
		Player.BuildPlayerAttr();
		NtfCallScript ntfCallScript = new NtfCallScript
		{
			Api = "",
			Arg = "{}"
		};
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		foreach (PlayerAttr attr in Player.Data.Attrs)
		{
			uint gid = attr.Gid;
			uint sid = attr.Sid;
			uint val = attr.Val;
			if (gid == 0)
			{
				ntfSyncPlayer.Custom[sid] = val;
				continue;
			}
			ntfSyncPlayer.Custom[Player.ToPackedAttrKey(gid, sid)] = val;
			ntfSyncPlayer.Custom[Player.ToShiftedAttrKey(gid, sid)] = val;
		}
		foreach (var (key, value) in Player.BuildMoneySync())
		{
			ntfSyncPlayer.Money[key] = value;
		}
		ntfCallScript.ExtraSync = ntfSyncPlayer;
		SetData(ntfCallScript);
	}

	private static Item ToSupportCardProto(GameSupportCardInfo card)
	{
		SupportAffixStateService.NormalizePendingState(card);
		Item item = card.ToProto();
		item.Slots[1u] = SupportAffixStateService.GetVisibleInitialAffixIndex(card);
		return item;
	}
}
