using System.Text.Json;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 向采集端下发的字段提取规则。
///
/// danmakus-core 的设计是：**协议解析与字段提取分离**。它只负责把 B 站的原始 JSON 消息
/// 解出来，具体"一条弹幕要取哪些字段、字段叫什么名字"由服务端在 WS 鉴权响应里下发
/// （<c>extractionRules</c>）。DanmakuExtractor 按这些规则在采集端做提取，然后上行 msgpack 事件。
///
/// 这正好让我们能用**中心的 <see cref="Models.RecordedDanmakuEvent"/> 字段名**作为提取目标：
/// 采集端传上来的 payload key 与中心模型一一对应，入库时直接映射，无需在采集端做任何改动。
///
/// 规则语法（danmakus-core/src/core/DanmakuExtractionTypes.ts）：
/// - <c>fields[].path</c>：取值路径，<c>a.b.0 | c.d</c> 表示按顺序取第一个非空值
/// - <c>fields[].as</c>：string / int / float / bool / hex32 / timestamp
/// - <c>consts</c>：不取自消息的固定值（如 type = "comment"）
/// - <c>target</c>：danmaku（普通弹幕）/ runtimeDelta / lifecycleSignal
///
/// 约定：<c>messageId</c> 能直接从 B 站消息取到的都在这里取；舰长类没有原生 ID 的，
/// 由中心按 <c>{uid}-{开播时间}-{等级}</c> 确定性构造（见 <see cref="DanmakusEventMapper"/>）。
/// </summary>
public static class DanmakusExtractionRules
{
    /// <summary>规则版本号。中心改了字段映射就递增，客户端按版本整体替换规则。</summary>
    public const int Version = 1;

    /// <summary>
    /// 构造下发的规则集合（序列化后即客户端期望的 DanmakuExtractionRuleSet）。
    /// </summary>
    public static object Build() => new
    {
        version = Version,
        protobufs = new object[]
        {
            new { name = "bilibili.live.SendGiftMsg", proto = SendGiftV2Proto }
        },
        rules = new object[]
        {
            // ── 普通弹幕 ──────────────────────────────────────
            // info[0][4]  = 服务器时间戳（秒）
            // info[1]     = 弹幕文本
            // info[2][0]  = 用户 uid，info[2][1] = 用户名
            // info[0][15].extra.id_str = B 站弹幕唯一 ID（跨节点去重键）
            // info[3]     = 粉丝牌 [等级, 名称, 主播名, 房间号, ..., 10=guard, 11=is_light, 12=anchor_uid]
            // info[4][0]  = 用户等级，info[16][0] = 财富等级
            // info[0][12] = dm_type（0 文本 / 1 表情）
            Rule(@"^DANMU_MSG(:.*)?$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "comment" },
                fields: new Dictionary<string, object>
                {
                    ["messageId"] = Field("info.0.15.extra.id_str", "string"),
                    ["timestamp"] = Field("info.0.4", "timestamp"),
                    ["text"] = Field("info.1", "string"),
                    ["uid"] = Field("info.2.0", "string"),
                    ["user"] = Field("info.2.1", "string"),
                    ["guardLevel"] = Field("info.7", "int"),
                    ["medalLevel"] = Field("info.3.0", "int"),
                    ["medalName"] = Field("info.3.1", "string"),
                    ["medalAnchor"] = Field("info.3.2", "string"),
                    ["medalRoomId"] = Field("info.3.3", "int"),
                    ["medalGuardLevel"] = Field("info.3.10", "int"),
                    ["medalIsLight"] = Field("info.3.11", "bool"),
                    ["medalAnchorUid"] = Field("info.3.12", "string"),
                    ["ulLevel"] = Field("info.4.0", "int"),
                    ["wealthLevel"] = Field("info.16.0", "int"),
                    ["dmType"] = Field("info.0.12", "int"),
                    ["rawCommand"] = Field("cmd", "string")
                }),

            // ── 礼物（旧版 JSON）──────────────────────────────
            // data.tid / data.rnd = 礼物消息唯一 ID（同一笔礼物的两种等价键）
            // price / total_coin / discount_price 都是金瓜子，原样上传，中心按 1000:1 转元
            Rule("^SEND_GIFT$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "gift" },
                fields: new Dictionary<string, object>
                {
                    ["messageId"] = Field("data.tid|data.rnd", "string"),
                    ["timestamp"] = Field("data.timestamp", "timestamp"),
                    ["name"] = Field("data.giftName|data.gift_name", "string"),
                    ["count"] = Field("data.num", "int"),
                    ["price"] = Field("data.price", "float"),
                    ["totalCoin"] = Field("data.total_coin", "float"),
                    ["discountPrice"] = Field("data.discount_price", "float"),
                    ["coinType"] = Field("data.coin_type", "string"),
                    ["guardLevel"] = Field("data.guard_level", "int"),
                    ["user"] = Field("data.uname", "string"),
                    ["uid"] = Field("data.uid", "string"),
                    ["face"] = Field("data.face", "string"),
                    ["rawCommand"] = Field("cmd", "string")
                }),

            // ── 礼物（新版 protobuf）──────────────────────────
            // data.pb 是 base64 的 SendGiftMsg。gift_items 是 repeated，
            // 提取器只能取第一个匹配值，故按"首件"提取；实测 B 站一条消息只带一件，
            // 数量由 num 表示（连击走 COMBO_SEND，中心侧已不记录）。
            Rule("^SEND_GIFT_V2$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "gift" },
                fields: new Dictionary<string, object>
                {
                    ["messageId"] = Field("gift_items.0.tid", "string"),
                    ["timestamp"] = Field("gift_items.0.timestamp", "timestamp"),
                    ["name"] = Field("gift_items.0.gift_name", "string"),
                    ["count"] = Field("gift_items.0.num", "int"),
                    ["price"] = Field("gift_items.0.price", "float"),
                    ["totalCoin"] = Field("gift_items.0.total_coin", "float"),
                    ["coinType"] = Field("gift_items.0.coin_type", "string"),
                    ["guardLevel"] = Field("guard_level", "int"),
                    ["user"] = Field("uname", "string"),
                    ["uid"] = Field("uid", "string"),
                    ["face"] = Field("face", "string"),
                    ["rawCommand"] = Field("cmd", "string")
                },
                proto: "bilibili.live.SendGiftMsg",
                protoSource: "data.pb"),

            // ── 醒目留言 ──────────────────────────────────────
            // SUPER_CHAT_MESSAGE 与 _JPN 是同一笔 SC 的两种下发，data.id 相同；
            // 中心侧按 messageId 归一化去重（双语 SC 只入库一条）。
            Rule("^SUPER_CHAT_MESSAGE(_JPN)?$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "super_chat" },
                fields: new Dictionary<string, object>
                {
                    ["messageId"] = Field("data.id", "string"),
                    ["timestamp"] = Field("data.ts", "timestamp"),
                    ["price"] = Field("data.price", "float"),
                    ["text"] = Field("data.message", "string"),
                    ["textJpn"] = Field("data.message_jpn", "string"),
                    ["messageTrans"] = Field("data.message_trans", "string"),
                    ["duration"] = Field("data.time", "int"),
                    ["medalLevel"] = Field("data.medal_info.medal_level", "int"),
                    ["medalName"] = Field("data.medal_info.medal_name", "string"),
                    ["medalAnchor"] = Field("data.medal_info.anchor_uname", "string"),
                    ["medalRoomId"] = Field("data.medal_info.anchor_roomid", "int"),
                    ["medalGuardLevel"] = Field("data.medal_info.guard_level", "int"),
                    ["medalIsLight"] = Field("data.medal_info.is_lighted", "bool"),
                    ["medalAnchorUid"] = Field("data.medal_info.target_id", "string"),
                    ["user"] = Field("data.user_info.uname", "string"),
                    ["uid"] = Field("data.user_info.uid", "string"),
                    ["face"] = Field("data.user_info.face", "string"),
                    ["rawCommand"] = Field("cmd", "string")
                }),

            // ── 舰长购买 ──────────────────────────────────────
            // 与 USER_TOAST_MSG 描述同一笔购买；两者 messageId 前缀不同，
            // 中心侧用 guard- / toast- 区分，避免互相吞掉
            // （与 BilibiliRecorder.CreateRecordedEvents 的处理保持一致）。
            Rule("^GUARD_BUY$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "guard" },
                fields: new Dictionary<string, object>
                {
                    ["timestamp"] = Field("data.start_time", "timestamp"),
                    ["name"] = Field("data.gift_name", "string"),
                    ["count"] = Field("data.num", "int"),
                    ["price"] = Field("data.price", "float"),
                    ["guardLevel"] = Field("data.guard_level", "int"),
                    ["user"] = Field("data.username", "string"),
                    ["uid"] = Field("data.uid", "string"),
                    ["face"] = Field("data.face", "string"),
                    ["rawCommand"] = Field("cmd", "string")
                }),

            Rule("^USER_TOAST_MSG$", "danmaku",
                consts: new Dictionary<string, object> { ["type"] = "guard" },
                fields: new Dictionary<string, object>
                {
                    ["timestamp"] = Field("data.start_time", "timestamp"),
                    ["name"] = Field("data.role_name", "string"),
                    ["count"] = Field("data.num", "int"),
                    ["price"] = Field("data.price", "float"),
                    ["guardLevel"] = Field("data.guard_level", "int"),
                    ["user"] = Field("data.username", "string"),
                    ["uid"] = Field("data.uid", "string"),
                    ["text"] = Field("data.toast_msg", "string"),
                    ["rawCommand"] = Field("cmd", "string")
                })
        }
    };

    private static object Rule(
        string pattern,
        string target,
        Dictionary<string, object> consts,
        Dictionary<string, object> fields)
        => new { pattern, target, consts, fields };

    private static object Rule(
        string pattern,
        string target,
        Dictionary<string, object> consts,
        Dictionary<string, object> fields,
        string proto,
        string protoSource)
        => new { pattern, target, consts, fields, proto, protoSource };

    private static object Field(string path, string @as) => new { path, @as = @as };

    /// <summary>
    /// SendGiftMsg 的 proto 定义（来自 blivedm-go/pb/events.proto 的 EventSendGift，
    /// 字段号已与 blivedm-go / biliLive-tools / bilidanmu 三处交叉验证）。
    /// 只用到了根上的 uid/uname/face/guard_level 和 repeated gift_items。
    /// </summary>
    private const string SendGiftV2Proto = """
        syntax = "proto3";
        package bilibili.live;

        message SendGiftMsg {
          uint64 uid = 1;
          string uname = 2;
          string face = 3;
          uint64 guard_level = 5;
          repeated GiftItem gift_items = 10;
        }

        message GiftItem {
          uint32 gift_id = 1;
          string gift_name = 2;
          uint32 num = 3;
          uint64 price = 5;
          uint64 total_coin = 7;
          string coin_type = 8;
          string tid = 9;
          uint64 timestamp = 10;
          string action = 18;
        }
        """;

    /// <summary>序列化成客户端期望的 JSON（供 WS 鉴权响应使用）。</summary>
    public static string BuildJson(JsonSerializerOptions options)
        => JsonSerializer.Serialize(Build(), options);
}
