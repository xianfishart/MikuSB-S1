using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

[CallGSApi("DreamCard_CheckOpen")]
public class DreamCard_CheckOpen : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		DateTime now = DateTime.Now;
		JsonValue[] ids = (from x in GameData.DreamCardActivityData.Values
			where IsOpen(x, now)
			orderby x.ID
			select JsonValue.Create(x.ID)).ToArray();
		JsonObject response = new JsonObject { ["tbID"] = new JsonArray(ids) };
		await CallGSRouter.SendScript(connection, "DreamCard_CheckOpen", response.ToJsonString());
	}

	private static bool IsOpen(DreamCardActivityExcel config, DateTime now)
	{
		DateTime? dateTime = ParseConfigTime(config.StartTime);
		if (!dateTime.HasValue || dateTime > now)
		{
			return false;
		}
		DateTime? dateTime2 = ParseConfigTime(config.EndTime);
		if (dateTime2.HasValue && now >= dateTime2.Value)
		{
			return false;
		}
		return string.IsNullOrWhiteSpace(config.Condition);
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}
}
