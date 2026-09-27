using Galaxy.Core;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;

namespace Galaxy.Auth;

public static class PermissionMiddleware
{
    public static async Task RequirePermission(HttpContext context, string permission, Func<Task> next)
    {
        var user = context.Items["User"];
        if (user is null)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 401, message = "未认证" }));
            return;
        }

        var authType = context.Items["AuthType"] as string;

        // OAuth token: 校验 scope 映射
        if (authType == "oauth")
        {
            var scopesJson = context.Items["OAuthScopes"] as string ?? "[]";
            var scopes = JsonSerializer.Deserialize<List<string>>(scopesJson) ?? [];
            var requiredScope = PermissionToScope(permission);
            if (requiredScope is not null && !scopes.Contains(requiredScope))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 403, message = "权限不足" }));
                return;
            }
            await next();
            return;
        }

        // API Key: 校验 ApiKey 级别权限
        if (authType == "apikey")
        {
            var apiKeyPermsJson = context.Items["ApiKeyPermissions"] as string ?? "[]";
            var apiKeyPerms = JsonSerializer.Deserialize<List<string>>(apiKeyPermsJson) ?? [];
            if (!apiKeyPerms.Contains(permission))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 403, message = "权限不足" }));
                return;
            }
            await next();
            return;
        }

        // JWT: 校验用户权限
        string permissionsJson;
        if (user is ClaimsPrincipal principal)
        {
            permissionsJson = principal.FindFirst("permissions")?.Value ?? "[]";
        }
        else if (user is Core.User u)
        {
            permissionsJson = u.Permissions;
        }
        else
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 403, message = "权限不足" }));
            return;
        }

        var permissions = JsonSerializer.Deserialize<List<string>>(permissionsJson) ?? [];
        if (!permissions.Contains(permission))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 403, message = "权限不足" }));
            return;
        }

        await next();
    }

    private static string? PermissionToScope(string permission)
    {
        // 反向映射：权限 → 最小 scope
        return permission switch
        {
            _ when permission == GalaxyPermissions.ModSubmit => GalaxyScopes.ModSubmit,
            _ when permission == GalaxyPermissions.ModQuery => GalaxyScopes.ModRead,
            _ when permission == GalaxyPermissions.ModManage => GalaxyScopes.ModManage,
            _ when permission == GalaxyPermissions.UserManage => GalaxyScopes.UserRead,
            _ when permission == GalaxyPermissions.SystemSettings => GalaxyScopes.SystemSettings,
            _ when permission == GalaxyPermissions.ApiKeyManage => GalaxyScopes.ApiKeyManage,
            _ when permission == GalaxyPermissions.OAuth2Manage => GalaxyScopes.OAuth2Manage,
            _ when permission == GalaxyPermissions.DeveloperApply => GalaxyScopes.DeveloperApply,
            _ => null
        };
    }
}
