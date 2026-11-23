using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Scene;

/// <summary>
/// 大厅切换场景（客户端 api：ChangeMainScene，参数 {"Id":21007}）。
///
/// 存档布局（第 350 节按官服行为重新核对）：
///   132/1        = 当前场景 id
///   139/401~414  = 3D 情景候选池，14 格，登录时从中随机挑一个非空槽
///   139/901      = 0 = 3D，1 = 2D
///
/// 第 350 节之前的写法是「401 写所选、402~413 用整张 scene_change.json 洗牌填充」，
/// 这会把 1/2（尘白之间 / 世界树营地）这类固定大厅也灌进互动情景池，
/// 于是「随机封面互动设置」里勾的几张被冲掉，面板只剩一格能认出来、
/// 重登反复落在同一个固定大厅上。现在改成：
///   · 客户端明确点了某一个场景 → 池子就是它（14 格全写同一个 id，确定、不留空槽）；
///   · 想随机就在「随机封面互动设置」里勾，由 RandomShowGirl_SetMainBg 写池子。
/// </summary>
[CallGSApi("ChangeMainScene")]
public class ChangeMainScene : ICallGSHandler
{
	private const int MainSceneGID = 132;

	private const int MainSceneSID = 1;

	private const uint ShowGirlGID = 139u;

	private const uint ShowGirlModeSID = 901u;

	private const uint ShowGirlSceneBaseSID = 401u;

	/// <summary>3D 互动情景池格数（401~414，对应 21001~21014 共 14 个情景）。</summary>
	private const uint ShowGirlSceneSlots = 14u;

	private static readonly Lazy<HashSet<uint>> FixedLobbyScenes = new Lazy<HashSet<uint>>(LoadFixedLobbyScenes);

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		string arg = "{\"sErr\":false}";
		ChangeMainSceneParam changeMainSceneParam = JsonSerializer.Deserialize<ChangeMainSceneParam>(param);
		if (changeMainSceneParam == null)
		{
			await CallGSRouter.SendScript(connection, "ChangeMainScene", arg);
			return;
		}
		PlayerInstance player = connection.Player;
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 132 && x.Sid == 1);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 132u,
				Sid = 1u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		uint sceneId = ((changeMainSceneParam.Id == 0) ? 1u : changeMainSceneParam.Id);
		playerAttr.Val = sceneId;
		ntfSyncPlayer.Custom[player.ToPackedAttrKey(132u, 1u)] = playerAttr.Val;
		ntfSyncPlayer.Custom[player.ToShiftedAttrKey(132u, 1u)] = playerAttr.Val;
		// 池子 = 所选这一个（14 格全写同一值，登录随机时结果恒定，也不会留空槽）
		for (uint i = 0u; i < ShowGirlSceneSlots; i++)
		{
			SetAttr139(player, ntfSyncPlayer, ShowGirlSceneBaseSID + i, sceneId);
		}
		SetAttr139(player, ntfSyncPlayer, ShowGirlModeSID, 0u);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "ChangeMainScene", arg, ntfSyncPlayer);
	}

	/// <summary>
	/// 固定大厅（尘白之间 / 世界树营地）对齐：132/1 是固定场景但 14 个槽位没对齐时补正。
	/// 老存档里 401~413 可能被写成 2、414 还留着别的值，登录时一次性抹平。
	/// </summary>
	public static bool AlignFixedMainScene(PlayerInstance player)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 132 && x.Sid == 1);
		if (playerAttr == null || !FixedLobbyScenes.Value.Contains(playerAttr.Val))
		{
			return false;
		}
		bool result = false;
		for (uint num = 0u; num < ShowGirlSceneSlots; num++)
		{
			uint sid = ShowGirlSceneBaseSID + num;
			PlayerAttr playerAttr2 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == ShowGirlGID && x.Sid == sid);
			if (playerAttr2 == null)
			{
				player.Data.Attrs.Add(new PlayerAttr
				{
					Gid = ShowGirlGID,
					Sid = sid,
					Val = playerAttr.Val
				});
				result = true;
			}
			else if (playerAttr2.Val != playerAttr.Val)
			{
				playerAttr2.Val = playerAttr.Val;
				result = true;
			}
		}
		return result;
	}

	private static HashSet<uint> LoadFixedLobbyScenes()
	{
		HashSet<uint> hashSet = new HashSet<uint>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "main", "scene_change.json");
			if (File.Exists(path))
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
				foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
				{
					if (item.TryGetProperty("Id", out var value) && value.TryGetUInt32(out var value2) && value2 != 0 && item.TryGetProperty("SceneID", out var value3) && value3.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value3.GetString()))
					{
						hashSet.Add(value2);
					}
				}
			}
		}
		catch
		{
		}
		if (hashSet.Count == 0)
		{
			hashSet.Add(1u);
			hashSet.Add(2u);
		}
		return hashSet;
	}

	private static void SetAttr139(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 139 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 139u,
				Sid = sid
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = value;
		sync.Custom[player.ToPackedAttrKey(139u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(139u, sid)] = value;
	}
}
