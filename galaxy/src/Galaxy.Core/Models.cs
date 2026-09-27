namespace Galaxy.Core;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Permissions { get; set; } = "[]"; // JSON array of permission strings
    public bool IsDisabled { get; set; }
    public bool IsDeveloper { get; set; }
    public int? DeveloperApplicationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ApiKey> ApiKeys { get; set; } = [];
    public DeveloperApplication? DeveloperApplication { get; set; }
}

public class ApiKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string KeyHash { get; set; } = "";
    public string KeyPrefix { get; set; } = ""; // 前8位用于识别
    public string Name { get; set; } = "";
    public string Permissions { get; set; } = "[]"; // JSON数组，该KEY拥有的权限
    public bool IsSystem { get; set; } // 标记是否为系统KEY
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
}

public class Mod
{
    public int Id { get; set; }
    public string ModId { get; set; } = ""; // 唯一标识
    public bool ClientOk { get; set; }
    public bool ServerOk { get; set; }
    public int SubmitCount { get; set; }
    public string? Note { get; set; }
    public int SubmittedBy { get; set; }
    public ModStatus Status { get; set; } = ModStatus.Pending;
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ModStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class SystemSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class EmailVerification
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DeveloperApplication
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string DeveloperName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string? WebsiteUrl { get; set; }
    public string? ContactInfo { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
}

public enum ApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class OAuthApp
{
    public int Id { get; set; }
    public int DeveloperUserId { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecretHash { get; set; } = "";
    public string ClientSecretPrefix { get; set; } = "";
    public string AppName { get; set; } = "";
    public string RedirectUris { get; set; } = "[]";
    public string Scopes { get; set; } = "[]";
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User DeveloperUser { get; set; } = null!;
    public List<OAuthAuthorizationCode> AuthorizationCodes { get; set; } = [];
    public List<OAuthAccessToken> AccessTokens { get; set; } = [];
}

public class OAuthAuthorizationCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int OAuthAppId { get; set; }
    public int UserId { get; set; }
    public string Scopes { get; set; } = "[]";
    public string RedirectUri { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public OAuthApp OAuthApp { get; set; } = null!;
    public User User { get; set; } = null!;
}

public class OAuthAccessToken
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = "";
    public string TokenPrefix { get; set; } = "";
    public int OAuthAppId { get; set; }
    public int UserId { get; set; }
    public string Scopes { get; set; } = "[]";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public OAuthApp OAuthApp { get; set; } = null!;
    public User User { get; set; } = null!;
}

public static class GalaxyScopes
{
    public const string UserRead = "user:read";
    public const string ModRead = "mod:read";
    public const string ModSubmit = "mod:submit";
    public const string ModQuery = "mod.query";
    public const string ModManage = "mod.manage";
    public const string UserManage = "user.manage";
    public const string SystemSettings = "system.settings";
    public const string ApiKeyManage = "apikey.manage";
    public const string OAuth2Manage = "oauth2.manage";
    public const string DeveloperApply = "developer.apply";

    public static readonly string[] All = [UserRead, ModRead, ModSubmit, ModQuery, ModManage, UserManage, SystemSettings, ApiKeyManage, OAuth2Manage, DeveloperApply];

    // scope 与权限的映射
    private static readonly Dictionary<string, string> ScopeToPermissionMap = new()
    {
        [UserRead] = GalaxyPermissions.UserManage,     // user:read 需要 user.manage 权限
        [ModRead] = GalaxyPermissions.ModQuery,         // mod:read 需要 mod.query 权限
        [ModSubmit] = GalaxyPermissions.ModSubmit,
        [ModQuery] = GalaxyPermissions.ModQuery,
        [ModManage] = GalaxyPermissions.ModManage,
        [UserManage] = GalaxyPermissions.UserManage,
        [SystemSettings] = GalaxyPermissions.SystemSettings,
        [ApiKeyManage] = GalaxyPermissions.ApiKeyManage,
        [OAuth2Manage] = GalaxyPermissions.OAuth2Manage,
        [DeveloperApply] = GalaxyPermissions.DeveloperApply,
    };

    public static string? PermissionForScope(string scope) => ScopeToPermissionMap.TryGetValue(scope, out var perm) ? perm : null;
}
