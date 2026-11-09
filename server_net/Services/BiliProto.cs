using System.Text;

namespace Danmu.Server.Services;

/// <summary>
/// 极简 protobuf wire-format 解码器，仅用于解析 B 站 SEND_GIFT_V2 的 data.pb 载荷
/// （礼物消息已从明文 JSON 迁移为 base64 protobuf，参考 bilidanmu / JLiverTool /
/// sjh8130/bili_danmaku 社区逆向的 SEND_GIFT_V2.proto）：
/// root{1:uid, 2:uname, 3:face, 5:guard_level, 8:medal_info, 10:repeated gift_item}，
/// gift_item{1:gift_id, 2:gift_name, 3:num, 5:price, 9:tid, 10:timestamp, 18:action}。
/// 不引入完整 protobuf 依赖，只支持本场景需要的 varint / length-delimited 读取。
/// </summary>
internal sealed class BiliProtoMessage
{
    private const int WireTypeVarint = 0;
    private const int WireTypeFixed64 = 1;
    private const int WireTypeLengthDelimited = 2;
    private const int WireTypeFixed32 = 5;

    private readonly Dictionary<int, List<BiliProtoValue>> _fields;

    private BiliProtoMessage(Dictionary<int, List<BiliProtoValue>> fields)
    {
        _fields = fields;
    }

    public static BiliProtoMessage Decode(byte[] data)
    {
        var fields = new Dictionary<int, List<BiliProtoValue>>();
        var offset = 0;
        while (offset < data.Length)
        {
            if (!TryReadVarint(data, ref offset, out var key))
            {
                break;
            }

            var fieldNumber = (int)(key >> 3);
            var wireType = (int)(key & 0x7);
            if (fieldNumber <= 0)
            {
                break; // 载荷异常，保留已解析部分
            }

            BiliProtoValue value;
            switch (wireType)
            {
                case WireTypeVarint:
                    if (!TryReadVarint(data, ref offset, out var varint))
                    {
                        return new BiliProtoMessage(fields);
                    }
                    value = new BiliProtoValue(WireTypeVarint, varint, null);
                    break;
                case WireTypeFixed64:
                    if (data.Length - offset < 8)
                    {
                        return new BiliProtoMessage(fields);
                    }
                    value = new BiliProtoValue(WireTypeFixed64, 0, data.AsSpan(offset, 8).ToArray());
                    offset += 8;
                    break;
                case WireTypeLengthDelimited:
                    if (!TryReadVarint(data, ref offset, out var length) || length > (ulong)(data.Length - offset))
                    {
                        return new BiliProtoMessage(fields);
                    }
                    var len = (int)length;
                    value = new BiliProtoValue(WireTypeLengthDelimited, 0, data.AsSpan(offset, len).ToArray());
                    offset += len;
                    break;
                case WireTypeFixed32:
                    if (data.Length - offset < 4)
                    {
                        return new BiliProtoMessage(fields);
                    }
                    value = new BiliProtoValue(WireTypeFixed32, 0, data.AsSpan(offset, 4).ToArray());
                    offset += 4;
                    break;
                default:
                    // wire type 3/4（deprecated groups）不在支持范围
                    return new BiliProtoMessage(fields);
            }

            if (!fields.TryGetValue(fieldNumber, out var list))
            {
                list = new List<BiliProtoValue>();
                fields[fieldNumber] = list;
            }
            list.Add(value);
        }

        return new BiliProtoMessage(fields);
    }

    /// <summary>读取 varint 字段的第一个值（repeated varint 场景未用到）。</summary>
    public bool TryGetVarint(int field, out long value)
    {
        value = 0;
        if (!_fields.TryGetValue(field, out var list)) return false;
        foreach (var v in list)
        {
            if (v.WireType == WireTypeVarint)
            {
                value = (long)v.Varint;
                return true;
            }
        }
        return false;
    }

    /// <summary>读取 length-delimited 字段的第一个值（UTF-8 字符串）。</summary>
    public string? GetString(int field)
    {
        if (!_fields.TryGetValue(field, out var list)) return null;
        foreach (var v in list)
        {
            if (v.WireType == WireTypeLengthDelimited && v.Bytes != null)
            {
                return Encoding.UTF8.GetString(v.Bytes);
            }
        }
        return null;
    }

    /// <summary>读取嵌套消息字段（如 repeated gift_item）。</summary>
    public List<BiliProtoMessage> GetMessages(int field)
    {
        var result = new List<BiliProtoMessage>();
        if (_fields.TryGetValue(field, out var list))
        {
            foreach (var v in list)
            {
                if (v.WireType == WireTypeLengthDelimited && v.Bytes != null)
                {
                    result.Add(Decode(v.Bytes));
                }
            }
        }
        return result;
    }

    private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
            if (shift >= 64) return false; // varint 超长，载荷异常
        }
        return false;
    }
}

internal readonly record struct BiliProtoValue(int WireType, ulong Varint, byte[]? Bytes);
