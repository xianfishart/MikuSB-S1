using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
[SkipLocalsInit]
internal sealed class _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__MultiWhitespaceRegex_0 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if ((uint)num < (uint)inputSpan.Length)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__Utilities.s_whitespace);
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				ReadOnlySpan<char> readOnlySpan = inputSpan.Slice(num);
				int i;
				for (i = 0; (uint)i < (uint)readOnlySpan.Length && char.IsWhiteSpace(readOnlySpan[i]); i++)
				{
				}
				if (i == 0)
				{
					return false;
				}
				readOnlySpan = readOnlySpan.Slice(i);
				Capture(0, start, runtextpos = num + i);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__MultiWhitespaceRegex_0 Instance = new _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__MultiWhitespaceRegex_0();

	private _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__MultiWhitespaceRegex_0()
	{
		pattern = "\\s+";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFA27619846C9082228409DDE84AD3B67A028F134D3CCD2BA48546FA8F37666AFC__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
