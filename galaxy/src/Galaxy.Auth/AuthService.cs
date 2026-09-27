using Galaxy.Core;
using Galaxy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.Auth;

public class AuthService
{
    private readonly GalaxyDbContext _db;
    private readonly GalaxyConfig _config;
    private readonly EmailService _emailService;

    public AuthService(GalaxyDbContext db, GalaxyConfig config, EmailService emailService)
    {
        _db = db;
        _config = config;
        _emailService = emailService;
    }

    public async Task<GalaxyResult<string>> RegisterAsync(string username, string email, string password, string? verifyCode = null)
    {
        // 检查注册是否开放
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "registration_open");
        if (setting is not null && setting.Value != "true")
            return GalaxyResult<string>.Error(403, "注册已关闭");

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            return GalaxyResult<string>.Error(400, "用户名至少3个字符");
        if (string.IsNullOrWhiteSpace(email))
            return GalaxyResult<string>.Error(400, "邮箱不能为空");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return GalaxyResult<string>.Error(400, "密码至少6个字符");

        // 检查SMTP是否启用，启用时需验证邮箱
        var smtpEnabled = await _emailService.IsSmtpEnabledAsync();
        if (smtpEnabled)
        {
            if (string.IsNullOrWhiteSpace(verifyCode))
                return GalaxyResult<string>.Error(400, "请先发送验证码");
            var codeResult = await _emailService.ValidateCodeAsync(email, verifyCode);
            if (codeResult.Status != 200)
                return GalaxyResult<string>.Error(codeResult.Status, codeResult.Message ?? "验证码无效或已过期");
        }

        if (await _db.Users.AnyAsync(u => u.Username == username))
            return GalaxyResult<string>.Error(409, "用户名已存在");
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return GalaxyResult<string>.Error(409, "邮箱已注册");

        // 读取默认权限配置
        var defaultPermsSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "default_permissions");
        var defaultPerms = GalaxyPermissions.Default;
        if (defaultPermsSetting is not null)
        {
            try { defaultPerms = JsonSerializer.Deserialize<string[]>(defaultPermsSetting.Value) ?? GalaxyPermissions.Default; }
            catch { defaultPerms = GalaxyPermissions.Default; }
        }

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = GalaxyDbInitializer.HashPassword(password),
            Permissions = JsonSerializer.Serialize(defaultPerms),
            IsDisabled = false
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // 自动创建系统 API KEY
        var rawKey = $"gxy_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_")[..43]}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        var prefix = rawKey[..8];
        _db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            KeyHash = Convert.ToBase64String(hash),
            KeyPrefix = prefix,
            Name = "系统",
            Permissions = user.Permissions,
            IsSystem = true
        });
        await _db.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        return GalaxyResult<string>.Ok(token);
    }

    public async Task<GalaxyResult<string>> LoginAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || user.IsDisabled)
            return GalaxyResult<string>.Error(401, "用户名或密码错误");
        if (!GalaxyDbInitializer.VerifyPassword(password, user.PasswordHash))
            return GalaxyResult<string>.Error(401, "用户名或密码错误");

        var token = GenerateJwtToken(user);
        return GalaxyResult<string>.Ok(token);
    }

    public async Task<GalaxyResult<ApiKeyInfo>> CreateApiKeyAsync(int userId, string name, List<string>? permissions = null)
    {
        // 获取用户权限
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return GalaxyResult<ApiKeyInfo>.Error(404, "用户不存在");

        var userPerms = JsonSerializer.Deserialize<List<string>>(user.Permissions) ?? new List<string>();

        // 权限不能超出用户自身权限
        if (permissions is not null && permissions.Count > 0)
        {
            foreach (var perm in permissions)
            {
                if (!userPerms.Contains(perm))
                    return GalaxyResult<ApiKeyInfo>.Error(400, "权限超出您的权限范围");
            }
        }

        var rawKey = $"gxy_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_")[..43]}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        var prefix = rawKey[..8];

        var apiKey = new ApiKey
        {
            UserId = userId,
            KeyHash = Convert.ToBase64String(hash),
            KeyPrefix = prefix,
            Name = name,
            Permissions = JsonSerializer.Serialize(permissions ?? []),
            IsSystem = false
        };
        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        return GalaxyResult<ApiKeyInfo>.Ok(new ApiKeyInfo
        {
            Id = apiKey.Id,
            Key = rawKey,
            Prefix = prefix,
            Name = name,
            Permissions = permissions ?? new List<string>(),
            IsSystem = false,
            CreatedAt = apiKey.CreatedAt
        });
    }

    public async Task<GalaxyResult<List<ApiKeyInfo>>> ListApiKeysAsync(int userId)
    {
        var keys = await _db.ApiKeys
            .Where(a => a.UserId == userId)
            .Select(a => new ApiKeyInfo
            {
                Id = a.Id,
                Key = "", // 不返回完整key
                Prefix = a.KeyPrefix,
                Name = a.Name,
                Permissions = JsonSerializer.Deserialize<List<string>>(a.Permissions) ?? new List<string>(),
                IsSystem = a.IsSystem,
                LastUsed = a.LastUsed,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
        return GalaxyResult<List<ApiKeyInfo>>.Ok(keys);
    }

    public async Task<GalaxyResult> UpdateApiKeyPermissionsAsync(int userId, int keyId, List<string> permissions)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(a => a.Id == keyId && a.UserId == userId);
        if (key is null) return GalaxyResult.Error(404, "API Key 不存在");
        if (key.IsSystem) return GalaxyResult.Error(403, "系统 KEY 不可修改");

        // 权限不能超出用户自身权限
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return GalaxyResult.Error(404, "用户不存在");
        var userPerms2 = JsonSerializer.Deserialize<List<string>>(user.Permissions) ?? new List<string>();
        foreach (var perm in permissions)
        {
            if (!userPerms2.Contains(perm))
                return GalaxyResult.Error(400, "权限超出您的权限范围");
        }

        key.Permissions = JsonSerializer.Serialize(permissions);
        await _db.SaveChangesAsync();
        return GalaxyResult.Ok("权限已更新");
    }

    public async Task<GalaxyResult> RevokeApiKeyAsync(int userId, int keyId)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(a => a.Id == keyId && a.UserId == userId);
        if (key is null) return GalaxyResult.Error(404, "API Key 不存在");
        if (key.IsSystem) return GalaxyResult.Error(403, "系统 KEY 不可删除");
        _db.ApiKeys.Remove(key);
        await _db.SaveChangesAsync();
        return GalaxyResult.Ok("API Key 已撤销");
    }

    public async Task<User?> ValidateApiKeyAsync(string rawKey)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        var apiKey = await _db.ApiKeys.Include(a => a.User).FirstOrDefaultAsync(a => a.KeyHash == hash);
        if (apiKey is null || apiKey.User.IsDisabled) return null;
        apiKey.LastUsed = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return apiKey.User;
    }

    public async Task<ApiKey?> ValidateApiKeyWithPermissionsAsync(string rawKey)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        var apiKey = await _db.ApiKeys.Include(a => a.User).FirstOrDefaultAsync(a => a.KeyHash == hash);
        if (apiKey is null || apiKey.User.IsDisabled) return null;
        apiKey.LastUsed = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return apiKey;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        return await _db.Users.FindAsync(id);
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("permissions", user.Permissions),
            new("isDeveloper", user.IsDeveloper.ToString().ToLower())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_config.JwtExpireHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class ApiKeyInfo
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public bool IsSystem { get; set; }
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; }
}
