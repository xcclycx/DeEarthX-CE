using Galaxy.Core;
using Galaxy.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.OAuth;

public class OAuthAppService
{
    private readonly IDbContextFactory<GalaxyDbContext> _dbFactory;

    public OAuthAppService(IDbContextFactory<GalaxyDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // 创建 OAuth 应用
    public async Task<GalaxyResult<OAuthAppCreateResult>> CreateAppAsync(int userId, string appName, List<string> redirectUris, List<string> scopes)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users.FindAsync(userId);
        if (user is null || !user.IsDeveloper)
            return GalaxyResult<OAuthAppCreateResult>.Error(403, "仅开发者可创建 OAuth 应用");

        if (string.IsNullOrWhiteSpace(appName))
            return GalaxyResult<OAuthAppCreateResult>.Error(400, "应用名称不能为空");
        if (redirectUris.Count == 0)
            return GalaxyResult<OAuthAppCreateResult>.Error(400, "至少需要一个回调地址");

        var clientId = $"gxy_client_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).Replace("+", "-").Replace("/", "_").Replace("=", "")}";
        var rawSecret = $"gxy_secret_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").Replace("=", "")}";
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
        var secretPrefix = rawSecret[..12];

        var oauthApp = new OAuthApp
        {
            DeveloperUserId = userId,
            ClientId = clientId,
            ClientSecretHash = Convert.ToBase64String(secretHash),
            ClientSecretPrefix = secretPrefix,
            AppName = appName,
            RedirectUris = JsonSerializer.Serialize(redirectUris),
            Scopes = JsonSerializer.Serialize(scopes),
            IsDisabled = false
        };
        db.OAuthApps.Add(oauthApp);
        await db.SaveChangesAsync();

        return GalaxyResult<OAuthAppCreateResult>.Ok(new OAuthAppCreateResult
        {
            Id = oauthApp.Id,
            ClientId = clientId,
            ClientSecret = rawSecret,
            AppName = appName,
            RedirectUris = redirectUris,
            Scopes = scopes
        });
    }

    // 列出我的 OAuth 应用
    public async Task<GalaxyResult<List<OAuthAppListItem>>> ListMyAppsAsync(int userId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var apps = await db.OAuthApps
            .Where(a => a.DeveloperUserId == userId)
            .Select(a => new OAuthAppListItem
            {
                Id = a.Id,
                ClientId = a.ClientId,
                AppName = a.AppName,
                RedirectUris = a.RedirectUris,
                Scopes = a.Scopes,
                IsDisabled = a.IsDisabled,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return GalaxyResult<List<OAuthAppListItem>>.Ok(apps);
    }

    // 更新 OAuth 应用
    public async Task<GalaxyResult> UpdateAppAsync(int userId, int appId, string? appName, List<string>? redirectUris, List<string>? scopes)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FirstOrDefaultAsync(a => a.Id == appId && a.DeveloperUserId == userId);
        if (app is null) return GalaxyResult.Error(404, "应用不存在");

        if (appName is not null) app.AppName = appName;
        if (redirectUris is not null) app.RedirectUris = JsonSerializer.Serialize(redirectUris);
        if (scopes is not null) app.Scopes = JsonSerializer.Serialize(scopes);
        await db.SaveChangesAsync();

        return GalaxyResult.Ok("应用已更新");
    }

    // 删除 OAuth 应用
    public async Task<GalaxyResult> DeleteAppAsync(int userId, int appId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FirstOrDefaultAsync(a => a.Id == appId && a.DeveloperUserId == userId);
        if (app is null) return GalaxyResult.Error(404, "应用不存在");

        db.OAuthApps.Remove(app);
        await db.SaveChangesAsync();
        return GalaxyResult.Ok("应用已删除");
    }

    // 管理员：列出所有 OAuth 应用
    public async Task<GalaxyResult<List<OAuthAppAdminItem>>> ListAllAppsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var apps = await db.OAuthApps
            .Include(a => a.DeveloperUser)
            .Select(a => new OAuthAppAdminItem
            {
                Id = a.Id,
                DeveloperUserId = a.DeveloperUserId,
                ClientId = a.ClientId,
                AppName = a.AppName,
                DeveloperUsername = a.DeveloperUser.Username,
                RedirectUris = a.RedirectUris,
                Scopes = a.Scopes,
                IsDisabled = a.IsDisabled,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return GalaxyResult<List<OAuthAppAdminItem>>.Ok(apps);
    }

    // 按开发者用户 ID 查询应用
    public async Task<GalaxyResult<List<OAuthAppAdminItem>>> ListAppsByDeveloperAsync(int developerUserId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var apps = await db.OAuthApps
            .Include(a => a.DeveloperUser)
            .Where(a => a.DeveloperUserId == developerUserId)
            .Select(a => new OAuthAppAdminItem
            {
                Id = a.Id,
                DeveloperUserId = a.DeveloperUserId,
                ClientId = a.ClientId,
                AppName = a.AppName,
                DeveloperUsername = a.DeveloperUser.Username,
                RedirectUris = a.RedirectUris,
                Scopes = a.Scopes,
                IsDisabled = a.IsDisabled,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return GalaxyResult<List<OAuthAppAdminItem>>.Ok(apps);
    }

    // 管理员：禁用/启用 OAuth 应用
    public async Task<GalaxyResult> ToggleAppAsync(int appId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.OAuthApps.FindAsync(appId);
        if (app is null) return GalaxyResult.Error(404, "应用不存在");

        app.IsDisabled = !app.IsDisabled;
        await db.SaveChangesAsync();
        return GalaxyResult.Ok(app.IsDisabled ? "应用已禁用" : "应用已启用");
    }
}

public class OAuthAppCreateResult
{
    public int Id { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AppName { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
    public List<string> Scopes { get; set; } = [];
}

public class OAuthAppListItem
{
    public int Id { get; set; }
    public string ClientId { get; set; } = "";
    public string AppName { get; set; } = "";
    public string RedirectUris { get; set; } = "[]";
    public string Scopes { get; set; } = "[]";
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OAuthAppAdminItem
{
    public int Id { get; set; }
    public int DeveloperUserId { get; set; }
    public string ClientId { get; set; } = "";
    public string AppName { get; set; } = "";
    public string DeveloperUsername { get; set; } = "";
    public string RedirectUris { get; set; } = "[]";
    public string Scopes { get; set; } = "[]";
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
}
