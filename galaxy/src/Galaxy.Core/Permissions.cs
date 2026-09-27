namespace Galaxy.Core;

public static class GalaxyPermissions
{
    public const string ModSubmit = "mod.submit";
    public const string ModQuery = "mod.query";
    public const string ModManage = "mod.manage";
    public const string UserManage = "user.manage";
    public const string SystemSettings = "system.settings";
    public const string ApiKeyManage = "apikey.manage";
    public const string OAuth2Manage = "oauth2.manage";
    public const string DeveloperApply = "developer.apply";

    public static readonly string[] All = [ModSubmit, ModQuery, ModManage, UserManage, SystemSettings, ApiKeyManage, OAuth2Manage, DeveloperApply];
    public static readonly string[] Default = [ModSubmit, ModQuery, ApiKeyManage, DeveloperApply];
    public static readonly string[] Admin = All;
    public static readonly string[] Developer = [OAuth2Manage];
}
