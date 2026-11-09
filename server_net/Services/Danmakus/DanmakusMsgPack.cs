using System.Collections;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// MessagePack 反序列化的容器类型归一。
///
/// 用 <c>Dictionary&lt;string, object?&gt;</c> 接收时，**只有最外层**是那个类型；
/// 嵌套的 map 会被反序列化成 <c>Dictionary&lt;object, object&gt;</c>，数组则是 <c>object[]</c>。
/// 直接写 <c>value is Dictionary&lt;string, object?&gt;</c> 判断会让所有嵌套内容被静默跳过 ——
/// 表现就是"上报成功但一条都没入库"。这里统一按 IDictionary/IList 的接口做宽松转换。
/// </summary>
public static class DanmakusMsgPack
{
    /// <summary>把任意 msgpack map 转成 string 键的只读字典。</summary>
    public static bool TryAsMap(object? value, out IReadOnlyDictionary<string, object?> map)
    {
        map = EmptyMap;

        if (value is null) return false;

        if (value is IReadOnlyDictionary<string, object?> already)
        {
            map = already;
            return true;
        }

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString();
                if (key is null) continue;
                result[key] = entry.Value;
            }
            map = result;
            return true;
        }

        return false;
    }

    /// <summary>把任意 msgpack array 转成 object 列表。</summary>
    public static bool TryAsList(object? value, out IReadOnlyList<object?> list)
    {
        switch (value)
        {
            case null:
                list = Array.Empty<object?>();
                return false;
            case IReadOnlyList<object?> already:
                list = already;
                return true;
            case IList sequence:
                var result = new List<object?>(sequence.Count);
                foreach (var item in sequence)
                {
                    result.Add(item);
                }
                list = result;
                return true;
            default:
                list = Array.Empty<object?>();
                return false;
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyMap =
        new Dictionary<string, object?>();
}
