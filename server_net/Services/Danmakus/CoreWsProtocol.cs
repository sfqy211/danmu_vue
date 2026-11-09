using System.Buffers.Binary;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// danmakus-client 的 Core Runtime 二进制帧协议。
///
/// 帧格式（与 danmakus-core/src/core/CoreWebSocketCodec.ts 完全一致）：
/// <code>
/// +--------+-----------------+-------------------+
/// | opcode | seq (uint32 LE) | msgpack payload   |
/// | 1 byte |     4 bytes     |  remainder        |
/// +--------+-----------------+-------------------+
/// </code>
/// payload 是 MessagePack 编码（整数超出 int32 范围时按 int64 编码）。
/// </summary>
public static class CoreWsProtocol
{
    public const int FrameHeaderSize = 5;

    // Client -> Server
    public const byte ClientAuth = 0x01;
    public const byte ClientUploadDanmaku = 0x02;
    public const byte ClientStateReport = 0x03;
    public const byte ClientRequestRooms = 0x04;

    // Server -> Client
    public const byte ServerAuthResult = 0x81;
    public const byte ServerUploadAck = 0x82;
    public const byte ServerPushRoomAssign = 0x83;
    public const byte ServerPushStreamerStatus = 0x84;
    public const byte ServerErrorNotify = 0x8F;

    public readonly record struct Frame(byte OpCode, uint Seq, ReadOnlyMemory<byte> Payload);

    /// <summary>解出帧头；缓冲区不足一帧时返回 false。</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out Frame frame)
    {
        if (data.Length < FrameHeaderSize)
        {
            frame = default;
            return false;
        }

        var opCode = data[0];
        var seq = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1, 4));
        frame = new Frame(opCode, seq, data.Slice(FrameHeaderSize).ToArray());
        return true;
    }

    /// <summary>编码一个帧；payload 为空时只发 5 字节帧头（客户端用它做轻量心跳）。</summary>
    public static byte[] Encode(byte opCode, uint seq, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[FrameHeaderSize + payload.Length];
        frame[0] = opCode;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(1, 4), seq);
        payload.CopyTo(frame.AsSpan(FrameHeaderSize));
        return frame;
    }

    public static byte[] Encode(byte opCode, uint seq, byte[]? payload)
        => Encode(opCode, seq, payload is null ? ReadOnlySpan<byte>.Empty : payload.AsSpan());
}
