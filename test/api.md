# vtbs.moe Open API

vtbs.moe 提供公开 API，请勿滥用。

---

## 免责声明

禁止利用 vdb 与 vtbs.moe 的开放数据与开放接口侵犯他人权益。如有任何纠纷，责任自负，vdb 与 vtbs.moe 相关团队成员不负任何责任。

侵犯他人权益的行为可能包括以下内容，若您的业务存在这些内容，我们建议您立刻核查处理：
- 获取、储存、公开用户或主播的收益、消费、评论、弹幕、入场等信息。
- 分析用户或主播的行为，或借此发布舆论导致用户或主播名誉受损。

---

## 数据质量说明 ⚠️

根据实际测试，以下数据的准确性有所不同：

| 数据字段 | 准确性 | 说明 |
|---------|--------|------|
| `mid` | ✅ 准确 | 用户 ID |
| `uname` | ✅ 准确 | 用户名 |
| `follower` | ✅ 准确 | 粉丝数 |
| `guardNum` | ✅ 准确 | 舰长数 |
| `video` | ✅ 准确 | 视频数 |
| 其他字段 | ⚠️ 可能过时 | 总播放、直播状态、最后直播时间等可能更新不及时 |

**注意**：vtbs.moe API **不提供** 投稿列表、BV 号、投稿时间等信息。

---

## CDN 节点

| 节点地址 | 说明 |
|---------|------|
| `api.vtbs.moe` | AWS |
| `api.aws.vtbs.moe` | AWS (推荐，同上) |
| `cfapi.vtbs.moe` | Cloudflare (推荐) |
| `api.hk.vtbs.moe` | 香港 → AWS |

---

## V1 (JSON) - 简单 JSON 接口

### /v1/vtbs
获取简单的 Vtuber 列表（仅 mid 和 uuid）

**接口：** `GET https://api.vtbs.moe/v1/vtbs`

**返回：**
```json
[
  {
    "mid": 197,
    "uuid": "948ae126-061d-5258-a280-82423b5a5b7b"
  }
]
```

**字段说明：**
- `mid`：用户 ID，出现在 `https://space.bilibili.com/` 后面
- `uuid`：唯一标识符

---

### /v1/info
获取所有 Vtuber 的完整信息

**接口：** `GET https://api.vtbs.moe/v1/info`

**返回：**
```json
[
  {
    "mid": 1576121,
    "uname": "帕里_Paryi",
    "video": 55,
    "roomid": 4895312,
    "sign": "我是paryipro的画师paryi~中国朋友们好~请大家关注我~paryi审核群：439902287",
    "notice": "",
    "face": "http://i2.hdslb.com/bfs/face/0f1f65edca3d354a974edb7a6bec01646bcfa4db.jpg",
    "rise": 1302,
    "topPhoto": "http://i0.hdslb.com/bfs/space/81a39f45e49364646274f6e6d4f406d18fdb6afd.png",
    "archiveView": 2691646,
    "follower": 94667,
    "liveStatus": 0,
    "recordNum": 958,
    "guardNum": 22,
    "liveNum": 1100,
    "lastLive": {
      "online": 121883,
      "time": 1560088457424
    },
    "averageLive": 42276996.14747928,
    "weekLive": 45900000,
    "guardChange": 270,
    "guardType": [0, 0, 22],
    "areaRank": 1000,
    "online": 0,
    "title": "b限定】明日方舟",
    "time": 1560102857468
  }
]
```

---

### /v1/infoa ⭐ (推荐)
获取所有 Vtuber 的完整信息（性能优化版，数据同 /v1/info）

**接口：** `GET https://api.vtbs.moe/v1/infoa`

**说明：** 直接从数据库批量获取，性能更好，推荐使用。

---

### /v1/secret
获取秘密 Vtuber 列表

**接口：** `GET https://api.vtbs.moe/v1/secret`

---

### /v1/secretInfo
获取秘密 Vtuber 的完整信息

**接口：** `GET https://api.vtbs.moe/v1/secretInfo`

---

### /v1/short
获取精简的 Vtuber 列表（仅 mid、uname、roomid）

**接口：** `GET https://api.vtbs.moe/v1/short`

**返回：**
```json
[
  {
    "mid": 392101937,
    "uname": "-水梨若official-",
    "roomid": 21745906
  }
]
```

---

### /v1/fullInfo
获取包含 vdb 数据的完整信息

**接口：** `GET https://api.vtbs.moe/v1/fullInfo`

**返回：**
```json
[
  {
    "mid": 5450477,
    "uname": "-可乐KORA-",
    "video": 4,
    "roomid": 7194103,
    "sign": "Egolive所属还未出道的四期生，虚拟考拉的可乐！是一只家里蹲！！\nTwitter:KORA_egolive",
    "notice": "",
    "face": "http://i2.hdslb.com/bfs/face/c412945daeaf056b081a5ba8ec7a9a8de9a38101.jpg",
    "rise": 0,
    "topPhoto": "http://i0.hdslb.com/bfs/space/aa4f6ea29d15397eb3791b0e27c31de391e94093.png",
    "archiveView": 0,
    "follower": 486,
    "liveStatus": 0,
    "recordNum": 382,
    "guardNum": 5,
    "lastLive": {
      "online": 4236,
      "time": 1617175669737
    },
    "guardChange": 29,
    "guardType": [0, 0, 5],
    "areaRank": 1000,
    "online": 0,
    "title": "【杂谈】来和家里蹲聊天吧oO",
    "time": 1617282221988,
    "liveStartTime": 0,
    "uuid": "9b44e2a9-3334-5792-b57d-1bf0939afe49",
    "vdb": {
      "uuid": "9b44e2a9-3334-5792-b57d-1bf0939afe49",
      "type": "vtuber",
      "bot": false,
      "accounts": [
        {
          "id": "5450477",
          "type": "official",
          "platform": "bilibili"
        }
      ],
      "name": {
        "extra": [],
        "cn": "-可乐KORA-",
        "default": "cn"
      },
      "group": "ee2d6579-f7b2-59e4-be05-ca88f4bdff7d"
    }
  }
]
```

---

### /v1/detail/:mid
获取单个 Vtuber 的详细信息

**接口：** `GET https://api.vtbs.moe/v1/detail/{mid}`

**示例：** `https://api.vtbs.moe/v1/detail/349991143`

**返回：**
```json
{
  "mid": 349991143,
  "uname": "神楽めあOfficial",
  "video": 188,
  "roomid": 12235923,
  "sign": "这里是神楽めあ(KaguraMea)！来自日本的清楚系虚拟YouTuber～weibo:@kaguramea　",
  "notice": "",
  "face": "http://i2.hdslb.com/bfs/face/49e143e1cae7f9e51b36c6c670976a95cc41ce12.jpg",
  "rise": 998,
  "topPhoto": "http://i0.hdslb.com/bfs/space/cde2a0fe3273ae4466d135541d965e21c58a7454.png",
  "archiveView": 21543188,
  "follower": 366159,
  "liveStatus": 0,
  "recordNum": 1268,
  "guardNum": 970,
  "liveNum": 559,
  "lastLive": {
    "online": 354234,
    "time": 1558976168120
  },
  "averageLive": 21271218.38426421,
  "weekLive": 0,
  "guardChange": 953,
  "guardType": [1, 15, 960],
  "areaRank": 2,
  "online": 0,
  "title": "【B限】MeAqua 協力お料理!!!!",
  "time": 1560103157470
}
```

---

### 舰长相关接口

#### /v1/guard/all
获取所有舰长信息

**接口：** `GET https://api.vtbs.moe/v1/guard/all`

**返回：**
```json
{
  "119": {
    "uname": "狂气的芙兰",
    "face": "https://i0.hdslb.com/bfs/face/12020cb3bfc0dc7f2a2c47007b204b9559d492f0.jpg",
    "mid": 119,
    "dd": [[], [], [349991143]]
  }
}
```

**dd 数组说明：**
- 第 1 个元素：总督列表
- 第 2 个元素：提督列表
- 第 3 个元素：舰长列表

---

#### /v1/guard/some
获取部分舰长（至少是提督或 DD）

**接口：** `GET https://api.vtbs.moe/v1/guard/some`

**返回：** 格式同 `/v1/guard/all`，过滤掉只有一个舰长的

---

#### /v1/guard/:mid
获取指定 Vtuber 的舰长列表

**接口：** `GET https://api.vtbs.moe/v1/guard/{mid}`

**示例：** `https://api.vtbs.moe/v1/guard/1576121`

**返回：**
```json
[
  {
    "mid": 110129,
    "uname": "朔海鸣音",
    "face": "https://i0.hdslb.com/bfs/face/862b9d84e0210c2c0c5b155bd95fb69d4c5c9cfa.jpg",
    "level": 2
  }
]
```

**level 说明：**
- `0`：总督
- `1`：提督
- `2`：舰长

---

#### /v1/guard/time
获取舰长列表更新时间戳

**接口：** `GET https://api.vtbs.moe/v1/guard/time`

**返回：** `1560050332931` (毫秒时间戳)

---

### 直播相关接口

#### /v1/living
获取当前正在直播的房间号列表

**接口：** `GET https://api.vtbs.moe/v1/living`

**返回：**
```json
[746929, 21665984, 3012597]
```

---

#### /v1/room/:roomid
获取指定房间的直播信息

**接口：** `GET https://api.vtbs.moe/v1/room/{roomid}`

**示例：** `https://api.vtbs.moe/v1/room/8899503`

**返回：**
```json
{
  "uid": 286179206,
  "roomId": "8899503",
  "title": "【时乃空生日会】我，20岁啦！！！",
  "popularity": 272839,
  "live_time": 1589536953000
}
```

---

#### /v1/hawk
获取直播热词

**接口：** `GET https://api.vtbs.moe/v1/hawk`

**返回：**
```json
{
  "day": [
    { "word": "哈哈哈", "weight": 416573.43 },
    { "word": "？？？", "weight": 326972.06 }
  ],
  "h": [
    { "word": "哈哈哈", "weight": 81216.98 },
    { "word": "哭哭", "weight": 77225.86 }
  ]
}
```

**说明：**
- `day`：最近 24 小时热词
- `h`：最近 1 小时热词

---

## V2 (JSON) - 批量历史数据接口

### /v2/bulkActive/:mid
获取粉丝数和播放量历史

**接口：** `GET https://api.vtbs.moe/v2/bulkActive/{mid}`

**示例：** `https://api.vtbs.moe/v2/bulkActive/349991143`

**返回：**
```json
[
  {
    "archiveView": 16222668,
    "follower": 298364,
    "time": 1555247781729
  }
]
```

**字段说明：**
- `archiveView`：视频播放量
- `follower`：粉丝数
- `time`：时间戳

---

### /v2/bulkActiveSome/:mid
获取粉丝数和播放量历史（仅最近 512 条）

**接口：** `GET https://api.vtbs.moe/v2/bulkActiveSome/{mid}`

**返回：** 格式同 `/v2/bulkActive/:mid`

---

### /v2/bulkGuard/:mid
获取舰长数历史

**接口：** `GET https://api.vtbs.moe/v2/bulkGuard/{mid}`

**返回：**
```json
[
  {
    "guardNum": 22,
    "areaRank": 1000,
    "time": 1560088457424
  }
]
```

---

### /v2/bulkOnline
获取全站人气历史

**接口：** `GET https://api.vtbs.moe/v2/bulkOnline`

**返回：**
```json
[
  {
    "liveStatus": 457,
    "online": 8783729,
    "time": 1617282137538
  }
]
```

**字段说明：**
- `liveStatus`：当前直播数量
- `online`：总人气
- `time`：时间戳

---

## V3 (Buffer) - 二进制数据接口

### /v3/allActive
获取所有 Vtuber 的活跃数据（二进制格式，大端序）

**接口：** `GET https://api.vtbs.moe/v3/allActive`

**数据结构：**
```
Pack 结构：
  [32bit] pack 大小
  [32bit] mid
  [data]... (多个 data 块)

Data 块结构：
  [32bit] archiveView (视频播放量)
  [32bit] follower (粉丝数)
  [64bit] time (时间戳)
```

**Node.js 解码示例：**
```javascript
const decodeData = buffer => {
  const data = []
  while (buffer.length) {
    const archiveView = buffer.readUInt32BE()
    const follower = buffer.readUInt32BE(4)
    const time = Number(buffer.readBigUInt64BE(8))
    data.push({ archiveView, follower, time })
    buffer = buffer.slice(16)
  }
  return data
}

const decodePack = buffer => {
  const actives = []
  while (buffer.length) {
    const size = buffer.readUInt32BE()
    const mid = buffer.readUInt32BE(4)
    const data = decodeData(buffer.slice(8, size))
    buffer = buffer.slice(size)
    actives.push({ mid, data })
  }
  return actives
}

decodePack(buffer)
```

---

## Meta 接口

### /meta/ping
健康检查

**接口：** `GET https://api.vtbs.moe/meta/ping`

**返回：** `pong`

---

### /meta/cdn
获取 CDN 列表

**接口：** `GET https://api.vtbs.moe/meta/cdn`

**返回：**
```json
[
  "https://api.vtbs.moe",
  "https://api.aws.vtbs.moe",
  "https://cfapi.vtbs.moe",
  "https://api.hk.vtbs.moe",
  "https://api.tokyo.vtbs.moe"
]
```

---

### /meta/timestamp
获取当前时间戳

**接口：** `GET https://api.vtbs.moe/meta/timestamp`

**返回：** `1590850640669` (毫秒时间戳)

---

## vdSocket - 实时弹幕 WebSocket

使用 Socket.io 连接获取实时弹幕。

**连接地址：** `https://api.vtbs.moe/vds`

**示例代码：**
```javascript
const socket = io('https://api.vtbs.moe', { path: '/vds' })

// 订阅房间
socket.emit('join', roomid)
// 或订阅所有房间
socket.emit('join', 'all')

// 取消订阅
socket.emit('leave', roomid)
// 或取消订阅所有
socket.emit('leave', 'all')

// 接收弹幕
socket.on('danmaku', data => {
  console.log(data)
})
```

**弹幕数据格式（实际测试结果）：**
```json
{
  "message": "弹幕内容",
  "roomid": 12235923,
  "mid": 0,
  "uname": "b***",
  "timestamp": 1772132428728
}
```

**字段说明：**
| 字段 | 说明 | 注意事项 |
|------|------|----------|
| `message` | 弹幕内容 | ✅ 完整可用 |
| `roomid` | 房间号 | ✅ 完整可用 |
| `mid` | 用户 ID | ⚠️ 通常为 0（匿名用户） |
| `uname` | 用户名 | ⚠️ 已脱敏（如 "b***", "岁***"） |
| `timestamp` | 时间戳 | ✅ 毫秒时间戳 |

**重要提示：**
- ❌ 无法获取真实的完整用户名
- ❌ 大多数情况下 `mid` 为 0，无法通过 `/v1/detail/:mid` 查询用户信息
- ✅ 可以正常获取弹幕内容和房间号

**文档：** https://github.com/dd-center/vtuber-danmaku#socketio

---

## Endpoint - shields.io 接口

用于 shields.io 徽章的接口。

| 接口 | 说明 |
|------|------|
| `/endpoint/vtbs` | Vtuber 数量 |
| `/endpoint/guardNum` | 舰长数量 |
| `/endpoint/live` | 正在直播数量 |
| `/endpoint/onlineSum` | 总在线人气 |
| `/endpoint/guard/:mid` | 指定 Vtuber 的舰长数 |
| `/endpoint/online/:mid` | 指定 Vtuber 的人气 |

---

## 其他

- **vtbs.moe api (Socket.IO)**：高级接口，请参考 GitHub 源码
- **DD@Home**：https://github.com/dd-center/Cluster-center
