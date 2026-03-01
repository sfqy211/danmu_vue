# Danmu Vue Viewer & Recorder

一个集 Bilibili 弹幕录制、回放与统计分析于一体的前后端项目。前端使用 Vue 3 + TypeScript + Vite，后端使用 .NET 9（server_net）提供 API、录制与数据处理。

## 功能概览

- 实时弹幕录制与回放
- 弹幕统计与营收统计
- 时间轴分布与关键词分析
- 多主播管理与历史场次筛选
- 移动端适配与深色模式

## 快速开始

### 环境准备

- Node.js 18+
- .NET SDK 9

### 安装依赖

```bash
npm install
```

### 启动步骤

1. **启动后端**
   后端内置了 Redis 服务 (Microsoft Garnet)，无需单独安装 Redis。
   ```bash
   cd server_net
   dotnet run --urls "http://0.0.0.0:3001"
   ```

   **后端构建测试**
   ```bash
   dotnet build
   ```
   后端默认地址：`http://localhost:3001`

2. **启动前端**
   ```bash
   npm run dev
   ```
   前端默认地址：`http://localhost:5200`

## 数据与目录说明

- 弹幕 XML 目录：`server/data/danmaku`
- SQLite 数据库：`server/data/danmaku_data.db`
- 主播背景图：`server/data/vup-bg`
- 主播头像：`server/data/vup-avatar`
- 直播封面：`server/data/vup-cover`

后端启动时会扫描弹幕目录并重建/更新数据库记录。

## 环境变量

可通过环境变量覆盖默认路径或认证信息：

| 变量名 | 说明 | 默认值 |
| :--- | :--- | :--- |
| `BILI_COOKIE` | Bilibili 账号 Cookie（可选） | 空 |
| `DANMAKU_DIR` | 弹幕 XML 目录 | `server/data/danmaku` |
| `DB_PATH` | SQLite 数据库路径 | `server/data/danmaku_data.db` |
| `REDIS_CONNECTION` | Redis 连接字符串 | `localhost:6379,abortConnect=false` |

## 构建前端

```bash
npm run build
```

## Docker 部署

```bash
docker-compose up -d --build
```

## 项目结构

```
danmu_vue/
├── src/                 # 前端源码
├── server_net/          # .NET 后端
├── server/data/         # 数据目录（XML/DB/头像/封面）
├── public/              # 静态资源与构建输出
└── docker-compose.yml   # 部署配置
```
