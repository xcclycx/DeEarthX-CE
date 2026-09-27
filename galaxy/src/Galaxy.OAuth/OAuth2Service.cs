using Galaxy.Core;
using Galaxy.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.OAuth;

public class OAuth2Service
{
    private readonly IDbContextFactory<GalaxyDbContext> _dbFactory;

    public OAuth2Service(IDbContextFactory<GalaxyDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // 授权码流程：创建授权码
    public async Task<GalaxyResult<string>> CreateAuthorizationCodeAsync(int userId, string clientId, string redirectUri, string scopes, string state)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == clientId && !a.IsDisabled);
        if (app is null)
            return GalaxyResult<string>.Error(400, "应用不存在或已禁用");

        // 验证 redirect_uri
        var allowedUris = JsonSerializer.Deserialize<List<string>>(app.RedirectUris) ?? [];
        if (!allowedUris.Contains(redirectUri))
            return GalaxyResult<string>.Error(400, "回调地址不匹配");

        // 验证开发者身份
        var developer = await db.Users.FindAsync(app.DeveloperUserId);
        if (developer is null || !developer.IsDeveloper)
            return GalaxyResult<string>.Error(403, "应用开发者已被撤销");

        // 验证授权用户是否拥有请求的 scope 对应的权限
        var user = await db.Users.FindAsync(userId);
        if (user is null || user.IsDisabled)
            return GalaxyResult<string>.Error(403, "用户无效");

        var requestedScopes = JsonSerializer.Deserialize<List<string>>(scopes) ?? [];
        var userPerms = JsonSerializer.Deserialize<List<string>>(user.Permissions) ?? [];
        foreach (var scope in requestedScopes)
        {
            var requiredPerm = GalaxyScopes.PermissionForScope(scope);
            if (requiredPerm is not null && !userPerms.Contains(requiredPerm))
                return GalaxyResult<string>.Error(403, $"您没有 {scope} 权限，无法授权");
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").Replace("=", "")[..40];
        var authCode = new OAuthAuthorizationCode
        {
            Code = code,
            OAuthAppId = app.Id,
            UserId = userId,
            Scopes = scopes,
            RedirectUri = redirectUri,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false
        };
        db.OAuthAuthorizationCodes.Add(authCode);
        await db.SaveChangesAsync();

        return GalaxyResult<string>.Ok(code);
    }

    // 授权码流程：用授权码换取 access_token
    public async Task<GalaxyResult<OAuthTokenResult>> ExchangeCodeForTokenAsync(string code, string clientId, string clientSecret, string redirectUri)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == clientId);
        if (app is null)
            return GalaxyResult<OAuthTokenResult>.Error(400, "应用不存在");

        // 验证 client_secret
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret));
        if (app.ClientSecretHash != Convert.ToBase64String(secretHash))
            return GalaxyResult<OAuthTokenResult>.Error(400, "Client Secret 不匹配");

        var authCode = await db.OAuthAuthorizationCodes
            .FirstOrDefaultAsync(c => c.Code == code && c.OAuthAppId == app.Id && !c.IsUsed);

        if (authCode is null)
            return GalaxyResult<OAuthTokenResult>.Error(400, "授权码无效");
        if (authCode.ExpiresAt < DateTime.UtcNow)
            return GalaxyResult<OAuthTokenResult>.Error(400, "授权码已过期");
        if (authCode.RedirectUri != redirectUri)
            return GalaxyResult<OAuthTokenResult>.Error(400, "回调地址不匹配");

        // 标记授权码已使用
        authCode.IsUsed = true;

        // 生成 access_token
        var rawToken = $"gxyo_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_")[..43]}";
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var tokenPrefix = rawToken[..8];

        var accessToken = new OAuthAccessToken
        {
            TokenHash = Convert.ToBase64String(tokenHash),
            TokenPrefix = tokenPrefix,
            OAuthAppId = app.Id,
            UserId = authCode.UserId,
            Scopes = authCode.Scopes,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        db.OAuthAccessTokens.Add(accessToken);
        await db.SaveChangesAsync();

        return GalaxyResult<OAuthTokenResult>.Ok(new OAuthTokenResult
        {
            AccessToken = rawToken,
            TokenType = "Bearer",
            ExpiresIn = 86400,
            Scope = authCode.Scopes
        });
    }

    // 验证 OAuth App 信息（用于授权确认页）
    public async Task<GalaxyResult<OAuthAppInfo>> GetAppInfoForAuthorizationAsync(string clientId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FirstOrDefaultAsync(a => a.ClientId == clientId && !a.IsDisabled);
        if (app is null)
            return GalaxyResult<OAuthAppInfo>.Error(400, "应用不存在");

        var developer = await db.Users.FindAsync(app.DeveloperUserId);
        return GalaxyResult<OAuthAppInfo>.Ok(new OAuthAppInfo
        {
            ClientId = app.ClientId,
            AppName = app.AppName,
            Scopes = app.Scopes,
            DeveloperName = developer?.Username ?? ""
        });
    }
}

public class OAuthTokenResult
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string Scope { get; set; } = "[]";
}

public class OAuthAppInfo
{
    public string ClientId { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Scopes { get; set; } = "[]";
    public string DeveloperName { get; set; } = "";
}
