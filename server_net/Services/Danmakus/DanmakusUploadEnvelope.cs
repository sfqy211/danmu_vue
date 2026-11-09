using System.IO.Compression;
using MessagePack;
using ZstdSharp;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// CLIENT_UPLOAD_DANMAKU 帧体解包。
///
/// 客户端（danmakus-core/DanmakuUploadCodec.ts）先对事件批次做 MessagePack 编码，
/// 再尝试 zstd → brotli → gzip 压缩（浏览器/运行时支持哪个用哪个，全不支持则 identity），
/// 外层仍然是一层 MessagePack：<c>{ compression: "zstd"|"brotli"|"gzip"|"identity", payload: bytes }</c>。
///
/// 解包必须按这个顺序反向做，否则拿到的只是压缩后的字节流。
/// </summary>
public static class DanmakusUploadEnvelope
{
    /// <summary>解开外层的 compression/payload 信封，返回内层的 msgpack 字节。</summary>
    public static byte[] Unwrap(byte[] body)
    {
        var envelope = MessagePackSerializer.Deserialize<Dictionary<string, object?>>(body);
        if (envelope is null)
        {
            throw new InvalidDataException("上传信封为空");
        }

        var compression = GetString(envelope, "compression") ?? "identity";
        if (!envelope.TryGetValue("payload", out var payloadValue) || payloadValue is not byte[] compressed)
        {
            throw new InvalidDataException("上传信封缺少 payload");
        }

        return compression switch
        {
            "identity" => compressed,
            "gzip" => GunzipDecompress(compressed),
            "brotli" => BrotliDecompress(compressed),
            // zstd 放在最后：Bun/Node 默认支持 zstd，实际几乎总是这条分支
            "zstd" => ZstdDecompress(compressed),
            _ => throw new InvalidDataException($"不支持的压缩格式: {compression}")
        };
    }

    private static string? GetString(Dictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value as string : null;

    private static byte[] GunzipDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] BrotliDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] ZstdDecompress(byte[] data)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(data).ToArray();
    }
}
