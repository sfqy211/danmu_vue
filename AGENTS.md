# AGENTS.md

本文件服务于本仓库内的 AI 代理与 vibe coding 协作。核心原则只有一条：先按目录确认你在改前端还是后端，再进入对应边界工作，不要把整个仓库当成一个模糊的全栈模板项目处理。

## 1. 项目边界

- 前端目录：`D:\AHEU\code\webpage\danmu_vue\src`
- 后端目录：`D:\AHEU\code\webpage\danmu_vue\server_net`
- 仓库根目录：`D:\AHEU\code\webpage\danmu_vue`

项目用途：

- 采集、存储、回放和分析 Bilibili 直播弹幕
- 提供前台回放与统计页面
- 提供后台主播、场次、点歌和录制管理能力

技术栈：

- 前端：`Vue 3` + `TypeScript` + `Vite` + `Pinia` + `Element Plus`
- 后端：`.NET 9` + `ASP.NET Core` + `EF Core` + `MySQL`
- 缓存/队列：嵌入式 `Microsoft Garnet`

## 2. 工作前先判断改动归属

### 如果任务属于前端

优先只阅读和修改：

- `src/`
- `vite.config.ts`
- `package.json`
- 必要时再看 `README.md` 与 `docs/`

典型前端任务：

- 页面布局、样式、交互
- 路由和页面组织
- API 请求适配
- 状态管理
- 列表展示、分页、筛选、图表

### 如果任务属于后端

优先只阅读和修改：

- `server_net/`
- 根目录 `.env` / `.env.example`
- `docker-compose.yml`
- 必要时再看 `README.md` 与 `docs/`

典型后端任务：

- Controller 接口
- EF Core 数据模型与查询
- 录制进程管理
- 弹幕解析和统计
- 后台鉴权
- 静态资源映射

### 如果任务是前后端联动

按这个顺序排查：

1. `src/api/danmaku.ts`
2. 对应 `server_net/Controllers/*.cs`
3. 对应 `server_net/Models/*.cs` / `server_net/Data/*.cs`
4. 如涉及部署，再看 `docs/` 和工作流

## 3. 必读文件

开始中等及以上改动前，至少看：

1. `README.md`
2. `docs/域名接入与部署指南.md`
3. `docs/架构基线与风险台账.md`

如果是部署相关，再加：

1. `.github/workflows/deploy-frontend.yml`
2. `.github/workflows/deploy.yml`
3. `docker-compose.yml`

## 4. 真实目录结构

### 前端 `src/`

- `main.ts`
  - 应用入口，挂载 `Pinia`、`router`、`Element Plus`
- `router/index.ts`
  - 路由主干，当前使用 `createWebHashHistory`
- `api/danmaku.ts`
  - 前端 API 访问层与字段兼容层
- `stores/danmakuStore.ts`
  - 前台核心状态中心
- `views/HomeView.vue`
  - 前台首页
- `views/AdminView.vue`
  - 管理后台页面，当前是高复杂度单文件
- `components/`
  - 前台和后台共用展示组件
- `constants/`
  - 主播常量与颜色配置
- `layouts/`
  - 页面布局

### 后端 `server_net/`

- `Program.cs`
  - 后端启动装配入口
- `Controllers/AdminController.cs`
  - 后台管理接口
- `Controllers/DanmakuController.cs`
  - 弹幕、主播、场次等业务接口
- `Services/`
  - 录制、弹幕处理、图片抓取、调度、缓存等服务
- `Data/DanmuContext.cs`
  - EF Core 数据库上下文
- `Data/DbInitializer.cs`
  - 初始化与数据同步
- `Models/`
  - 房间、场次、点歌等实体
- `Filters/AdminAuthAttribute.cs`
  - 管理端鉴权过滤器

## 5. 当前部署事实

不要自行脑补部署模型，当前事实是：

- 前端构建产物 `dist/` 主要部署到腾讯云 `COS + CDN`
- 后端 API 部署在腾讯云服务器，通过 `1Panel + Docker` 提供服务
- 推荐域名：
  - `ovodm.top` / `www.ovodm.top`：前端
  - `api.ovodm.top`：后端 API

本地开发事实：

- 前端 dev server 端口：`5200`
- 后端本地运行端口：`3001`
- Vite 将 `/api` 代理到 `http://localhost:3001`

容器部署事实：

- 宿主机端口：`5200`
- 容器内端口：`3001`

结论：

- 不要把生产 API 地址写死为本地地址
- 不要把本地代理逻辑误当成线上地址策略
- 不要随意把前端改成依赖 .NET 同域托管

## 6. 前端工作约束

### 6.1 路由与页面

- 当前使用 Hash 路由，不要擅自改为 history 模式。
- 如果必须改路由模式，必须同时评估 COS、CDN、根路径重写和 SPA fallback。

### 6.2 API 访问

- `src/api/danmaku.ts` 是关键适配层。
- 这里已经兼容 `snake_case`、`camelCase`、`PascalCase`。
- 除非后端字段规范已统一并完成联调，否则不要删除这些兼容逻辑。

### 6.3 状态与性能

- `src/stores/danmakuStore.ts` 负责摘要加载、弹幕分页、前端过滤。
- `loadMore` 当前并发保护偏弱，改分页逻辑时要注意重复请求和重复拼接。
- 搜索和过滤在大数据量场次下有性能压力，避免无边界增加实时计算。

### 6.4 高风险前端文件

- `src/views/AdminView.vue`
  - 当前集成鉴权、列表、表单、分页、批量操作与样式，是最容易引发回归的前端文件。
- `src/api/danmaku.ts`
  - 一旦调整错误，会影响所有前后端契约适配。
- `src/stores/danmakuStore.ts`
  - 一旦调整错误，会影响前台加载链路和大列表表现。

### 6.5 前端修改建议

- 小修复优先局部改动，不要顺手重写整个页面。
- 如果确实重构后台页，优先把 `AdminView.vue` 拆为 composables + 子组件。
- 视觉或布局改动尽量不触碰 API 适配层。

## 7. 后端工作约束

### 7.1 启动与配置

- `server_net/Program.cs` 负责读取 `.env`、注册服务、映射控制器和静态资源。
- 不要破坏现有 `.env` 读取路径逻辑。
- MySQL 连接字符串依赖环境变量传入，不要改成只依赖本地私有配置。

### 7.2 服务注册与处理链路

- 不要随意调整 `EmbeddedRedisService`、`RedisService`、`DanmakuProcessor`、`ProcessManager` 的角色与启动关系。
- 修改录制恢复、弹幕处理、调度逻辑前，先确认是否会影响服务启动后的自动恢复链路。

### 7.3 静态资源与数据目录

- 后端会映射：
  - `/vup-bg`
  - `/vup-avatar`
  - `/vup-cover`
- 不要破坏这些路径与 `server/data` 目录的对应关系。

### 7.4 鉴权与接口契约

- 管理端鉴权涉及 `AdminAuthAttribute` 与前端 token 注入逻辑。
- 修改管理端鉴权时，要同时检查前端 `AdminView.vue` 中的认证请求方式。
- 修改响应字段时，要同步评估前端兼容层是否仍可工作。

### 7.5 高风险后端文件

- `server_net/Program.cs`
  - 改错会直接影响启动、静态资源和运行环境。
- `server_net/Controllers/AdminController.cs`
  - 改错会直接影响后台管理功能。
- `server_net/Controllers/DanmakuController.cs`
  - 改错会直接影响前台数据获取。
- `server_net/Services/*`
  - 改错可能影响录制、分析、图片抓取与调度。

## 8. 环境变量与敏感信息

常见变量：

- `BILI_COOKIE`
- `PORT`
- `ADMIN_TOKEN`
- `MYSQL_CONNECTION_STRING`
- `VITE_API_BASE_URL`
- `VITE_ADMIN_API_BASE_URL`
- `TENCENT_SECRET_ID`
- `TENCENT_SECRET_KEY`
- `COS_BUCKET`
- `COS_REGION`
- `DANMAKU_DIR`

规则：

- 禁止把真实密钥、Cookie、Token 提交到仓库。
- 前端默认回退 `/api` 是有意设计，不要轻易改。
- 改环境变量名时，必须同步代码、工作流和文档。

## 9. 验证要求

### 前端改动后

至少执行：

```bash
npm run build
```

不要直接执行：

```bash
npm run dev
```

如改动页面逻辑，再手动检查：

- 首页
- 主播详情页
- 点歌页
- 管理页

### 后端改动后

至少执行：

```bash
dotnet build server_net/Danmu.Server.csproj
```

如改动接口，再检查：

- 前端是否还能正常解析数据
- 管理接口鉴权是否正常
- 静态资源路径是否仍可访问

### 部署改动后

必须额外检查：

- `docker-compose.yml`
- GitHub Actions 工作流
- 文档中的端口、域名、Secrets 是否一致

## 10. 文档同步规则

以下变化必须同步更新文档：

- 部署方式
- 域名
- 端口
- 启动命令
- 环境变量名
- 数据目录
- 架构边界

优先同步：

1. `README.md`
2. `docs/域名接入与部署指南.md`
3. `docs/架构基线与风险台账.md`

## 11. 建议的排查顺序

### 页面显示异常

1. 看 `src/views/*` 和 `src/components/*`
2. 看 `src/stores/danmakuStore.ts`
3. 看 `src/api/danmaku.ts`

### 请求数据异常

1. 看 `src/api/danmaku.ts`
2. 看对应 `server_net/Controllers/*.cs`
3. 看模型和数据查询逻辑

### 管理后台异常

1. 看 `src/views/AdminView.vue`
2. 看 `server_net/Filters/AdminAuthAttribute.cs`
3. 看 `server_net/Controllers/AdminController.cs`

### 录制或统计异常

1. 看 `server_net/Services/ProcessManager.cs`
2. 看 `server_net/Services/BilibiliRecorder.cs`
3. 看 `server_net/Services/DanmakuProcessor.cs`
4. 看 `server_net/Services/DanmakuService.cs`

### 部署或线上访问异常

1. 看 `VITE_API_BASE_URL` / `VITE_ADMIN_API_BASE_URL`
2. 看 `docker-compose.yml`
3. 看 `docs/域名接入与部署指南.md`
4. 看 GitHub Actions 工作流

## 12. 明确不要做的事

- 不要把整个仓库当成单体同构项目来改。
- 不要混淆 `src` 前端边界和 `server_net` 后端边界。
- 不要擅自把 Hash 路由改成 history。
- 不要硬编码生产 API 地址。
- 不要删除前端兼容层后假设接口字段一定统一。
- 不要在一次改动里同时做业务重构和部署方案切换。
- 不要忽略 `server/data` 持久化目录。
- 不要只改代码不改文档。

## 13. 给后续代理的简明指令

- 任务开始先说清楚你改的是前端、后端，还是联动。
- 前端任务默认工作区是 `src/`。
- 后端任务默认工作区是 `server_net/`。
- 联动任务默认先从 `src/api/danmaku.ts` 开始。
- 遇到管理后台问题，默认优先审查 `src/views/AdminView.vue`。
- 遇到启动、录制、静态资源问题，默认优先审查 `server_net/Program.cs` 和 `server_net/Services/`。

如果代码与文档不一致，以当前代码和 `docs/` 最新说明共同判断，并在提交中补齐不一致说明。
