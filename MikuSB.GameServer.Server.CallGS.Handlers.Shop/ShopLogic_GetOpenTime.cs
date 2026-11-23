using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_GetOpenTime")]
public class ShopLogic_GetOpenTime : ICallGSHandler
{
	private static JsonArray? _tabs;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject response = new JsonObject();
		foreach (JsonNode node in LoadTabs())
		{
			if (node is JsonObject tab && int.TryParse(tab["ShopId"]?.ToString(), out var shopId) && shopId > 0)
			{
				response[shopId.ToString()] = new JsonObject
				{
					["nBegin"] = ToUnix(tab["Begin"]?.ToString()),
					["nEnd"] = ToUnix(tab["End"]?.ToString())
				};
			}
		}
		// 第 235 节（2026-11-11）：活动商店统一走原生页签，追忆商店整体关闭。
		// 原来这里额外放开了 401~404（往期活动的「追忆精选」摊位），客户端因此
		// 认为它们长期开放，界面就把「追忆精选」画成第一个页签。现在不再放开，
		// 让它们沿用 Resources/shop/shop_tab.json 里的原始时间（2024~2025，已过期），
		// 不满足开放条件，自然不出现。101~148 保持全开，覆盖各期原生限量/无限供应。
		foreach (int item in Enumerable.Range(101, 48))
		{
			response[item.ToString()] = new JsonObject
			{
				["nBegin"] = 0,
				["nEnd"] = 9999999999L
			};
		}
		await CallGSRouter.SendScript(connection, "ShopLogic_GetOpenTime", response.ToJsonString());
	}

	private static long ToUnix(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0L;
		}
		string s = raw.Trim().Trim(new char[2] { '[', ']' });
		DateTime result;
		return DateTime.TryParseExact(s, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTimeOffset(result).ToUnixTimeSeconds() : 0;
	}

	private static JsonArray LoadTabs()
	{
		if (_tabs != null)
		{
			return _tabs;
		}
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "shop_tab.json");
			_tabs = ((!File.Exists(path)) ? new JsonArray() : JsonNode.Parse(File.ReadAllText(path))?.AsArray());
		}
		catch
		{
			_tabs = new JsonArray();
		}
		return _tabs;
	}
}
