using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

public readonly struct ItemTemplateInfo
{
	public ulong TemplateId { get; }

	public string LuaType { get; }

	public uint Param1 { get; }

	public uint UseMode { get; }

	public bool IsBox => LuaType == "itembox" && Param1 != 0;

	public bool IsVigorBox => LuaType == "vigor_box" && Param1 != 0;

	private ItemTemplateInfo(ulong templateId, string luaType, uint param1, uint useMode)
	{
		TemplateId = templateId;
		LuaType = luaType;
		Param1 = param1;
		UseMode = useMode;
	}

	public static bool TryResolve(ulong templateId, out ItemTemplateInfo info)
	{
		if (GameData.OtherItemByFullId.TryGetValue(templateId, out OtherItemExcel value))
		{
			info = new ItemTemplateInfo(templateId, value.LuaType ?? "", value.Param1, value.UseMode);
			return true;
		}
		if (GameData.SuppliesByFullId.TryGetValue(templateId, out SuppliesExcel value2))
		{
			info = new ItemTemplateInfo(templateId, value2.LuaType ?? "", value2.Param1, value2.UseMode);
			return true;
		}
		info = default;
		return false;
	}

	public static Item ToSyncProto(BaseGameItemInfo item)
	{
		return item.ToProto();
	}
}
