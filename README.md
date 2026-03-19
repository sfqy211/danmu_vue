# Danmu Vue Viewer & Recorder

一个用于 Bilibili 弹幕录制、回放和统计分析的前后端项目。

- 前端：Vue 3 + TypeScript + Vite
- 后端：.NET 9
- 数据库：MySQL
- 缓存/队列：内嵌 Microsoft Garnet（Redis 协议）

## 功能概览

- 实时弹幕录制与回放
- 弹幕统计与营收统计
- 时间轴分布与关键词分析
- 多主播管理与历史场次筛选
- 主播头像、封面与背景图管理
- 管理端 API 与部署状态检查

## 当前部署结构

项目当前推荐使用以下部署方式：

- 前端静态页面：腾讯云 COS + CDN
- 后端 API：腾讯云服务器 + 1Panel + Docker
- 自动部署：
  - 前端通过 GitHub Actions 构建并上传到 COS
  - 后端通过 GitHub Actions SSH 到服务器并执行 Docker 重建

推荐域名结构：

- `ovodm.top`：前端主站
- `www.ovodm.top`：前端备用网址
- `api.ovodm.top`：后端 API

域名接入和部署细节请查看：

- [DOMAIN_DEPLOY_GUIDE.md](./DOMAIN_DEPLOY_GUIDE.md)

## 本地开发

### 环境准备

- Node.js 18+
- npm 9+
- .NET SDK 9
- MySQL 8+

### 安装依赖

```bash
npm install
```

### 配置环境变量

在项目根目录创建 `.env` 文件，可参考：

```env
BILI_COOKIE=
PORT=3001
ADMIN_TOKEN=
VITE_API_BASE_URL=/api
MYSQL_CONNECTION_STRING=server=127.0.0.1;port=3306;database=danmaku_db;user=root;password=123456;charset=utf8mb4;
TENCENT_SECRET_ID=
TENCENT_SECRET_KEY=
COS_BUCKET=
COS_REGION=ap-shanghai
```

### 启动后端

后端内置了 Redis 服务，无需单独安装 Redis。

```bash
cd server_net
dotnet run --urls "http://0.0.0.0:3001"
```

后端默认地址：

- `http://localhost:3001`

### 启动前端

```bash
npm run dev
```

前端默认地址：

- `http://localhost:5200`

### 常用开发命令

```bash
npm run dev
npm run build
npm run start
```

说明：

- `npm run dev`：启动前端开发服务器
- `npm run build`：构建前端
- `npm run start`：以 Release 模式启动后端

## 生产部署

### Docker 部署

项目包含 `docker-compose.yml`，默认将宿主机 `5200` 端口映射到容器内 `3001`。

```bash
docker-compose up -d --build
```

默认端口关系：

- 宿主机：`5200`
- 容器：`3001`

### 1Panel 反向代理

如果服务器使用 1Panel，常见做法是：

- `api.ovodm.top` 反向代理到 `http://127.0.0.1:5200`
- 由 1Panel 管理 HTTPS 证书

注意：

- API 站点 HTTPS 端口应为 `443`
- 不要将 HTTPS 端口配置成 `80`

## 自动部署

### 前端工作流

文件：

- `.github/workflows/deploy-frontend.yml`

作用：

- 构建前端
- 上传 `dist` 到腾讯云 COS
- 刷新腾讯云 CDN 缓存

前端部署依赖以下 GitHub Actions Secrets：

- `VITE_API_BASE_URL`
- `VITE_ADMIN_API_BASE_URL`
- `TENCENT_SECRET_ID`
- `TENCENT_SECRET_KEY`
- `COS_BUCKET`
- `COS_REGION`
- `CDN_PATHS`

注意：

- `COS_BUCKET` 必须使用完整桶名，例如 `ovodm-web-1316468658`
- `COS_REGION` 应填写腾讯云地域编码，例如 `ap-shanghai`

### 后端工作流

文件：

- `.github/workflows/deploy.yml`

作用：

- SSH 登录服务器
- 拉取最新代码
- 执行 `docker-compose up --build -d`

注意：

- 服务器上的仓库内容会被工作流强制同步
- 不建议在服务器上手动修改仓库代码

## 环境变量说明

| 变量名 | 说明 |
| --- | --- |
| `BILI_COOKIE` | Bilibili Cookie |
| `PORT` | 后端端口，默认 `3001` |
| `ADMIN_TOKEN` | 管理端认证 Token |
| `MYSQL_CONNECTION_STRING` | MySQL 连接字符串 |
| `VITE_API_BASE_URL` | 前端 API 基础地址 |
| `VITE_ADMIN_API_BASE_URL` | 前端管理 API 基础地址 |
| `TENCENT_SECRET_ID` | 腾讯云 SecretId |
| `TENCENT_SECRET_KEY` | 腾讯云 SecretKey |
| `COS_BUCKET` | 腾讯云 COS 完整桶名 |
| `COS_REGION` | 腾讯云 COS 地域编码 |
| `DANMAKU_DIR` | 弹幕 XML 数据目录 |

## 数据与目录说明

- 弹幕 XML：`server/data/danmaku`
- 主播背景图：`server/data/vup-bg`
- 主播头像：`server/data/vup-avatar`
- 直播封面：`server/data/vup-cover`

后端启动时会扫描数据目录并同步数据库记录。

## 技术说明

### API 地址处理

前端默认支持通过环境变量注入 API 地址：

- `VITE_API_BASE_URL`
- `VITE_ADMIN_API_BASE_URL`

如果未配置，前端会回退到相对路径 `/api`。

### 图片资源处理

当前前端中的图片资源默认按当前域名加载，适合由 COS/CDN 统一提供静态资源。

## 项目结构

```text
danmu_vue/
├── .github/                 # GitHub Actions 与脚本
├── public/                  # 前端静态资源
├── scripts/                 # 构建辅助脚本
├── server/                  # 数据目录与旧代码相关文件
├── server_net/              # .NET 后端
├── src/                     # 前端源码
├── docker-compose.yml       # Docker 部署配置
├── Dockerfile               # 镜像构建配置
└── DOMAIN_DEPLOY_GUIDE.md   # 域名接入与部署指南
```

## 常见问题

### 1. 前端访问 `/api/...` 返回 COS XML 错误

通常表示前端请求仍然落到了主站域名，而不是 API 域名。

检查：

- `VITE_API_BASE_URL`
- `VITE_ADMIN_API_BASE_URL`
- 前端是否已重新构建并上传

### 2. GitHub Actions 上传 COS 失败并提示 `NoSuchBucket`

通常是以下配置错误：

- `COS_BUCKET` 不是完整桶名
- `COS_REGION` 填写错误

### 3. API HTTPS 访问失败

优先检查：

- `api.ovodm.top` 是否已解析到服务器
- 1Panel 反代目标是否为 `http://127.0.0.1:5200`
- HTTPS 端口是否为 `443`
- 腾讯云安全组是否放行 `443`

## 维护建议

- 先改 GitHub Secrets，再触发前端自动部署
- 修改部署结构前，先确认当前线上使用的是哪套方案
- 不要把不同部署方案的改动混在同一次推送中
- 新接手项目时，优先阅读：
  - `README.md`
  - `DOMAIN_DEPLOY_GUIDE.md`
  - `.github/workflows/*.yml`
