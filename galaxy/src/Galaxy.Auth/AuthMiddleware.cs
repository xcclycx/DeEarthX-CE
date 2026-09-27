using Galaxy.Core;
using Galaxy.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.Auth;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // 公开路径不需要认证
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // 尝试从 Authorization header 获取认证信息
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader))
        {
            await WriteUnauthorized(context, "未认证");
            return;
        }

        var bearerToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : "";

        // 尝试 OAuth2 token 认证 (Bearer gxyo_xxx)
        if (bearerToken.StartsWith("gxyo_", StringComparison.OrdinalIgnoreCase))
        {
            var dbFactory = context.RequestServices.GetRequiredService<IDbContextFactory<GalaxyDbContext>>();
            using var db = await dbFactory.CreateDbContextAsync();
            var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken)));
            var oauthToken = await db.OAuthAccessTokens
                .Include(t => t.User)
                .Include(t => t.OAuthApp)
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (oauthToken is not null && oauthToken.ExpiresAt > DateTime.UtcNow && !oauthToken.User.IsDisabled && !oauthToken.OAuthApp.IsDisabled)
            {
                context.Items["User"] = oauthToken.User;
                context.Items["AuthType"] = "oauth";
                context.Items["OAuthScopes"] = oauthToken.Scopes;
                await _next(context);
                return;
            }
            await WriteUnauthorized(context, "OAuth Token 无效或已过期");
            return;
        }

        // 尝试 API Key 认证 (Bearer gxy_xxx)
        if (bearerToken.StartsWith("gxy_", StringComparison.OrdinalIgnoreCase))
        {
            var authService = context.RequestServices.GetRequiredService<AuthService>();
            var apiKey = await authService.ValidateApiKeyWithPermissionsAsync(bearerToken);
            if (apiKey is not null)
            {
                context.Items["User"] = apiKey.User;
                context.Items["AuthType"] = "apikey";
                context.Items["ApiKeyPermissions"] = apiKey.Permissions;
                await _next(context);
                return;
            }
            await WriteUnauthorized(context, "API Key 无效");
            return;
        }

        // 尝试 JWT 认证
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var config = context.RequestServices.GetRequiredService<GalaxyConfig>();
            var handler = new JwtSecurityTokenHandler();
            try
            {
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.JwtSecret))
                };
                var principal = handler.ValidateToken(bearerToken, parameters, out _);
                context.Items["User"] = principal;
                context.Items["AuthType"] = "jwt";
                await _next(context);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Galaxy.Auth] JWT 验证失败: {ex.Message}");
                await WriteUnauthorized(context, "认证失败");
                return;
            }
        }

        await WriteUnauthorized(context, "未认证");
    }

    private static bool IsPublicPath(string path)
    {
        if (path == "/" || path == "") return true;
        var publicPrefixes = new[]
        {
            "/api/auth/login",
            "/api/auth/register",
            "/api/auth/settings",
            "/api/auth/send-verify-code",
            "/api/oauth2/token",
        };
        foreach (var prefix in publicPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // GET /api/mod/search, /api/mod/{modId}, /api/mod/stats 是公开的
        if (path.StartsWith("/api/mod/search", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/mod/stats", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // /api/mod/xxx (GET 查询单个 mod) 是公开的
        if (path.StartsWith("/api/mod/") && !path.StartsWith("/api/mod/submit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 静态文件
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static async Task WriteUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 401, message }));
    }
}
