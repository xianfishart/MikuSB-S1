using System;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 大厅（主界面）场景切换（2026-11-12 新增，修复记录第 238 节）。
///
/// 场景 id 取自 Resources/main/scene_change.json 的 Id：
///   1       = Entry_01   「尘白之间」二维插画（主界面是 2D 背景图）
///   2       = Entry_01b  「世界树营地」3D 场景
///   21001~21014 = 3D 情景（需要相应 manifestation 解锁）
///
/// 写法和客户端 ChangeMainScene 请求完全一致：
///   132/1 = 所选 id；139/401~414 固定大厅时 14 格全写同一 id；
///   139/901 = 0（关掉随机）；最后落库，重登后生效。
/// </summary>
[CommandInfo("mainscene", "Set lobby (main) scene: 1=2D Entry_01, 2=3D Entry_01b, 21001~21014=3D scene.", "Usage: /mainscene <id>  (append @uid)", new string[]
{
	"mset"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandMainScene : ICommands
{
	private const uint MainSceneGid = 132u;

	private const uint MainSceneSid = 1u;

	private const uint ShowGirlGid = 139u;

	private const uint ShowGirlModeSid = 901u;

	private const uint SceneSlotBase = 401u;

	private const uint SceneSlotCount = 14u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		uint id = (uint)Math.Max(0, arg.GetInt(0));
		if (id == 0u)
		{
			await arg.SendMsg("Usage: /mainscene <id>   1=二维插画(尘白之间)  2=3D(世界树营地)  21001~21014=3D情景");
			return;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		PlayerAttr sceneAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == MainSceneGid && x.Sid == MainSceneSid);
		if (sceneAttr == null)
		{
			sceneAttr = new PlayerAttr
			{
				Gid = MainSceneGid,
				Sid = MainSceneSid
			};
			player.Data.Attrs.Add(sceneAttr);
		}
		sceneAttr.Val = id;
		int slots = 0;
		if (id == 1u || id == 2u)
		{
			for (uint i = 0u; i < SceneSlotCount; i++)
			{
				SetShowGirl(player, SceneSlotBase + i, id);
				slots++;
			}
		}
		else
		{
			SetShowGirl(player, SceneSlotBase, id);
			slots++;
		}
		SetShowGirl(player, ShowGirlModeSid, 0u);
		DatabaseHelper.SaveDatabaseType(player.Data);
		string name = id switch
		{
			1u => "尘白之间（二维插画）",
			2u => "世界树营地（3D）",
			_ => $"3D 情景 {id}"
		};
		await arg.SendMsg($"mainscene: 132/1={id} → {name}；已写 {slots} 个候选槽 + 901=0 并落库，重登后生效 (uid {player.Uid})");
	}

	private static void SetShowGirl(PlayerInstance player, uint sid, uint value)
	{
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == ShowGirlGid && x.Sid == sid);
		if (attr == null)
		{
			player.Data.Attrs.Add(new PlayerAttr
			{
				Gid = ShowGirlGid,
				Sid = sid,
				Val = value
			});
			return;
		}
		attr.Val = value;
	}
}
