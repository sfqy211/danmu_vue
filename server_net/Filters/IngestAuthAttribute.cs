using System.Security.Cryptography;
using System.Text;
using Danmu.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Filters;

/// <summary>
/// 采集端上报鉴权。与 <see cref="AdminAuthAttribute"/> 分离，避免把可操作后台的
/// admin token 分发给外部采集节点。
///
/// 采集端在 <c>X-Node-Token</c> 头里带明文 Token；服务端只保存其 SHA256，
/// 用数据库中的 ingest_nodes 记录校验，并顺带刷新 last_seen_at。
/// </summary>
public class IngestAuthAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Node-Token";
    /// <summary>通过校验后写入 HttpContext.Items 的节点标识键。</summary>
    public const string NodeIdItemKey = "IngestNodeId";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var req = context.HttpContext.Request;
        if (HttpMethods.IsOptions(req.Method)) return;

        // 三种来源都接受，复用同一套节点凭据：
        // - X-Node-Token：本中心 /api/ingest 的原生约定
        // - Token：danmakus-client 的 AccountApiClient 固定发这个头
        // - token query：个别客户端在 WS 握手/EventSource 场景无法改头
        var token = req.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = req.Headers["Token"].FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            token = req.Query["token"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        var db = context.HttpContext.RequestServices.GetRequiredService<DanmuContext>();
        var node = await db.IngestNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.TokenHash == hash && n.IsEnabled);

        if (node == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items[NodeIdItemKey] = node.NodeId;
    }
}
