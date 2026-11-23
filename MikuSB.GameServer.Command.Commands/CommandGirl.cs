using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Character;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("girl", "Game.Command.Girl.Desc", "Game.Command.Girl.Usage", new string[] { "g" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGirl : ICommands
{
	[CommandMethod("add")]
	public async ValueTask AddGirl(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('s');
		int num6;
		int star = default;
		if (num5.HasValue)
		{
			star = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		Math.Clamp(level, 1, 80);
		star = Math.Clamp(star, 1, 9);
		PlayerInstance player = arg.Target.Player;
		List<CharacterInfo> girls = new List<CharacterInfo>();
		if (detail == -1)
		{
			foreach (CardExcel config in GameData.CardData.Values)
			{
				CharacterInfo character = await player.CharacterManager.AddCharacter((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, (uint)star, sendPacket: false);
				if (character != null)
				{
					girls.Add(character);
				}
			}
		}
		else
		{
			CharacterInfo girl = await player.CharacterManager.AddCharacter(ItemTypeEnum.TYPE_CARD, (uint)detail, (uint)particular, 1u, (uint)star, sendPacket: false);
			if (girl == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.NotFound"));
				return;
			}
			girls.Add(girl);
		}
		if (girls.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(girls));
		}
		await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.Added", girls.Count.ToString()));
	}

	[CommandMethod("level")]
	public async ValueTask UpdateGirlLevel(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()) || !(await arg.CheckArgCnt(2)))
		{
			return;
		}
		int guid = arg.GetInt(0);
		int level = arg.GetInt(1);
		level = Math.Clamp(level, 1, 80);
		PlayerInstance player = arg.Target.Player;
		List<CharacterInfo> girls = new List<CharacterInfo>();
		if (guid == -1)
		{
			foreach (CharacterInfo girl in player.CharacterManager.CharacterData.Characters)
			{
				girl.Level = (uint)level;
				girls.Add(girl);
			}
		}
		else
		{
			CharacterInfo girl2 = player.CharacterManager.GetCharacterByGUID((uint)guid);
			if (girl2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.NotFound"));
				return;
			}
			girl2.Level = (uint)level;
			girls.Add(girl2);
		}
		if (girls.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(girls));
		}
		await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.UpdateLevel", level.ToString(), girls.Count.ToString()));
	}

	[CommandMethod("neuronic")]
	public async ValueTask UpdateNeuronicLevel(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()) || !(await arg.CheckArgCnt(2)))
		{
			return;
		}
		int guid = arg.GetInt(0);
		int level = Math.Clamp(arg.GetInt(1), 0, 6);
		PlayerInstance player = arg.Target.Player;
		List<CharacterInfo> girls = new List<CharacterInfo>();
		List<uint> spines = new List<uint>();
		for (int i = 0; i < 6; i++)
		{
			spines.Add((i < level) ? 511u : 0u);
		}
		uint proLevel = (uint)(Enumerable.Count(spines, (uint x) => x == 511) / 2);
		if (guid == -1)
		{
			foreach (CharacterInfo girl in player.CharacterManager.CharacterData.Characters)
			{
				girl.Spines = spines;
				girl.ProLevel = proLevel;
				girls.Add(girl);
			}
		}
		else
		{
			CharacterInfo girl2 = player.CharacterManager.GetCharacterByGUID((uint)guid);
			if (girl2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.NotFound"));
				return;
			}
			girl2.Spines = spines;
			girl2.ProLevel = proLevel;
			girls.Add(girl2);
		}
		if (girls.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(girls));
		}
		await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.UpdateNeuronicLevel", level.ToString(), girls.Count.ToString()));
	}

	[CommandMethod("break")]
	public async ValueTask UpdateBreakLevel(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()) || !(await arg.CheckArgCnt(2)))
		{
			return;
		}
		int guid = arg.GetInt(0);
		int level = Math.Clamp(arg.GetInt(1), 0, 45);
		PlayerInstance player = arg.Target.Player;
		List<CharacterInfo> girls = new List<CharacterInfo>();
		if (guid == -1)
		{
			foreach (CharacterInfo girl in player.CharacterManager.CharacterData.Characters)
			{
				girl.Break = (uint)level;
				girls.Add(girl);
			}
		}
		else
		{
			CharacterInfo girl2 = player.CharacterManager.GetCharacterByGUID((uint)guid);
			if (girl2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.NotFound"));
				return;
			}
			girl2.Break = (uint)level;
			girls.Add(girl2);
		}
		if (girls.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(girls));
		}
		await arg.SendMsg(I18NManager.Translate("Game.Command.Girl.UpdateBreakLevel", level.ToString(), girls.Count.ToString()));
	}
}
