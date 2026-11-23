using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Player;

internal static class VigorRecovery
{
	public const uint Gid = 9900u;

	public const uint SidAnchor = 1u;

	public const int VigorSeconds = 300;

	private const uint FallbackMax = 240u;

	private static readonly Logger Log = new Logger("VigorRecovery");

	private static Dictionary<uint, uint> _maxByLevel;

	private static readonly object _lock = new object();

	public static uint MaxOf(PlayerInstance player)
	{
		if (player?.Data == null)
		{
			return 240u;
		}
		Dictionary<uint, uint> dictionary = LoadMax();
		if (!dictionary.TryGetValue(player.Data.Level, out var value))
		{
			value = ((dictionary.Count > 0) ? dictionary.Values.Last() : 240u);
		}
		if (value == 0)
		{
			value = 240u;
		}
		return value;
	}

	private static Dictionary<uint, uint> LoadMax()
	{
		lock (_lock)
		{
			if (_maxByLevel != null)
			{
				return _maxByLevel;
			}
			Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "player", "levels.json");
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject jsonObject)
							{
								int num = jsonObject["Level"]?.GetValue<int>() ?? 0;
								int num2 = jsonObject["MaxVigor"]?.GetValue<int>() ?? 0;
								if (num > 0 && num2 > 0)
								{
									dictionary[(uint)num] = (uint)num2;
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warn("load levels.json failed: " + ex.Message);
			}
			_maxByLevel = dictionary;
			return dictionary;
		}
	}

	private static uint Now()
	{
		return (uint)Math.Min(4294967295L, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}

	public static bool Apply(PlayerInstance player)
	{
		if (player?.Data == null)
		{
			return false;
		}
		PlayerGameData data = player.Data;
		uint num = MaxOf(player);
		uint num2 = Now();
		bool result = false;
		// 体力允许溢出（第 229 节：数据金购买体力不封顶，好友赠送同理）。
		// 这里只兜「明显损坏」的存档（历史上有被写成几十万的情况），
		// 正常的 240 → 300+ 一律保留；下面的恢复逻辑只往上补到上限，不会往下削。
		if (data.Vigor > num * 20)
		{
			Log.Warn($"uid={data.Uid} vigor {data.Vigor} far above max {num}, clamp");
			data.Vigor = num;
			result = true;
		}
		uint num3 = player.Attributes.GetValue(9900u, 1u);
		if (num3 == 0 || num3 > num2)
		{
			num3 = num2;
			player.Attributes.Set(9900u, 1u, num2);
			result = true;
		}
		if (data.Vigor < num)
		{
			long num4 = ((long)num2 - (long)num3) / 300;
			if (num4 > 0)
			{
				ulong num5 = (ulong)(data.Vigor + num4);
				if (num5 >= num)
				{
					data.Vigor = num;
					num3 = num2;
				}
				else
				{
					data.Vigor = (uint)num5;
					num3 += (uint)(int)(num4 * 300);
				}
				player.Attributes.Set(9900u, 1u, num3);
				Log.Info($"uid={data.Uid} vigor recovered to {data.Vigor}/{num}");
				result = true;
			}
		}
		else if (num3 != num2)
		{
			player.Attributes.Set(9900u, 1u, num2);
			result = true;
		}
		return result;
	}

	public static uint AddCapped(PlayerInstance player, uint amount)
	{
		uint num = MaxOf(player);
		ulong num2 = (ulong)(player?.Data.Vigor ?? 0) + (ulong)amount;
		uint num3 = (uint)((num2 >= num) ? num : num2);
		if (player?.Data != null)
		{
			player.Data.Vigor = num3;
		}
		return num3;
	}
}
