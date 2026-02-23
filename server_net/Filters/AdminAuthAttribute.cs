using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Danmu.Server.Filters;

public class AdminAuthAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var req = context.HttpContext.Request;
        
        // Allow OPTIONS
        if (req.Method == "OPTIONS") return;

        var token = req.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "") 
                    ?? req.Query["token"].FirstOrDefault();
        
        var adminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN");

        if (string.IsNullOrEmpty(adminToken))
        {
            context.Result = new ObjectResult(new { error = "Server configuration error: ADMIN_TOKEN missing" }) { StatusCode = 500 };
            return;
        }

        if (token != adminToken)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
