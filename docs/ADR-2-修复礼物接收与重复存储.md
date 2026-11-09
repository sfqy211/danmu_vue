# ADR-2: 修复礼物无法接收、双语 SC 重复、礼物重复存储

> 状态：已实施
> 日期：2026-11-09
> 参考项目：`D:\AHEU\code\reference\biliLive-tools`（blive-message-listener）、`D:\AHEU\code\bilidanmu`（自研 Rust 解析）

## 问题与根因

### 1. 礼物信息无法接收

B 站已将礼物消息从明文 JSON 的 `SEND_GIFT` 迁移为 `SEND_GIFT_V2`，载荷是
`data.pb`（base64 protobuf）。录制器只解析 JSON 版 `SEND_GIFT`，新协议房间完全收不到礼物。

**修复**：

- 新增 `server_net/Services/BiliProto.cs`：极简 protobuf wire-format 解码器（仅 varint /
  length-delimited / fixed 字段，不引入完整 protobuf 依赖）。
- `BilibiliRecorder.CreateRecordedEvents` 新增 `SEND_GIFT_V2` 分支。字段定义来自社区逆向
  （参考 bilidanmu / JLiverTool / sjh8130/bili_danmaku）：
  - root：`1:uid, 2:uname, 3:face, 5:guard_level, 8:medal_info, 10:repeated gift_item`
  - gift_item：`1:gift_id, 2:gift_name, 3:num, 5:price, 9:tid, 10:timestamp, 18:action`
- 一条消息可携带多件礼物，逐件产出事件（`CreateRecordedEvent` → `CreateRecordedEvents`
  返回列表），与两个参考项目一致。
- 解析失败时记录 Warning（`Failed to parse SEND_GIFT_V2 payload`），避免静默表现为"收不到"。
- 附带修复：`ReceiveLoopAsync` 原先不处理 WebSocket 分帧（`EndOfMessage`），超过 64KB 的
  压缩弹幕批次会被截断丢弃且字节流错位，导致礼物/SC 批量丢失。现按 `EndOfMessage`
  拼装完整消息后再处理。

### 2. 双语 SC 重复接收

ADR-1 采用"跳过 `SUPER_CHAT_MESSAGE_JPN`"按 cmd 名过滤，不够健壮。

**修复**（参考 bilidanmu 的 `SuperChatDedupWindow`）：

- 两种 cmd 共用同一解析分支；录制器内维护有界去重窗口（512 条 FIFO），
  按归一化 `data.id`（数字/字符串统一转字符串）只放行首条。
- 无论 B 站以何种形态重复下发（主推送 + JPN、同一 cmd 重发）都只存一条。
- 拿不到 id 时退回旧行为：只放行 `SUPER_CHAT_MESSAGE`。
- `text_jpn` 映射顺序由 `message_trans ?? message_jpn` 改为 `message_jpn ?? message_trans`
  （与 bilidanmu 一致：双语 SC 的 `message_jpn` 才是真正的日语，机翻只是兜底），
  涉及 `SessionDbService.WriteBatchAsync` 与 `DanmakuService.MapRecordedEvent`。

### 3. 礼物重复接收

录制器同时记录 `SEND_GIFT`（每笔购买，type=gift）与 `COMBO_SEND`（连击聚合通知，
type=gift_combo），后者 `combo_total_coin` 是累计金额，导致连击礼物在回放列表与礼物
统计中双重计数。

**修复**（两个参考项目均不订阅 `COMBO_SEND`）：不再记录 `COMBO_SEND`。
历史会话中的 `gift_combo` 行仍按原样展示/统计（`FilterDisplayable`、`DisplayableTypesSql`
保持对旧数据的兼容）。

## 验证

- `dotnet build server_net/Danmu.Server.csproj` 通过（0 错误）。
- 合成载荷验证（仓库外临时项目，程序集名 `Danmu.Server.Tests`）：
  - 手工编码 protobuf 载荷 → 2 件礼物逐件解析，名称/数量/价格换算（÷1000）/舰长等级/
    秒级时间戳转毫秒/头像 全部正确；
  - `COMBO_SEND` 产出 0 条事件；
  - 旧 `SEND_GIFT` JSON 协议解析结果不变；
  - SC 主推送（含 `message_jpn`/`message_trans`）与 JPN 变体均正常产出单条事件。

## 风险与备注

| 风险 | 说明 |
| --- | --- |
| V2 时间戳字段语义 | 按秒处理，>1e12 的异常值按毫秒兜底 |
| V2 无 coin_type 字段 | 参照 bilidanmu 全部按金瓜子单价处理；银瓜子礼物单价极小，金额影响可忽略 |
| JPN 变体先于主推送到达 | 极小概率；首条先到则以其为主记录（与 bilidanmu 行为一致） |
| 历史数据 | 旧库中已存的 `gift_combo` 行与 ADR-1 之前的重复 SC 行不清理，按原样展示 |

## 附录：B 站弹幕连接风控适配（2026-11-09 实测）

本地验证时发现录制器完全连不上弹幕服务器（连接 1~2 秒即被重置）。逐层对照实验
（.NET ClientWebSocket / Node 原生 TLS，参考项目 bilidanmu 同机正常）定位出四层原因，
均已修复：

| # | 原因 | 修复 |
| --- | --- | --- |
| 1 | **wss 被 TLS 指纹拦截**：.NET SChannel 的 wss 握手被接受后秒断，Node/rustls 可正常连接 | 改走**非 TLS 的 2244 端口**（`ws://{host}:2244/sub`，tiny-bilibili-ws 的 WEBSOCKET_PORT），无 TLS 指纹可查 |
| 2 | **WS 升级请求带浏览器头反而被杀**：UA/Origin/Cookie 任一存在都会被断，极简请求可通过 | `ConnectAsync` 不再设置任何自定义请求头（与 bilidanmu/tungstenite 裸握手一致） |
| 3 | **conf（getDanmuInfo）请求形态影响 token 有效性**：带 `type=0` 参数、bili_ticket、独立 buvid/b_lsid 头签发的 token 会被 WS 拒绝；buvid 必须在 Cookie 内（否则 -352） | `GetDanmakuConfAsync` 新 API 改为精简请求：仅 UA + Referer/Origin + Cookie（buvid 合并进 Cookie），参数仅 `id`，不送 bili_ticket |
| 4 | **token 与鉴权 uid 必须配对**：登录 cookie 签发"已登录 token"（约 362 字符）须配真实 uid；匿名 conf 签发匿名 token（约 256 字符）须配 uid=0。不匹配会被秒断 | conf 传空 cookie（纯 buvid 匿名），鉴权统一 `uid=0`，录制链路不依赖账号登录态 |

其他配套修复：

- 鉴权成功（op=8）后**立即发送首个心跳**（此前等 30s 周期心跳，会被服务端断开）；
  心跳包体改为 `[object Object]`（与 bilidanmu 一致）。
- WS 连接增加 15s 超时（边缘节点偶发握手挂起，超时后由重连循环接管）。
- `SendBiliGetAsync` 通用请求把 buvid3/buvid4/b_nut 合并进 Cookie（保留原独立头），
  修复所有 WBI 接口的 -352 风控。
- 账号 cookie 与弹幕连接解耦后，cookie 过期不再影响录制（仅影响后台需要登录态的
  API 调用）；过期凭据仍需在管理后台重新扫码登录。

安全说明：2244 为明文端口，鉴权 token 经明文传输。该 token 仅限对应房间弹幕读取，
风险可接受；如需加密传输需解决 TLS 指纹问题（当前 B 站仅放行浏览器/rustls/OpenSSL
指纹，.NET SChannel 被拒）。

