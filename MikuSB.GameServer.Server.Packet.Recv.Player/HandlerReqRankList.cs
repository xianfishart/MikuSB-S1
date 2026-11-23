using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using MikuSB.Enums.Packet;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Recv.Player;

/// <summary>
/// 排行榜请求（opcode 51 → 52 RspRankList）—— 第 442 节补上「冬日接力战」的本服单机榜。
///
/// 客户端 <c>WorldBossLogic.GetRankList(nReferRank1)</c> 发的 rank_name 是 <c>WorldBoss_Rank_1001</c>；
/// 拿到回包后在 UI 里做的是：
/// <code>
///   self.tbLineupRankInfo = { { sLineup = e.MemberName, Rank = i, Score = e.Score, sInfo = e.Info }, ... }
/// </code>
/// 所以每一条里除 Score / Info 外，还要把「阵容标识串」塞进 MemberName（协议字段 5）。
/// 本地 MikuProto.dll 的 RankInfo 只有 score/rank/sum/info 四个字段，没法直接赋值，
/// 这里按 protobuf 线格式手工拼字节（未知字段 5 客户端能正常解析，不认也只是忽略）。
/// </summary>
[Opcode(51)]
public class HandlerReqRankList : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		string rankName = ParseFirstString(data) ?? string.Empty;
		if (WorldBossRankService.IsWorldBossRank(rankName))
		{
			List<WorldBossRankEntry> entries = WorldBossRankService.Top(rankName, WorldBossRankService.DefaultMax);
			byte[] body = BuildRankBody(rankName, entries);
			PacketFraming framing = connection.Framing;
			if (framing == PacketFraming.Unknown)
			{
				framing = PacketFraming.FourByteLittleEndianLength;
			}
			BasePacket packet = new BasePacket(52, body, framing);
			await connection.SendPacket(packet, seqNo);
			return;
		}
		RankList response = new RankList
		{
			RankName = rankName
		};
		await connection.SendPacket(52, response, 0);
	}

	private static byte[] BuildRankBody(string rankName, IReadOnlyList<WorldBossRankEntry> entries)
	{
		using MemoryStream stream = new MemoryStream();
		using (CodedOutputStream output = new CodedOutputStream(stream, leaveOpen: true))
		{
			WriteString(output, 1, rankName);
			uint rank = 1u;
			foreach (WorldBossRankEntry entry in entries)
			{
				byte[] payload = BuildEntry(entry, rank);
				rank++;
				output.WriteTag(2, WireFormat.WireType.LengthDelimited);
				output.WriteBytes(ByteString.CopyFrom(payload));
			}
		}
		return stream.ToArray();
	}

	private static byte[] BuildEntry(WorldBossRankEntry entry, uint rank)
	{
		using MemoryStream stream = new MemoryStream();
		using (CodedOutputStream output = new CodedOutputStream(stream, leaveOpen: true))
		{
			// 客户端 RankList.list 的元素类型是 ListItem，字段是：
			//   1 member_name（阵容标识串）  2 score（分数）  3 info（玩家名）
			// 排名不占字段，客户端按列表下标自己填（UpdateLineupRankList 里 e.Rank = i）。
			if (!string.IsNullOrEmpty(entry.Lineup))
			{
				WriteString(output, 1, entry.Lineup);
			}
			output.WriteTag(2, WireFormat.WireType.Varint);
			output.WriteUInt32(entry.Score);
			if (!string.IsNullOrEmpty(entry.Name))
			{
				WriteString(output, 3, entry.Name);
			}
		}
		return stream.ToArray();
	}

	private static void WriteString(CodedOutputStream output, int fieldNumber, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
		output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
		output.WriteBytes(ByteString.CopyFrom(bytes));
	}

	private static string? ParseFirstString(byte[] data)
	{
		if (data.Length == 0)
		{
			return null;
		}
		try
		{
			CodedInputStream codedInputStream = new CodedInputStream(data);
			while (!codedInputStream.IsAtEnd)
			{
				uint num = codedInputStream.ReadTag();
				if (num == 0)
				{
					break;
				}
				if (WireFormat.GetTagWireType(num) == WireFormat.WireType.LengthDelimited)
				{
					return codedInputStream.ReadString();
				}
				codedInputStream.SkipLastField();
			}
		}
		catch
		{
		}
		try
		{
			return Encoding.UTF8.GetString(data).Trim(new char[5] { '\0', ' ', '\r', '\n', '\t' });
		}
		catch
		{
			return null;
		}
	}
}
