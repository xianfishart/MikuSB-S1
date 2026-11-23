using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class SpineStrData
{
	[JsonPropertyName("ns")]
	public int Ns { get; set; } = 0;

	[JsonPropertyName("tbn")]
	public List<int> Tbn { get; set; }

	[JsonPropertyName("tbr")]
	public List<int> Tbr { get; set; }

	public SpineStrData()
	{
		int num = 2;
		List<int> list = new List<int>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<int> span = CollectionsMarshal.AsSpan(list);
		span[0] = 0;
		span[1] = 0;
		Tbn = list;
		Tbr = new List<int>();
	}
}
