using Galaxy.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.Data;

public class GalaxyDbInitializer
{
    private readonly IServiceProvider _sp;

    public GalaxyDbInitializer(IServiceProvider sp) => _sp = sp;

    public async Task InitializeAsync(GalaxyConfig config)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GalaxyDbContext>();

        await db.Database.EnsureCreatedAsync();

        // 自动添加缺失的表（SQLite EnsureCreatedAsync 对已存在的数据库不做任何操作）
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await EnsureTableAsync(conn, "EmailVerifications", @"
                CREATE TABLE IF NOT EXISTS ""EmailVerifications"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Email"" TEXT NOT NULL DEFAULT '',
                    ""Code"" TEXT NOT NULL DEFAULT '',
                    ""ExpiresAt"" TEXT NOT NULL,
                    ""IsUsed"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
                )");
            await EnsureIndexAsync(conn, "IX_EmailVerifications_Email", "EmailVerifications", "Email");

            await EnsureTableAsync(conn, "DeveloperApplications", @"
                CREATE TABLE IF NOT EXISTS ""DeveloperApplications"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""UserId"" INTEGER NOT NULL,
                    ""DeveloperName"" TEXT NOT NULL DEFAULT '',
                    ""Purpose"" TEXT NOT NULL DEFAULT '',
                    ""WebsiteUrl"" TEXT NULL,
                    ""ContactInfo"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""ReviewNote"" TEXT NULL,
                    ""ReviewedAt"" TEXT NULL,
                    ""ReviewedBy"" INTEGER NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    FOREIGN KEY(""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
                )");

            await EnsureTableAsync(conn, "OAuthApps", @"
                CREATE TABLE IF NOT EXISTS ""OAuthApps"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""DeveloperUserId"" INTEGER NOT NULL,
                    ""ClientId"" TEXT NOT NULL DEFAULT '',
                    ""ClientSecretHash"" TEXT NOT NULL DEFAULT '',
                    ""ClientSecretPrefix"" TEXT NOT NULL DEFAULT '',
                    ""AppName"" TEXT NOT NULL DEFAULT '',
                    ""RedirectUris"" TEXT NOT NULL DEFAULT '[]',
                    ""Scopes"" TEXT NOT NULL DEFAULT '[]',
                    ""IsDisabled"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    FOREIGN KEY(""DeveloperUserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
                )");
            await EnsureIndexAsync(conn, "IX_OAuthApps_ClientId", "OAuthApps", "ClientId", unique: true);

            await EnsureTableAsync(conn, "OAuthAuthorizationCodes", @"
                CREATE TABLE IF NOT EXISTS ""OAuthAuthorizationCodes"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Code"" TEXT NOT NULL DEFAULT '',
                    ""OAuthAppId"" INTEGER NOT NULL,
                    ""UserId"" INTEGER NOT NULL,
                    ""Scopes"" TEXT NOT NULL DEFAULT '[]',
                    ""RedirectUri"" TEXT NOT NULL DEFAULT '',
                    ""ExpiresAt"" TEXT NOT NULL,
                    ""IsUsed"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    FOREIGN KEY(""OAuthAppId"") REFERENCES ""OAuthApps""(""Id"") ON DELETE CASCADE,
                    FOREIGN KEY(""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
                )");
            await EnsureIndexAsync(conn, "IX_OAuthAuthorizationCodes_Code", "OAuthAuthorizationCodes", "Code", unique: true);

            await EnsureTableAsync(conn, "OAuthAccessTokens", @"
                CREATE TABLE IF NOT EXISTS ""OAuthAccessTokens"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""TokenHash"" TEXT NOT NULL DEFAULT '',
                    ""TokenPrefix"" TEXT NOT NULL DEFAULT '',
                    ""OAuthAppId"" INTEGER NOT NULL,
                    ""UserId"" INTEGER NOT NULL,
                    ""Scopes"" TEXT NOT NULL DEFAULT '[]',
                    ""ExpiresAt"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    FOREIGN KEY(""OAuthAppId"") REFERENCES ""OAuthApps""(""Id"") ON DELETE CASCADE,
                    FOREIGN KEY(""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
                )");
            await EnsureIndexAsync(conn, "IX_OAuthAccessTokens_TokenHash", "OAuthAccessTokens", "TokenHash", unique: true);

            // 自动添加缺失的列
            await EnsureColumnAsync(conn, "Mods", "Status", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, "Mods", "ReviewNote", "TEXT NULL");
            await EnsureColumnAsync(conn, "Users", "IsDeveloper", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, "Users", "DeveloperApplicationId", "INTEGER NULL");
            await EnsureColumnAsync(conn, "ApiKeys", "Permissions", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, "ApiKeys", "IsSystem", "INTEGER NOT NULL DEFAULT 0");
        }
        finally
        {
            await conn.CloseAsync();
        }

        // 确保注册开放设置存在
        if (!await db.SystemSettings.AnyAsync(s => s.Key == "registration_open"))
        {
            db.SystemSettings.Add(new SystemSetting { Key = "registration_open", Value = "true" });
            await db.SaveChangesAsync();
        }

        // 确保SMTP相关设置存在
        var defaultSettings = new Dictionary<string, string>
        {
            ["smtp_host"] = "",
            ["smtp_port"] = "587",
            ["smtp_username"] = "",
            ["smtp_password"] = "",
            ["smtp_from"] = "",
            ["smtp_enabled"] = "false",
            ["default_permissions"] = JsonSerializer.Serialize(GalaxyPermissions.Default),
            ["developer_require_approval"] = "true",
        };
        foreach (var (key, value) in defaultSettings)
        {
            if (!await db.SystemSettings.AnyAsync(s => s.Key == key))
            {
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
            }
        }
        await db.SaveChangesAsync();

        // 创建默认管理员
        if (!await db.Users.AnyAsync(u => u.Username == config.AdminUsername))
        {
            var admin = new User
            {
                Username = config.AdminUsername,
                Email = "admin@galaxy.local",
                PasswordHash = HashPassword(config.AdminPassword),
                Permissions = JsonSerializer.Serialize(GalaxyPermissions.Admin),
                IsDisabled = false
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

        // 为已有用户补建系统 API KEY
        var usersWithoutSystemKey = await db.Users
            .Where(u => !db.ApiKeys.Any(a => a.UserId == u.Id && a.IsSystem))
            .ToListAsync();
        foreach (var user in usersWithoutSystemKey)
        {
            var rawKey = $"gxy_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_")[..43]}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            var prefix = rawKey[..8];
            db.ApiKeys.Add(new ApiKey
            {
                UserId = user.Id,
                KeyHash = Convert.ToBase64String(hash),
                KeyPrefix = prefix,
                Name = "系统",
                Permissions = user.Permissions,
                IsSystem = true
            });
        }
        await db.SaveChangesAsync();
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return $"$v1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[1] != "v1") return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string EncryptSmtpPassword(string plainText, string key)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(Encoding.UTF8.GetBytes(plainText), 0, plainText.Length);
        var result = new byte[aes.IV.Length + encrypted.Length];
        aes.IV.CopyTo(result, 0);
        encrypted.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    public static string DecryptSmtpPassword(string encryptedText, string key)
    {
        var bytes = Convert.FromBase64String(encryptedText);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var iv = new byte[16];
        Array.Copy(bytes, 0, iv, 0, 16);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(bytes, 16, bytes.Length - 16);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static async Task EnsureColumnAsync(System.Data.Common.DbConnection conn, string table, string column, string definition)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        var exists = false;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            await alterCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureTableAsync(System.Data.Common.DbConnection conn, string tableName, string createSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        var exists = (await cmd.ExecuteScalarAsync()) is not null;
        if (!exists)
        {
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = createSql.Trim();
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureIndexAsync(System.Data.Common.DbConnection conn, string indexName, string tableName, string column, bool unique = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{indexName}'";
        var exists = (await cmd.ExecuteScalarAsync()) is not null;
        if (!exists)
        {
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"CREATE {(unique ? "UNIQUE" : "")} INDEX \"{indexName}\" ON \"{tableName}\" (\"{column}\")";
            await createCmd.ExecuteNonQueryAsync();
        }
    }
}
