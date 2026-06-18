# ADR-1: 修复双语 SC 重复存储 Bug

> 阶段：第一阶段
> 状态：已实施
> 日期：2026-06-19

## 问题描述

当前录制器在处理 B站直播 SC 消息时，`SUPER_CHAT_MESSAGE` 和 `SUPER_CHAT_MESSAGE_JPN` 都会创建独立的事件记录，导致同一条 SC 被存储两次。

根据 B站 API 文档和参考项目"D:\AHEU\code\reference"（blivedm）：

- `SUPER_CHAT_MESSAGE` 已包含 `message`（原文）、`message_jpn`（日语翻译）、`message_trans`（机翻）字段
- `SUPER_CHAT_MESSAGE_JPN` 是同一 SC 的重复推送，仅多了 `message_jpn` 字段，无新增信息
- blivedm 的做法：只处理 `SUPER_CHAT_MESSAGE`，忽略 `SUPER_CHAT_MESSAGE_JPN`

## 当前行为

```
SUPER_CHAT_MESSAGE       → 创建事件，text=message，text_jpn=null
SUPER_CHAT_MESSAGE_JPN   → 创建事件，text=message_jpn，text_jpn=null  ← 重复！
```

查询时通过 `MergeBilingualSuperChats` 方法尝试合并两条记录，但逻辑复杂且不可靠。

## 修复方案

### 1. 录制端：跳过 JPN 事件，提取翻译字段

**文件**：`server_net/Services/BilibiliRecorder.cs`

修改 `CreateRecordedEvent` 方法：

```csharp
// 修改前：cmd.StartsWith("SUPER_CHAT_MESSAGE") 匹配两种事件
if (cmd.StartsWith("SUPER_CHAT_MESSAGE", StringComparison.Ordinal))

// 修改后：精确匹配，跳过 JPN
if (cmd == "SUPER_CHAT_MESSAGE_JPN")
{
    return null; // 跳过，信息已包含在 SUPER_CHAT_MESSAGE 中
}
if (cmd == "SUPER_CHAT_MESSAGE")
```

同时在 `SUPER_CHAT_MESSAGE` 处理中提取 `message_trans`：

```csharp
MessageJpn = TryGetString(data, "message_jpn"),   // 已有
MessageTrans = TryGetString(data, "message_trans"), // 新增
```

### 2. 模型：新增 MessageTrans 字段

**文件**：`server_net/Models/DanmakuModels.cs`（或 RecordedDanmakuEvent 所在文件）

```csharp
public string? MessageTrans { get; set; }  // 机翻文本
```

### 3. 查询端：删除合并逻辑

**文件**：`server_net/Services/DanmakuService.cs`

- 删除 `MergeBilingualSuperChats` 方法
- 删除 `ParseRecordedEventLines` 中对双语 SC 的合并代码
- SC 的 `text_jpn` 字段直接从事件记录中读取，不再需要查询时合并

### 4. 前端：无需改动

API 返回的数据结构不变，`text_jpn` 字段始终有值（不再需要前端合并）。

## 改动文件清单

| 文件                  | 改动                                                |
| --------------------- | --------------------------------------------------- |
| `BilibiliRecorder.cs` | 跳过 `SUPER_CHAT_MESSAGE_JPN`；提取 `message_trans` |
| `DanmakuModels.cs`    | `RecordedDanmakuEvent` 新增 `MessageTrans` 属性     |
| `DanmakuService.cs`   | 删除 `MergeBilingualSuperChats` 及相关合并逻辑      |

## 实施记录

实施时验证发现 ADR 原方案存在两处 gap，已一并修复：

### Gap 1（关键）：`MapRecordedEvent` 映射缺失

原方案称"SC 的 `text_jpn` 字段直接从事件记录中读取"，但 `MapRecordedEvent` 读取的是 `recordedEvent.TextJpn`（录制时从未赋值），而非 `recordedEvent.MessageJpn`（录制时实际赋值的字段）。删除合并逻辑后 `DanmakuMessage.TextJpn` 将恒为 null，前端 `contentJpn` 会丢失。

**修复**：`MapRecordedEvent` 中改为 `TextJpn = recordedEvent.MessageJpn ?? recordedEvent.TextJpn`。

### Gap 2：合并逻辑有两处，原方案只提到一处

原方案只提 `ParseRecordedEventLines`，但 `ParseJsonlFileAsync` 也有相同的 JPN 合并代码，已一并删除。

## 验证方式

1. 模拟收到 `SUPER_CHAT_MESSAGE` 事件，确认只生成一条记录，且 `text_jpn` 和 `message_trans` 都有值
2. 模拟收到 `SUPER_CHAT_MESSAGE_JPN` 事件，确认被跳过（不创建记录）
3. 查询 SC 列表，确认无重复记录
4. 前端 SC 展示正常

## 风险

| 风险                         | 缓解                                                                        |
| ---------------------------- | --------------------------------------------------------------------------- |
| 旧数据中已有重复的 JPN 记录  | 查询端删除合并逻辑后，旧 JPN 记录会作为独立 SC 显示；可在第三阶段迁移时清理 |
| `message_trans` 字段可能为空 | 正常，部分 SC 没有机翻；前端已处理 null                                     |
