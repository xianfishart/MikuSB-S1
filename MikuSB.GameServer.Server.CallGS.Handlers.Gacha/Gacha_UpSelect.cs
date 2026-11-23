using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

[CallGSApi("Gacha_UpSelect")]
public class Gacha_UpSelect : ICallGSHandler
{
	private const uint GachaStrGid = 42u;

	private const int UpSelectIndex = 0;

	private const int UpSelectGetFlagIndex = 1;

	private const int UpPickPoolIndex = 2;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		GachaUpSelectParam req = System.Text.Json.JsonSerializer.Deserialize<GachaUpSelectParam>(param);
		PlayerInstance player = connection.Player;
		if (req == null || req.NId == 0 || req.Gdpl == null || req.Gdpl.Count < 4)
		{
			await CallGSRouter.SendScript(connection, "Gacha_UpSelect", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.GachaData.TryGetValue((uint)req.NId, out GachaExcel gachaCfg) || gachaCfg.UpSelect != 1)
		{
			await CallGSRouter.SendScript(connection, "Gacha_UpSelect", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!(gachaCfg.Pool ?? new List<string>()).Where(GameData.GachaPoolData.ContainsKey).SelectMany((string name) => GameData.GachaPoolData[name]).Any((GachaPoolItem item) => item.UPSelectTag == 1 && item.GDPL.Count >= 4 && item.GDPL[0] == req.Gdpl[0] && item.GDPL[1] == req.Gdpl[1] && item.GDPL[2] == req.Gdpl[2] && item.GDPL[3] == req.Gdpl[3]))
		{
			await CallGSRouter.SendScript(connection, "Gacha_UpSelect", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		string existing = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 42 && x.Sid == (uint)req.NId)?.Val;
		JArray state = (string.IsNullOrWhiteSpace(existing) ? new JArray() : JArray.Parse(existing));
		EnsureArraySize(state, 3);
		state[0] = new JArray(req.Gdpl);
		state[1] = 0;
		if (state[2] == null)
		{
			state[2] = 0;
		}
		player.SetStrAttr(42u, (uint)req.NId, state.ToString(Formatting.None));
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.CustomStr[player.ToShiftedAttrKey(42u, (uint)req.NId)] = state.ToString(Formatting.None);
		await CallGSRouter.SendScript(connection, "Gacha_UpSelect", "{}", sync);
	}

	private static void EnsureArraySize(JArray state, int size)
	{
		while (state.Count < size)
		{
			state.Add(JValue.CreateNull());
		}
	}
}
