# Danmu Vue Viewer & Recorder

这是一个集 Bilibili 弹幕**实时录制**与**可视化查看**于一体的系统，基于 Vue 3 + TypeScript + Vite + Express + PM2 开发。

## 功能特性

- 🎙️ **实时录制**：支持多直播间同时录制，自动保存为标准 XML 格式。
- 📊 **弹幕统计**：查看用户发送弹幕数量、SC 统计、排行。
- 📈 **时间轴分析**：可视化弹幕密度随时间的变化。
- 🔍 **搜索与过滤**：支持按关键词、用户、SC 类型筛选弹幕。
- 🌓 **深色模式**：支持明亮/深色主题切换。
- 📱 **响应式设计**：适配桌面端和移动端。
-  **进程管理**：使用 PM2 守护录制进程，支持崩溃自启。

---

## 快速开始

### 1. 环境准备
- Node.js 18+
- 安装依赖：
  ```bash
  npm install
  cd server && npm install && cd ..
  ```
- 配置环境变量：
  复制 `.env.example` 为 `.env` 并填写 `BILI_COOKIE`（可选，用于录制高等级弹幕或解决 API 限制）。

### 2. 开发调试

#### 方案 A：一键启动 (前后端同时运行)
```bash
npm run dev:all
```
- 前端：`http://localhost:5200`
- 后端 API：`http://localhost:3001`

#### 方案 B：前后端分离开发
- **启动前端**：`npm run dev`
  - 访问地址：`http://localhost:5200` (Vite 默认端口)
- **启动后端 API**：`cd server && npm run dev`
  - API 地址：`http://localhost:3001`

#### 弹幕监控开启方式
- 开发环境：后端启动时会自动开始监控录制（由 `server/src/api.ts` 启动 `recorder.ts` 或 `pm2`）。
- 生产环境：使用 PM2 或 Docker 守护进程。

#### PM2相关命令
- 启动所有进程：`pm2 start ecosystem.config.cjs`
- 查看进程状态：`pm2 status`
- 重启所有进程：`pm2 restart all`
- 停止所有进程：`pm2 stop all`
- 删除所有进程：`pm2 delete all`
- 查看日志：`pm2 logs`

### 生产环境部署 (1Panel + 腾讯云 COS/CDN)

本项目推荐采用 **前后端分离** 的部署架构：
- **前端**：构建为静态资源，托管于对象存储（如腾讯云 COS）并通过 CDN 加速。
- **后端**：使用 Docker 部署在服务器上（如通过 1Panel 面板），提供 API 和录制服务。

---

### 1. 后端部署 (Docker + 1Panel)

后端负责提供 API 接口和执行弹幕录制任务。

1. **上传文件**：将项目根目录下的所有文件上传到服务器目录（如 `/opt/1panel/apps/danmu-tools`）。
2. **创建 Compose 项目**：
   - 进入 1Panel 控制台 -> **容器** -> **编排 (Compose)**。
   - 点击 **创建**，项目名称填 `danmu-server`。
   - 在 **编辑编排文件** 中，填入项目根目录下的 `docker-compose.yml` 内容。
     - **注意端口映射**：默认配置为 `5200:3001`，即宿主机的 `5200` 端口映射到容器的 `3001` 端口。
   - 点击 **确认**，1Panel 会自动构建镜像并启动容器。
3. **验证后端**：
   - 在服务器上执行 `curl http://127.0.0.1:5200/api/streamers`，应返回 JSON 数据。
   - 确保服务器防火墙（如腾讯云安全组）已放行 `5200` 端口。

### 2. 前端部署 (静态托管 + CDN)

前端代码构建后为纯静态文件，无需 Node.js 环境运行。

1. **修改前端配置**：
   - 编辑 `.env.production` (如无则创建)，设置后端 API 地址：
     ```ini
     VITE_API_BASE_URL=https://api.sfqyweb.xyz
     ```
     *(请将 `https://api.sfqyweb.xyz` 替换为你的实际后端域名)*

2. **本地构建**：
   - 运行 `npm run build`。
   - 构建产物将生成在 `dist` 目录下。

3. **上传至对象存储 (COS/OSS)**：
   - 将 `dist` 目录下的所有文件上传至你的对象存储桶（如腾讯云 COS）。
   - 开启静态网站托管功能。

4. **配置 CDN 加速 (推荐)**：
   - 为对象存储配置 CDN 加速域名（如 `sfqyweb.xyz`）。
   - **关键配置**：由于前端是 SPA (单页应用)，需要在 CDN 或对象存储中配置 **404 错误页面重定向到 `index.html`**，以解决路由刷新 404 问题。
   - **API 跨域**：后端已开启 CORS，前端可直接请求后端域名。

### 3. 后端域名配置 (CDN/Nginx 反代)

为了让前端能安全地访问后端，建议为后端配置一个域名（如 `api.sfqyweb.xyz`）。

1. **CDN 回源配置**：
   - 在腾讯云 CDN 控制台添加域名 `api.sfqyweb.xyz`。
   - **源站配置**：
     - 源站类型：自有源站
     - 源站地址：你的服务器公网 IP
     - **端口**：`5200` (对应 Docker 映射出的宿主机端口)
     - 回源协议：HTTP

2. **验证**：
   - 访问 `https://api.sfqyweb.xyz/api/streamers`，应能正常获取数据。

---

## 配置说明

你可以通过 `.env` 文件或环境变量调整系统行为：

| 变量名 | 说明 | 默认值 |
| :--- | :--- | :--- |
| `BILI_COOKIE` | Bilibili 账号 Cookie (用于录制) | 无 |
| `DANMAKU_DIR` | XML 弹幕文件存放目录 | `server/data/danmaku` |
| `PORT` | 后端 API 服务端口 | `3001` |
| `DB_PATH` | SQLite 数据库路径 | `danmaku_data.db` |

---

## 项目结构

```
danmu_vue/
├── server/             # 后端服务
│   ├── src/            # 源码 (api.ts: 接口, recorder.ts: 录制)
│   └── data/           # 数据存储 (XML 和 DB)
├── src/                # 前端源码 (Vue 3)
├── ecosystem.config.cjs # PM2 配置文件
├── Dockerfile          # Docker 构建文件
└── docker-compose.yml  # Docker 编排文件
```
