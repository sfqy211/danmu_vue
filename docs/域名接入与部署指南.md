# 域名接入与部署指南

这份文档用于帮助第一次接手本项目的人完成新域名接入与上线。

本文档基于以下部署方案：

- 前端静态页面：腾讯云 COS + CDN
- 后端 API：腾讯云轻量服务器 + 1Panel + Docker
- 域名结构：
  - `ovodm.top`：前端站点
  - `www.ovodm.top`：前端站点别名
  - `api.ovodm.top`：后端 API

本文档不包含任何密钥、账号、Cookie、Token 等隐私信息。

## 1. 项目部署结构说明

本项目当前采用两条发布链路：

- 前端发布：
  - GitHub Actions 构建前端
  - 将 `dist` 上传到腾讯云 COS
  - 再刷新腾讯云 CDN 缓存
- 后端发布：
  - GitHub Actions 通过 SSH 登录服务器
  - 在服务器上执行 `docker-compose up --build -d`

也就是说：

- 前端页面不直接由服务器提供
- 服务器主要负责 API 服务

## 2. 需要准备的内容

开始前请先准备好：

- 已备案的域名
- 腾讯云 DNSPod
- 腾讯云 COS
- 腾讯云 CDN
- 一台已安装 1Panel 的服务器
- GitHub 仓库管理员权限

推荐域名规划：

- `ovodm.top`：前端主站
- `www.ovodm.top`：前端备用网址
- `api.ovodm.top`：后端接口

## 3. DNS 解析配置

在腾讯云 DNS 里添加以下记录：

| 主机记录 | 记录类型 | 记录值 |
| --- | --- | --- |
| `@` | `CNAME` | 腾讯云 CDN 分配的 CNAME |
| `www` | `CNAME` | 腾讯云 CDN 分配的 CNAME |
| `api` | `A` | 服务器公网 IP |

说明：

- `@` 和 `www` 指向 CDN
- `api` 直接指向服务器

## 4. 创建 COS 存储桶

在腾讯云 COS 中创建存储桶。

建议：

- 地域：选择和服务器相同区域，例如 `上海`
- 名称：例如 `ovodm-web`
- 访问权限：按实际需求选择，常见为 `公有读私有写`

注意：

- 创建后系统会生成完整桶名，例如 `ovodm-web-1316468658`
- GitHub Actions 中要使用完整桶名，不是只填前半段

## 5. 上传前端构建产物到 COS

将前端打包后的 `dist` 目录内容上传到 COS 存储桶根目录。

根目录通常至少应包含：

- `index.html`
- `assets/`
- 其他前端构建生成的静态文件

## 6. 腾讯云 CDN 配置

为前端主站配置 CDN：

- 加速域名：`ovodm.top`
- 加速类型：`CDN 网页小文件`
- 源站类型：`COS源`
- 回源协议：建议 `HTTPS`

如果还需要 `www.ovodm.top`，也应一并接入或做跳转。

### 6.1 根路径访问报错的处理

如果访问：

- `https://ovodm.top/index.html#/` 可以打开
- 但 `https://ovodm.top/` 打不开

通常是因为根路径 `/` 没有回源到 `index.html`。

此时需要在 CDN 中添加回源 URL 重写规则：

- 待重写回源 URL：`/`
- 目标回源 Host：COS 源站域名
- 目标回源 Path：`/index.html`

保存后等待配置生效，再重新访问首页。

## 7. 服务器 API 配置

后端服务使用 Docker 运行，当前常见端口关系如下：

- 宿主机端口：`5200`
- 容器内端口：`3001`

也就是说：

- 服务器本机访问 `http://127.0.0.1:5200`
- 实际会转到容器内的 .NET 服务

## 8. 1Panel 配置 API 站点

在 1Panel 中创建一个反向代理站点：

- 域名：`api.ovodm.top`
- 类型：反向代理
- 前端请求路径：`/`
- 后端代理地址：`http://127.0.0.1:5200`
- 站点端口：`80`

测试地址：

```text
http://api.ovodm.top/api/streamers
```

如果能返回 JSON，说明 API 反代成功。

## 9. 1Panel 配置 HTTPS

为 `api.ovodm.top` 申请并部署证书。

推荐配置：

- 启用 HTTPS：开启
- HTTPS 端口：`443`
- HTTP 选项：自动跳转到 HTTPS

注意：

- HTTPS 端口必须是 `443`
- 不要把 HTTPS 端口写成 `80`
- 腾讯云安全组和服务器防火墙需要放行 `443`

测试地址：

```text
https://api.ovodm.top/api/streamers
```

如果能返回 JSON，说明 HTTPS API 配置成功。

## 10. GitHub Actions Secrets 配置

进入 GitHub 仓库：

- `Settings`
- `Secrets and variables`
- `Actions`

确认以下 Secrets 已正确配置：

| 名称 | 示例值 |
| --- | --- |
| `VITE_API_BASE_URL` | `https://api.ovodm.top` |
| `VITE_ADMIN_API_BASE_URL` | `https://api.ovodm.top` |
| `CDN_PATHS` | `https://ovodm.top/` |
| `COS_BUCKET` | `ovodm-web-1316468658` |
| `COS_REGION` | `ap-shanghai` |
| `TENCENT_SECRET_ID` | 腾讯云密钥 |
| `TENCENT_SECRET_KEY` | 腾讯云密钥 |

说明：

- `COS_BUCKET` 必须填完整桶名
- `COS_REGION` 必须填地域编码，例如 `ap-shanghai`
- 如果桶名或地域填错，前端自动上传会报 `NoSuchBucket`

## 11. 自动部署工作流说明

### 11.1 前端工作流

文件：

- `.github/workflows/deploy-frontend.yml`

作用：

- 构建前端
- 上传 `dist` 到 COS
- 刷新 CDN 缓存

### 11.2 后端工作流

文件：

- `.github/workflows/deploy.yml`

作用：

- SSH 登录服务器
- 拉取最新代码
- 执行 `docker-compose up --build -d`

## 12. 常见问题排查

### 12.1 首页打开报 `AccessDenied`

常见原因：

- CDN 根路径没有重写到 `index.html`
- COS 静态页面入口未正确配置

优先检查：

- `https://ovodm.top/index.html#/` 是否可访问
- CDN 是否配置 `/ -> /index.html` 回源重写

### 12.2 前端请求 `/api/...` 返回 `NoSuchKey`

现象：

- 浏览器访问的是 `https://ovodm.top/api/...`
- 返回 COS XML 报错

原因：

- 前端没有使用独立 API 域名
- `VITE_API_BASE_URL` 未正确配置

解决：

- 将 `VITE_API_BASE_URL` 和 `VITE_ADMIN_API_BASE_URL` 改为 `https://api.ovodm.top`
- 重新触发前端工作流

### 12.3 GitHub Actions 报 `NoSuchBucket`

原因通常是：

- `COS_BUCKET` 填错
- `COS_REGION` 填错

正确示例：

- `COS_BUCKET=ovodm-web-1316468658`
- `COS_REGION=ap-shanghai`

### 12.4 API 请求一直 pending 或 HTTPS 不通

优先检查：

- `api.ovodm.top` DNS 是否已生效
- 1Panel 反向代理是否指向 `http://127.0.0.1:5200`
- HTTPS 端口是否为 `443`
- 安全组和防火墙是否放行 `443`
- 证书是否绑定到 `api.ovodm.top`

## 13. 上线检查清单

正式上线前请逐项确认：

- `https://ovodm.top/` 可以正常打开首页
- `https://www.ovodm.top/` 可以正常访问或跳转
- `https://api.ovodm.top/api/streamers` 返回正常
- 前端页面中的接口请求已经改为 `https://api.ovodm.top/api/...`
- GitHub Actions 前端工作流可以成功上传到 COS
- GitHub Actions 后端工作流可以成功更新服务器
- 腾讯云安全组已放行 `80` 和 `443`
- 1Panel 中 API 站点 HTTPS 端口为 `443`

## 14. 维护建议

- 新接手项目时，先确认 GitHub Secrets 是否完整
- 不要在服务器上手改仓库代码，自动部署可能会覆盖本地修改
- 涉及域名切换时，优先确认前端域名、API 域名、COS 桶名、CDN 域名是否一一对应
- 推送前先确认当前修改是否符合现有部署方案，避免把不同部署方案的配置混在一起

