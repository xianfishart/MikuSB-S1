using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Character;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_SetRoleLikeFlag")]
public class GirlCard_SetRoleLikeFlag : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SetRoleLikeFlagParam girlData = JsonSerializer.Deserialize<SetRoleLikeFlagParam>(param);
		if (girlData != null)
		{
			CharacterInfo cardData = player.CharacterManager.GetCharacterByGUID(girlData.CardId);
			if (cardData != null)
			{
				cardData.Flag = ((girlData.Flag == 1) ? ItemFlagEnum.FLAG_ROLE_LIKE : ItemFlagEnum.FLAG_READED);
				NtfSyncPlayer sync = new NtfSyncPlayer
				{
					Items = { cardData.ToProto() }
				};
				await CallGSRouter.SendScript(connection, "GirlCard_SetRoleLikeFlag", "{}", sync);
			}
		}
	}
}
