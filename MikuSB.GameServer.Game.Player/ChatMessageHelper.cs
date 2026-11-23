using System.CodeDom.Compiler;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Player;

public static class ChatMessageHelper
{
	[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
	private static Regex MultiWhitespaceRegex()
	{
		return _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__MultiWhitespaceRegex_0.Instance;
	}

	public static uint BuildClientTimestamp()
	{
		return (uint)Extensions.GetUnixSec();
	}

	public static string NormalizeForClient(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		string input = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
		return MultiWhitespaceRegex().Replace(input, " ").Trim();
	}
}
