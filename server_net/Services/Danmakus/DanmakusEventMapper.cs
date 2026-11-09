using System.Text.Json;
using Danmu.Server.Models;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 把 danmakus 采集端上行的事件映射成中心的 <see cref="RecordedDanmakuEvent"/>。
///
/// 采集端上行的 <c>ExtractedUploadEvent</c> 结构：
/// <code>
/// {
///   localId: number,        // outbox 本地自增 id，ACK 时回传以便客户端标记已发送
///   streamerUid: number,    // 主播 uid（客户端按录制列表/状态解析出来的）
///   eventTsMs: number,      // 客户端收到消息的时间（服务端时间校准过）
///   roomId: number,
///   rulesVersion: number,
///   danmaku: { ...字段由中心下发的规则决定... }
/// }
/// </code>
///
/// 因为提取规则是中心下发的（见 <see cref="DanmakusExtractionRules"/>），
/// <c>danmaku</c> 里的 key 已经与我们的事件模型同名，这里只做类型归一与缺省补全。
/// </summary>
public static class DanmakusEventMapper
{
    /// <summary>
    /// 单条上行事件转换。返回 null 表示这条事件应当丢弃（缺 messageId / 时间戳非法）。
    /// </summary>
    public static RecordedDanmakuEvent? Map(IReadOnlyDictionary<string, object?> danmaku, long eventTsMs)
    {
        var type = GetString(danmaku, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        // 时间戳优先用消息体里的（B 站服务器时间），缺失才回退到客户端收到的时间。
        // 多节点按时间线拼接时，必须用同一个基准，否则两台机器的时钟差会污染时间线。
        var timestamp = GetLong(danmaku, "timestamp") ?? eventTsMs;
        if (timestamp <= 0)
        {
            return null;
        }

        var uid = GetString(danmaku, "uid") ?? "";

        var evt = new RecordedDanmakuEvent
        {
            Type = type,
            Timestamp = timestamp,
            Uid = uid,
            User = GetString(danmaku, "user") ?? "",
            Text = GetString(danmaku, "text"),
            TextJpn = GetString(danmaku, "textJpn"),
            MessageJpn = GetString(danmaku, "textJpn"),
            MessageTrans = GetString(danmaku, "messageTrans"),
            Name = GetString(danmaku, "name"),
            GuardLevel = GetInt(danmaku, "guardLevel"),
            MedalLevel = GetInt(danmaku, "medalLevel"),
            MedalName = GetString(danmaku, "medalName"),
            MedalAnchor = GetString(danmaku, "medalAnchor"),
            MedalRoomId = GetInt(danmaku, "medalRoomId"),
            MedalGuardLevel = GetInt(danmaku, "medalGuardLevel"),
            MedalIsLight = GetBool(danmaku, "medalIsLight"),
            MedalAnchorUid = GetLong(danmaku, "medalAnchorUid"),
            UlLevel = GetInt(danmaku, "ulLevel"),
            WealthLevel = GetInt(danmaku, "wealthLevel"),
            CoinType = GetString(danmaku, "coinType"),
            Duration = GetInt(danmaku, "duration"),
            Face = GetString(danmaku, "face"),
            DmType = GetInt(danmaku, "dmType"),
            RawCommand = GetString(danmaku, "rawCommand")
        };

        // 金额：与 BilibiliRecorder 保持一致，金瓜子 → 元（1000:1）
        if (GetDouble(danmaku, "price") is double price)
        {
            evt.Price = NormalizeGoldSeeds(price);
        }
        if (GetDouble(danmaku, "totalCoin") is double totalCoin)
        {
            evt.TotalCoin = NormalizeGoldSeeds(totalCoin);
        }
        if (GetDouble(danmaku, "discountPrice") is double discountPrice)
        {
            evt.DiscountPrice = NormalizeGoldSeeds(discountPrice);
        }

        switch (type)
        {
            case "comment":
                evt.Count = 1;
                evt.IsPriceTotal = false;
                break;
            case "gift":
                evt.Count = Math.Max(1, GetInt(danmaku, "count") ?? 1);
                evt.IsPriceTotal = false;
                break;
            case "super_chat":
                evt.Count = 1;
                // SC 的 price 是一整笔金额（不是单价）
                evt.IsPriceTotal = true;
                break;
            case "guard":
                evt.Count = Math.Max(1, GetInt(danmaku, "count") ?? 1);
                evt.IsPriceTotal = true;
                break;
        }

        evt.MessageId = ResolveMessageId(danmaku, evt);
        return evt;
    }

    /// <summary>
    /// 消息唯一 ID：能取到 B 站原生 ID 就用原生的，取不到（舰长类）按确定性规则构造，
    /// 保证**任何节点、任何重传**算出来的键都一样，唯一索引才能正确去重。
    /// </summary>
    private static string ResolveMessageId(IReadOnlyDictionary<string, object?> danmaku, RecordedDanmakuEvent evt)
    {
        var native = GetString(danmaku, "messageId");
        if (!string.IsNullOrWhiteSpace(native))
        {
            return native;
        }

        // 舰长/提督/总督没有原生 ID：uid + 开通时间 + 等级 三元组在时间上是唯一的
        if (evt.Type == "guard")
        {
            var prefix = evt.RawCommand?.Contains("TOAST", StringComparison.OrdinalIgnoreCase) == true
                ? "toast-"
                : "guard-";
            return $"{prefix}{evt.Uid}-{evt.Timestamp}-{evt.GuardLevel}";
        }

        // 其他类型理论上都能取到原生 ID；兜底用内容指纹，至少保证幂等。
        return BuildFallbackMessageId(evt);
    }

    /// <summary>
    /// 内容指纹兜底：**必须与 <see cref="BilibiliRecorder.BuildFallbackMessageId"/> 完全一致**，
    /// 否则同一台机器录的同一场直播，走本机录制和走采集节点上报会算出不同的键，
    /// 唯一索引就挡不住重复。
    ///
    /// 算法：<c>fp- + SHA256("{uid}|{timestamp}|{text}") 前 12 字节</c>。
    /// </summary>
    private static string BuildFallbackMessageId(RecordedDanmakuEvent evt)
        => BilibiliRecorder.BuildFallbackMessageId(evt.Uid, evt.Timestamp, evt.Text);

    internal static double NormalizeGoldSeeds(double rawPrice)
        => rawPrice <= 0 ? 0 : rawPrice / 1000.0;

    // ─── msgpack 值的宽松读取 ─────────────────────────────────────
    // 客户端用 useBigInt64 编码，超出 int32 的整数会落到 long/ulong/BigInteger，
    // 这里统一收口，避免每个字段都写一遍类型分支。

    public static string? GetString(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            string s => s,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static long? GetLong(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null) return null;
        try
        {
            return value switch
            {
                long l => l,
                int i => i,
                uint ui => ui,
                ulong ul => ul <= long.MaxValue ? (long)ul : null,
                short s => s,
                byte b => b,
                double d => (long)d,
                float f => (long)f,
                decimal m => (long)m,
                System.Numerics.BigInteger bi => (long)bi,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static int? GetInt(IReadOnlyDictionary<string, object?> map, string key)    {
        var value = GetLong(map, key);
        if (value is null) return null;
        return value.Value > int.MaxValue ? int.MaxValue
            : value.Value < int.MinValue ? int.MinValue
            : (int)value.Value;
    }

    public static double? GetDouble(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            ulong ul => ul,
            decimal m => (double)m,
            System.Numerics.BigInteger bi => (double)bi,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>数组元素转 long（客户端 msgpack 的 connectedRooms 可能是 int/long/BigInteger）。</summary>
    public static long? ConvertToLong(object? value)
    {
        if (value is null) return null;
        try
        {
            return value switch
            {
                long l => l,
                int i => i,
                uint ui => ui,
                ulong ul => ul <= long.MaxValue ? (long)ul : null,
                short s => s,
                byte b => b,
                double d => (long)d,
                float f => (long)f,
                decimal m => (long)m,
                System.Numerics.BigInteger bi => (long)bi,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// bool 字段：提取规则里 <c>as: "bool"</c> 已经归一过，
    /// 但 B 站原始值可能是 0/1 数字，这里再兜一层。
    /// </summary>
    public static bool? GetBool(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            _ => null
        };
    }
}
