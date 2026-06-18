# ADR-2: 增加礼物实付价格字段与监听 USER_TOAST_MSG

> 阶段：第二阶段
> 前置：ADR-1（双语 SC 修复）
> 状态：已实施
> 日期：2026-06-19

## 背景

当前礼物和舰长价格记录存在以下问题：

### 问题 1：SEND_GIFT 用原价而非实付价

B站 `SEND_GIFT` 事件包含多个价格字段：

| 字段             | 含义                   | 当前是否提取             |
| ---------------- | ---------------------- | ------------------------ |
| `price`          | 礼物原价单价（金瓜子） | 是（当前唯一使用的字段） |
| `discount_price` | 折扣价（金瓜子）       | 否                       |
| `total_coin`     | 实际支付总金瓜子数     | 否                       |

当前代码用 `price × num` 计算总价值，但实际支付可能因折扣、活动等与原价不同。"D:\AHEU\code\reference"：API 文档明确说明 `total_coin` 不总等于 `num × price`。

### 问题 2：GUARD_BUY 用原价而非实付价

B站有两个舰长相关事件：

| 事件             | `price` 含义                            | 其他信息                                 |
| ---------------- | --------------------------------------- | ---------------------------------------- |
| `GUARD_BUY`      | **原金瓜子标价**（如 198000 = 198元）   | 无实付价、无庆祝文本                     |
| `USER_TOAST_MSG` | **实际金瓜子标价**（如 138000 = 138元） | 有庆祝文本 `toast_msg`、身份 `role_name` |

当前只监听 `GUARD_BUY`，拿到的是原价，无法反映实际支付金额。

### 问题 3：GUARD_BUY 缺少头像

`GUARD_BUY` 和 `USER_TOAST_MSG` 事件本身都不包含 `face` 字段，当前代码 `TryGetString(data, "face")` 始终返回 null。

## 改动方案

### 1. SEND_GIFT：新增 total_coin 和 discount_price

**文件**：`BilibiliRecorder.cs` + `DanmakuModels.cs`

`RecordedDanmakuEvent` 新增字段：

```csharp
public double? TotalCoin { get; set; }       // 实际支付总金瓜子数（转为元）
public double? DiscountPrice { get; set; }    // 折扣单价金瓜子数（转为元）
```

`CreateRecordedEvent` 中 `SEND_GIFT` 分支修改：

```csharp
if (cmd == "SEND_GIFT")
{
    var data = root.GetProperty("data");
    var count = TryGetInt32(data, "num") ?? 1;
    var priceRaw = TryGetDouble(data, "price") ?? 0;
    var totalCoinRaw = TryGetDouble(data, "total_coin");
    var discountPriceRaw = TryGetDouble(data, "discount_price");
    var coinType = TryGetString(data, "coin_type");

    return new RecordedDanmakuEvent
    {
        Type = "gift",
        Timestamp = (TryGetInt64(data, "timestamp") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
        Name = TryGetString(data, "giftName") ?? TryGetString(data, "gift_name"),
        Count = count > 0 ? count : 1,
        Price = NormalizeGoldSeeds(priceRaw),                          // 原价（保留）
        TotalCoin = totalCoinRaw.HasValue ? NormalizeGoldSeeds(totalCoinRaw.Value) : null,  // 新增
        DiscountPrice = discountPriceRaw.HasValue ? NormalizeGoldSeeds(discountPriceRaw.Value) : null, // 新增
        IsPriceTotal = false,
        CoinType = coinType,
        // ... 其余字段不变
    };
}
```

### 2. COMBO_SEND：新增 total_coin

`COMBO_SEND` 事件也有 `combo_total_coin` 字段，当前已部分使用，改为也存入 `TotalCoin`：

```csharp
if (cmd == "COMBO_SEND")
{
    var data = root.GetProperty("data");
    var comboTotalCoin = TryGetDouble(data, "combo_total_coin");
    // ... 现有逻辑
    TotalCoin = normalizedPrice,  // combo 的 total_coin 已是总价值
}
```

### 3. 新增 USER_TOAST_MSG 监听

**文件**：`BilibiliRecorder.cs`

在 `CreateRecordedEvent` 中新增分支：

```csharp
if (cmd == "USER_TOAST_MSG")
{
    var data = root.GetProperty("data");
    return new RecordedDanmakuEvent
    {
        Type = "guard",
        Timestamp = (TryGetInt64(data, "start_time") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
        Name = TryGetString(data, "role_name") ?? "guard",
        Count = Math.Max(1, TryGetInt32(data, "num") ?? 1),
        Price = NormalizeGoldSeeds(TryGetDouble(data, "price") ?? 0),  // 实付价
        IsPriceTotal = true,
        GuardLevel = TryGetInt32(data, "guard_level"),
        User = TryGetString(data, "username") ?? "",
        Uid = TryGetString(data, "uid") ?? "",
        Text = TryGetString(data, "toast_msg"),  // 庆祝文本
        RawCommand = cmd
    };
}
```

### 4. GUARD_BUY 与 USER_TOAST_MSG 的关系

两者都会触发，但含义不同：

| 事件             | 时机           | price 含义 |
| ---------------- | -------------- | ---------- |
| `GUARD_BUY`      | 购买瞬间       | 原价       |
| `USER_TOAST_MSG` | 庆祝消息展示时 | 实付价     |

**处理策略**：两个事件都存储，但通过 `RawCommand` 字段区分。统计实付金额时优先使用 `USER_TOAST_MSG` 的 `price`。

后续如需去重，可在查询时按 `uid + guard_level + start_time` 合并，取 `USER_TOAST_MSG` 的价格。

### 5. 统计逻辑更新

**文件**：`DanmakuService.cs`

统计礼物金额时，优先使用 `TotalCoin`（实付总价），回退到 `Price × Count`：

```csharp
// 计算单条礼物的实际金额
var actualPrice = event.TotalCoin ?? (event.Price * (event.IsPriceTotal ? 1 : event.Count));
```

舰长统计优先使用 `USER_TOAST_MSG` 的价格：

```csharp
// 舰长金额：优先取 USER_TOAST_MSG 的实付价
if (event.RawCommand == "USER_TOAST_MSG")
{
    actualPrice = event.Price;  // 已是实付价
}
```

## 改动文件清单

| 文件                  | 改动                                                                                        |
| --------------------- | ------------------------------------------------------------------------------------------- |
| `BilibiliRecorder.cs` | `SEND_GIFT` 提取 `total_coin`/`discount_price`；新增 `USER_TOAST_MSG` 分支                  |
| `DanmakuModels.cs`    | `RecordedDanmakuEvent` 新增 `TotalCoin`、`DiscountPrice`；`DanmakuMessage` 新增 `TotalCoin` |
| `DanmakuService.cs`   | 统计逻辑优先使用 `TotalCoin`；舰长统计去重；API 返回新增 `totalCoin` 字段                   |

## 实施记录

### Gap 1：COMBO_SEND 无需额外 TotalCoin

COMBO_SEND 已将 `combo_total_coin` 归一化后存入 `Price`（`IsPriceTotal = true`），无需额外存入 `TotalCoin`。

### Gap 2：DanmakuMessage 也需要 TotalCoin

原方案只提 `RecordedDanmakuEvent` 新增字段，但统计逻辑在 `DanmakuMessage` 上运行，`DanmakuMessage` 也需要 `TotalCoin` 字段。已补充。

### Gap 3：舰长去重策略细化

原方案说"统计时去重"但未给具体策略。实施时采用：同一用户若同时有 `GUARD_BUY` 和 `USER_TOAST_MSG`，统计时跳过 `GUARD_BUY`，只计 `USER_TOAST_MSG` 的实付价。

### Gap 4：ToRecordedEvent 反向映射

`ToRecordedEvent`（DanmakuMessage → RecordedDanmakuEvent）也需映射 `TotalCoin`，否则写入 Redis 时会丢失该字段。已补充。

## 验证方式

1. 模拟 `SEND_GIFT` 事件，确认 `total_coin` 和 `discount_price` 被正确提取和转换
2. 模拟 `USER_TOAST_MSG` 事件，确认实付价被正确记录
3. 模拟 `GUARD_BUY` + `USER_TOAST_MSG` 同时触发，确认两条记录都存在且 `RawCommand` 不同
4. 统计页面金额与实际一致（使用实付价而非原价）

## 风险

| 风险                                       | 缓解                                                     |
| ------------------------------------------ | -------------------------------------------------------- |
| `USER_TOAST_MSG` 可能延迟或丢失            | 保留 `GUARD_BUY` 作为兜底，统计时优先取 `USER_TOAST_MSG` |
| 旧数据没有 `total_coin` 字段               | 回退到 `price × count`，兼容旧数据                       |
| 舰长事件重复（GUARD_BUY + USER_TOAST_MSG） | 通过 `RawCommand` 区分，统计时去重                       |
