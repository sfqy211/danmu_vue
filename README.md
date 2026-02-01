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
- **启动后端 API**：`npm run dev:server`

#### 弹幕监控开启方式
- 'pm2 start ecosystem.config.cjs'
### 生产环境部署 (1Panel + PM2)

项目采用前后端分离部署方案，建议使用 1Panel 面板进行管理。

### 1. 后端部署 (API & 录制监控)

1. **上传文件**：将以下文件上传到服务器目录（例如 `/opt/1panel/apps/danmu-tools`）：
   - `server/` 文件夹 (包含源码和后端 `package.json`)
   - `ecosystem.config.cjs` (PM2 配置文件)
   - `package.json` (根目录配置文件)
   - `tsconfig.json` (根目录配置文件)
   - `.env` (可选，配置 BILI_COOKIE)

2. **创建 Node.js 项目**：
   - 在 1Panel 中创建 Node.js 项目。
   - **项目目录**：选择包含上述文件的根目录 `/opt/1panel/apps/danmu-tools`。
   - **启动命令**：`npx pm2-runtime start ecosystem.config.cjs`
   - **端口**：容器内 `3001`，暴露端口 `5200`。

3. **管理录制进程**：
   - 进入容器终端，使用 `npx pm2 list` 查看录制状态。
   - 使用 `npx pm2 logs` 查看实时录制日志。

### 2. 前端部署 (静态网页)

1. **本地构建**：运行 `npm run build` 生成 `dist` 文件夹。
2. **上传文件**：将 `dist` 内的所有内容上传到 1Panel 网站根目录（例如 `/www/sites/43.143.121.223/index`）。
3. **创建静态网站**：在 1Panel 中创建一个静态网站（例如使用端口 `90`）。

### 3. Nginx 配置 (关键)

为了确保页面刷新不出现 404，以及前端能正常访问后端 API，需修改 Nginx 配置文件：

1. **配置伪静态 (解决刷新 404)**：
   在 `server` 块中添加：
   ```nginx
   location / {
       try_files $uri $uri/ /index.html;
   }
   ```

2. **配置反向代理 (连接后端)**：
   配置 `/api` 路径转发到后端的 `5200` 端口：
   ```nginx
   location /api {
       proxy_pass http://127.0.0.1:5200;
       proxy_set_header Host $host;
       proxy_set_header X-Real-IP $remote_addr;
   }
   ```

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
