using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(1015)]
public class HandlerNtfReadItem : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		IDArray req = IDArray.Parser.ParseFrom(data);
		InventoryManager inv = connection.Player?.InventoryManager;
		if (inv == null)
		{
			return;
		}
		bool changed = false;
		foreach (ulong rawId in req.Ids)
		{
			uint id = (uint)rawId;
			if (inv.InventoryData.Items.TryGetValue(id, out BaseGameItemInfo item) && item.Flag != ItemFlagEnum.FLAG_READED)
			{
				item.Flag = ItemFlagEnum.FLAG_READED;
				changed = true;
				item = null;
			}
		}
		if (changed)
		{
			DatabaseHelper.SaveDatabaseType(inv.InventoryData);
		}
		await Task.CompletedTask;
	}
}
