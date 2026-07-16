# ADR-3: 弹幕存储从 JSONL 迁移到 SQLite（按 Session 分库）

> 阶段：第三阶段
> 前置：ADR-1（双语 SC 修复）、ADR-2（实付价格字段）
> 状态：待实施
> 日期：2026-06-19

## 背景

当前弹幕数据流：

```
WebSocket → RecordedDanmakuEvent → Redis List → 临时JSONL → 最终JSONL → 文件监听 → 统计
```

存在的问题：

1. JSONL 文件全量加载到内存才能查询，大数据量场次性能差
2. 临时文件→最终文件的迁移流程复杂，崩溃恢复逻辑脆弱
3. 无法对弹幕数据做 SQL 查询和聚合
4. `DanmakuProcessor` 的 FileSystemWatcher 机制不可靠

## 决策

采用**按 Session 分库**方案：每场直播一个 SQLite `.db` 文件，结合 JSONL 文件方案的天然分片优势和 SQLite 的索引查询能力。

**不兼容旧 JSONL 格式**，提供一次性迁移脚本将所有历史 JSONL 文件转换为 SQLite 文件。改造完成后系统只支持 SQLite 读取。

**移除临时文件机制**，直播期间直接写入最终的 `.db` 文件。系统维护在无直播时进行，无需考虑临时文件恢复。

## 改造后数据流

```
WebSocket → RecordedDanmakuEvent → Redis List（缓冲）
                                      ↓ 每分钟批量
                                   Session .db 文件（SQLite 批量 INSERT）
                                      ↓ 直播结束
                                   清除 Redis，计算统计写入 Session 表
```

## 详细设计

### 1. Session .db 文件

**路径规则**：`danmaku/{uid}/{yyyy-MM-dd HH-mm-ss}.db`

- 文件名使用直播开始时间的日期格式，不含标题（标题可能变更）
- 不再使用临时目录，直播期间直接写入最终的 `.db` 文件
- 不再需要 `danmaku_tmp/` 目录

**每个 .db 内的表结构**：

```sql
CREATE TABLE danmaku_messages (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    type              TEXT NOT NULL DEFAULT 'comment',
    timestamp         INTEGER NOT NULL,
    user              TEXT NOT NULL DEFAULT '',
    uid               TEXT NOT NULL DEFAULT '',
    text              TEXT,
    text_jpn          TEXT,
    name              TEXT,
    count             INTEGER NOT NULL DEFAULT 1,
    price             REAL,
    is_price_total    INTEGER NOT NULL DEFAULT 0,
    total_coin        REAL,
    discount_price    REAL,
    guard_level       INTEGER,
    medal_level       INTEGER,
    medal_name        TEXT,
    coin_type         TEXT,
    duration          INTEGER,
    face              TEXT,
    emots             TEXT,
    dm_type           INTEGER,
    raw_command       TEXT
);
CREATE INDEX idx_danmaku_timestamp ON danmaku_messages(timestamp);
CREATE INDEX idx_danmaku_type ON danmaku_messages(type);
```

**字段说明**（相比 ADR 初版，整合了第一、二阶段的改动）：

| 字段             | 来源事件     | 说明                                               |
| ---------------- | ------------ | -------------------------------------------------- |
| `id`             | 自增         | 主键，分页用                                       |
| `type`           | 所有         | `comment`/`super_chat`/`gift`/`guard`/`gift_combo` |
| `timestamp`      | 所有         | 毫秒级 Unix 时间戳                                 |
| `user`           | 所有         | 用户昵称                                           |
| `uid`            | 所有         | 用户 UID                                           |
| `text`           | 弹幕/SC      | 弹幕文字或 SC 消息                                 |
| `text_jpn`       | SC           | 日语翻译 + 机翻（ADR-1 修复后直接从 SC 事件提取）  |
| `name`           | 礼物/舰长    | 礼物名称或舰长身份                                 |
| `count`          | 礼物/舰长    | 数量                                               |
| `price`          | 所有         | 原价（元）                                         |
| `is_price_total` | 所有         | 0=单价，1=总价                                     |
| `total_coin`     | 礼物         | 实付总价（元，ADR-2 新增）                         |
| `discount_price` | 礼物         | 折扣单价（元，ADR-2 新增）                         |
| `guard_level`    | 舰长/弹幕    | 1=总督，2=提督，3=舰长                             |
| `medal_level`    | 弹幕/SC      | 粉丝牌等级                                         |
| `medal_name`     | 弹幕/SC      | 粉丝牌名称                                         |
| `coin_type`      | 礼物         | `silver`/`gold`                                    |
| `duration`       | SC           | SC 停留秒数                                        |
| `face`           | 弹幕/SC/礼物 | 头像 URL                                           |
| `emots`          | 弹幕         | 表情 JSON                                          |
| `dm_type`        | 弹幕         | 0=文字，1=表情                                     |
| `raw_command`    | 所有         | 原始命令名（区分 GUARD_BUY / USER_TOAST_MSG）      |

**已移除的字段**（相比 ADR 初版精简）：

| 移除字段            | 原因       |
| ------------------- | ---------- |
| `medal_anchor`      | 几乎不使用 |
| `medal_room_id`     | 几乎不使用 |
| `medal_guard_level` | 几乎不使用 |
| `medal_is_light`    | 几乎不使用 |
| `medal_anchor_uid`  | 几乎不使用 |
| `ul_level`          | 几乎不使用 |
| `wealth_level`      | 几乎不使用 |

### 2. Redis → SQLite 写入策略

| 项目       | 说明                                                  |
| ---------- | ----------------------------------------------------- |
| 写入时机   | 每 1 分钟从 Redis 取增量数据，批量 INSERT 到 .db 文件 |
| 连接管理   | 每个活跃录制器持有一个 SQLite 连接，直播期间保持打开  |
| 批量写入   | 在一个事务内批量 INSERT                               |
| Redis 清理 | 直播结束时才清除 Redis 数据                           |
| 实时查询   | 直播中的场次从 Redis 读（不变），结束后从 SQLite 读   |

### 3. Session 表 FilePath 字段

改造后只支持两种值：

| 前缀      | 含义   | 读取方式            |
| --------- | ------ | ------------------- |
| `redis:`  | 直播中 | 从 Redis List 读取  |
| `sqlite:` | 已结束 | 从对应 .db 文件读取 |

**不再支持无前缀的 JSONL 路径**。迁移脚本会将所有旧记录更新为 `sqlite:` 前缀。

### 4. 统计计算

- 直播结束时从 SQLite 一次性计算 `BuildAnalysis` + `BuildGiftAnalysis`
- 结果写入 Session 表的 `summary_json` / `gift_summary_json`
- 直播中统计由前端实时计算（不变）
- 统计金额优先使用 `total_coin`（实付价），舰长优先使用 `USER_TOAST_MSG` 的价格（ADR-2）

### 5. DanmakuProcessor：删除

迁移后 `DanmakuProcessor` 不再有任何功能：

- 不再生成 JSONL 文件 → FileSystemWatcher 永远不会触发
- 统计计算改为直播结束时从 SQLite 计算 → 不需要文件触发
- 旧文件迁移由迁移脚本完成 → 不需要 Processor 扫描

**直接删除此服务。**

### 6. 临时文件：移除

- 移除 `danmaku_tmp/` 目录及相关逻辑
- 移除 `ReconcileTmpFilesAsync`、`RestoreTmpToRedisAsync`、`PromoteTmpFileAsync` 等方法
- `FlushRedisIncrementallyAsync` 中写临时 JSONL 文件的逻辑改为写 SQLite

### 7. 前端 API：不变

API 返回数据结构完全兼容，前端零改动。

## 迁移脚本设计

### 脚本位置

`server_net/Tools/MigrateJsonlToSqlite.cs` — 作为独立命令行工具运行

### 执行方式

```bash
dotnet run --project server_net -- migrate-jsonl
```

### 迁移逻辑

```
1. 扫描 danmaku/ 目录下所有 .jsonl 和 .xml 文件
2. 对每个文件：
   a. 解析文件内容（复用现有 ParseJsonlFileAsync / ParseLegacyXmlFileAsync）
   b. 确定目标 .db 文件路径：danmaku/{uid}/{yyyy-MM-dd HH-mm-ss}.db
   c. 如果 .db 文件已存在，跳过（幂等）
   d. 创建 .db 文件，建表，批量 INSERT
   e. 查找 Session 表中匹配的记录，更新 FilePath 为 sqlite:{uid}/{日期时间}.db
3. 输出迁移统计：成功/跳过/失败数量
4. 旧 JSONL/XML 文件不自动删除，需手动确认后删除
```

### 幂等性

- 如果 `.db` 文件已存在且非空，跳过该文件
- 如果 Session 记录的 `FilePath` 已经是 `sqlite:` 前缀，跳过

### 旧数据字段映射

旧 JSONL 中的 `RecordedDanmakuEvent` 字段映射到新表：

| 旧字段            | 新表字段         | 说明                                    |
| ----------------- | ---------------- | --------------------------------------- |
| `MessageJpn`      | `text_jpn`       | ADR-1 修复后直接有值                    |
| `MessageTrans`    | `text_jpn`       | 合并到 text_jpn（优先取 message_trans） |
| `TotalCoin`       | `total_coin`     | 旧数据无此字段，设为 null               |
| `DiscountPrice`   | `discount_price` | 旧数据无此字段，设为 null               |
| `MedalAnchor`     | —                | 不再存储                                |
| `MedalRoomId`     | —                | 不再存储                                |
| `MedalGuardLevel` | —                | 不再存储                                |
| `MedalIsLight`    | —                | 不再存储                                |
| `MedalAnchorUid`  | —                | 不再存储                                |
| `UlLevel`         | —                | 不再存储                                |
| `WealthLevel`     | —                | 不再存储                                |

## 改动文件清单

| 文件                                     | 改动程度 | 说明                                                                                                       |
| ---------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------- |
| **新增** `Services/SessionDbService.cs`  | 新文件   | 管理 per-session SQLite 连接、批量写入、分页查询                                                           |
| **新增** `Tools/MigrateJsonlToSqlite.cs` | 新文件   | JSONL → SQLite 一次性迁移脚本                                                                              |
| **新增** `Models/DanmakuMessageRow.cs`   | 新文件   | SQLite 表对应的扁平化实体模型                                                                              |
| `BilibiliRecorder.cs`                    | 中等     | `FlushRedisIncrementallyAsync` 改为写 SQLite；`EndRedisSessionAsync` 简化；移除临时文件相关逻辑            |
| `DanmakuService.cs`                      | 大幅重构 | 只支持 Redis 和 SQLite 两种读取方式；统计改为从 SQLite 计算；删除所有 JSONL 读取方法；删除临时文件恢复方法 |
| `DanmakuProcessor.cs`                    | **删除** | 不再需要                                                                                                   |
| `Program.cs`                             | 小       | 注册 SessionDbService；移除 DanmakuProcessor 注册                                                          |
| `前端`                                   | 不变     | API 返回格式不变                                                                                           |

## 实施顺序

1. 新增 `DanmakuMessageRow.cs`（SQLite 表实体模型）
2. 新增 `SessionDbService.cs`（SQLite 连接管理、批量写入、分页查询）
3. 修改 `BilibiliRecorder.cs`（Redis → SQLite 写入、移除临时文件逻辑）
4. 修改 `DanmakuService.cs`（只支持 Redis + SQLite 读取、移除 JSONL 逻辑、统计从 SQLite 计算）
5. 删除 `DanmakuProcessor.cs`
6. 修改 `Program.cs`（注册新服务、移除旧服务）
7. 新增迁移脚本 `MigrateJsonlToSqlite.cs`
8. 测试：本地启动 → 模拟直播 → 验证写入和读取 → 运行迁移脚本 → 验证旧数据
9. 清理：确认迁移完成后删除旧 JSONL/XML 文件和 `danmaku_tmp/` 目录

## 风险与缓解

| 风险                            | 缓解措施                                              |
| ------------------------------- | ----------------------------------------------------- |
| SQLite 连接泄漏                 | 录制器 `Dispose` 中确保关闭连接；使用 `using` 声明    |
| 迁移脚本中途失败                | 幂等设计，可重复运行；.db 文件已存在则跳过            |
| 磁盘空间（新旧文件共存）        | 迁移完成后手动删除旧 JSONL/XML 文件                   |
| 直播期间 .db 文件损坏           | SQLite WAL 模式保证崩溃安全；单文件损坏不影响其他场次 |
| 移除 JSONL 兼容后旧数据无法读取 | 迁移脚本必须在系统上线前执行完毕                      |
| 旧数据缺少 total_coin 等新字段  | 设为 null，统计时回退到 price × count                 |
