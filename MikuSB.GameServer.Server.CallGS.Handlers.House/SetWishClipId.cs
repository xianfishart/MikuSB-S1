using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 基地「信源交换 — 设置关键断片（许愿）」（House_Request: SetWishClipId）。
///
/// 客户端 umg_dorm_jigsaw_wish 的确定按钮发 {FuncName="SetWishClipId", WishClipId=N}（不弹遮罩），
/// 回包回调名是 **SetWishClipIdSuccess**，只调 DormClipWish:OnSetSuccess()。
///
/// 2026-11-13 补充（修复记录第 296 节）：
///   · 3167 存的是 **ClipId-1**，不是 ClipId —— 客户端 OnOpen 读出后 +1 才当选中项用；
///     实测请求 WishClipId=6、服务端写 5，界面选中的就是 6，故保持 -1 约定。
///   · 3166 存「今天是否已许愿」的标记（客户端只判断 != 0，非 0 就弹 ui.ClipSetTimeOut
///     并且根本不打开许愿界面）。所以 DormPuzzleService 的每日重置里补了 3166=0，
///     否则许过一次之后按钮就永远点不开了。
/// </summary>
[HouseFunc("SetWishClipId")]
public class SetWishClipId : IHouseFuncHandler
{
	private static readonly Logger Log = new Logger("SetWishClipId");

	private const uint NowWishClipIdSid = 3167u;

	private const uint SetWishClipTimesSid = 3166u;

	private const uint HasOpenedWishClipUiSid = 60103u;

	private const int MaxClipId = 11;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			JsonObject request = (root["tbParam"] as JsonObject) ?? root;
			int wishClipId = HouseJson.NumField(request, "WishClipId");
			if (wishClipId <= 0 || wishClipId > MaxClipId)
			{
				Log.Info($"uid={player.Uid} bad WishClipId={wishClipId}");
				await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"SetWishClipIdSuccess\",\"bSuccess\":false,\"sErr\":\"error.BadParam\"}");
				return;
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			await HouseAttr.SetAsync(connection, NowWishClipIdSid, (uint)(wishClipId - 1), sync);
			await HouseAttr.SetAsync(connection, SetWishClipTimesSid, 1u, sync);
			await HouseAttr.SetAsync(connection, HasOpenedWishClipUiSid, 1u, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"uid={player.Uid} wish clip -> {wishClipId} (stored {wishClipId - 1})");
			await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"SetWishClipIdSuccess\",\"bSuccess\":true}", sync);
		}
	}
}
