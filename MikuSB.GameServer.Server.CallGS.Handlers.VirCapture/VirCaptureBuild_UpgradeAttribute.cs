using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛「升级建筑属性」（第 498 节新增）。
///
/// 客户端：VirCaptureBuild.ReqUpgradeAttribute(nId, nAttrIndx)
///   me:CallGS("VirCaptureBuild_UpgradeAttribute", json.encode({nId=.., nAttrIndx=..}))
/// 属性等级写在 161/(10000 + nId*30 + 6 + (nAttrIndx-1))，nAttrIndx = 1..5。
///
/// 花费：building.json 里 Attr1/2/3 是「{某级: 数值}」形态的收益表，不是花费表，
/// 官方真正的升级材料编码还没确认，先不扣（记一条 INFO，方便以后对表）。
/// </summary>
[CallGSApi("VirCaptureBuild_UpgradeAttribute")]
public class VirCaptureBuild_UpgradeAttribute : ICallGSHandler
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildAttr");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		AttrParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<AttrParam>(param);
			}
		}
		catch (JsonException)
		{
		}
		if (req == null || req.Id == 0u || req.AttrIndex < 1u || req.AttrIndex > VirCaptureBuildState.MaxAttr)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!VirCaptureBuildTable.TryGet(req.Id, out VirCaptureBuildingRow cfg))
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (VirCaptureBuildState.GetLevel(player, cfg.Id) == 0u)
		{
			// 建筑还没建，属性无从升。
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint current = VirCaptureBuildState.GetAttrLevel(player, cfg.Id, req.AttrIndex);
		// 第 509 节：属性等级上限取建筑配置的 nMaxAttrLevel（官服 16）。
		// 旧实现拿 VirCaptureBuildState.MaxAttr（= 5，客户端的「属性种类数」）当等级上限，
		// 结果三条属性全部卡在 5 级：客户端显示「等级5/16」还能点，服务端一律回 TxtLevelupMax，
		// 界面看起来就是「点了没反应、数字始终不变」。
		uint maxAttrLevel = Math.Max(1u, cfg.MaxAttrLevel);
		if (current >= maxAttrLevel)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtLevelupMax\"}");
			return;
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 第 509 节：属性升级要收蓝图币，原来一点不扣（改完属性等级就 return），
		// 玩家在界面上看到 2160/1280/880 这些价格，扣款却始终为 0。
		// 价格取 building.json 的 Attr1/2/3（按等级排列），实测与界面完全一致。
		uint nextAttrLevel = current + 1u;
		uint cost = cfg.AttrCostToReach(req.AttrIndex, nextAttrLevel);
		uint cashId = VirCaptureStaffTable.CashId(cfg.MapId == 0u ? 326u : cfg.MapId);
		if (cost > 0u && cashId != 0u)
		{
			if (PortCurrency.GetBalance(player, cashId) < cost)
			{
				Log.Info($"[VirCapture] build {cfg.Id} attr{req.AttrIndex} lv{nextAttrLevel} cost {cost} balance={PortCurrency.GetBalance(player, cashId)} -> not enough uid={player.Uid}");
				await Send(connection, "{\"sErr\":\"error.gold_not_enough\"}");
				return;
			}
			if (!PortCurrency.TrySpend(player, cashId, cost, sync, out string error))
			{
				await Send(connection, "{\"sErr\":\"" + error + "\"}");
				return;
			}
		}
		VirCaptureBuildState.Write(player, cfg.Id, VirCaptureBuildState.OffAttr1 + req.AttrIndex - 1u, nextAttrLevel, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"[VirCapture] build {cfg.Id} attr{req.AttrIndex} {current} -> {nextAttrLevel} cost={cost} cash={cashId} uid={player.Uid}");
		// 回包带上 nId / nAttrIndx / nLevel：客户端 PalManage.UpgradeBuildingAttrRsp 会按 nId 刷新界面。
		JsonObject reply = new JsonObject
		{
			["nId"] = cfg.Id,
			["nAttrIndx"] = req.AttrIndex,
			["nLevel"] = nextAttrLevel
		};
		await CallGSRouter.SendScript(connection, "VirCaptureBuild_UpgradeAttribute", reply.ToJsonString(), sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureBuild_UpgradeAttribute", arg);
	}

	private sealed class AttrParam
	{
		[JsonPropertyName("nId")]
		public uint Id { get; set; }

		[JsonPropertyName("nAttrIndx")]
		public uint AttrIndex { get; set; }
	}
}
