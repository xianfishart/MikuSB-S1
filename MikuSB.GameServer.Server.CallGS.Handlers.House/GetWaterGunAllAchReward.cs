using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 心动水弹：一键领取全部任务奖励（2026-11-12 重写，修复记录第 261 节）。
/// 客户端报文：{"FuncName":"GetWaterGunAllAchReward"}。
/// 旧实现用 Resources/house/watergun_ach.json 取 ID 列表，但该文件根本不存在，
/// 所以一键领取永远返回空数组；即使文件存在，走 TaskProgressService 也是错表（Gid=2）。
/// 现在 ID 列表直接取 Resources/house/LoveGun/WaterGunAchiev.json 的 20 条（20101~20120），
/// 逐条走 HouseAchievementClaim，只发真正满足条件且未领的。
/// </summary>
[HouseFunc("GetWaterGunAllAchReward")]
public class GetWaterGunAllAchReward : IHouseFuncHandler
{
	private static List<int>? _ids;

	public async Task Handle(Connection connection, string param)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		JsonArray claimed = new JsonArray();
		foreach (int id in LoadIds())
		{
			JsonArray granted = await HouseAchievementClaim.ClaimAsync(connection, id, sync);
			if (granted.Count <= 0)
			{
				continue;
			}
			claimed.Add(id);
			foreach (JsonNode? item in granted)
			{
				rewards.Add(item?.DeepClone());
			}
		}
		// 2026-11-12 追加（修复记录第 263 节）：FuncName 必须带 Rsp 后缀。
		// 客户端回调名是 GetWaterGunAllAchRewardRsp，名字不匹配时回调不会被调用，
		// 于是 CloseConnection 也不执行 —— 表现就是点「一键领取」后一直转圈。
		// tbAchsGet 是客户端回调里实际读取的字段（反汇编常量表取证）。
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GetWaterGunAllAchRewardRsp",
			["bReceived"] = rewards.Count > 0,
			["tbAchsGet"] = claimed,
			["tbAchId"] = claimed.DeepClone(),
			["tbReward"] = rewards,
			["tbRewards"] = rewards.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}

	private static List<int> LoadIds()
	{
		if (_ids != null)
		{
			return _ids;
		}
		List<int> list = new List<int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "LoveGun", "WaterGunAchiev.json");
			if (File.Exists(path))
			{
				JsonArray? rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (rows != null)
				{
					foreach (JsonObject obj in rows.OfType<JsonObject>())
					{
						int id = HouseJson.NumField(obj, "ID");
						if (id > 0)
						{
							list.Add(id);
						}
					}
				}
			}
		}
		catch
		{
		}
		_ids = list;
		return _ids;
	}
}
