using Galaxy.Core;
using Galaxy.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Galaxy.Auth;

public class EmailService
{
    private readonly IDbContextFactory<GalaxyDbContext> _dbFactory;
    private readonly GalaxyConfig _config;

    public EmailService(IDbContextFactory<GalaxyDbContext> dbFactory, GalaxyConfig config)
    {
        _dbFactory = dbFactory;
        _config = config;
    }

    public async Task<GalaxyResult> SendVerifyCodeAsync(string email)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        // 检查SMTP是否启用
        var smtpEnabled = await GetSettingAsync(db, "smtp_enabled");
        if (smtpEnabled != "true")
            return GalaxyResult.Error(400, "邮箱验证功能未开启");

        // 60秒内不能重复发送
        var recentCode = await db.EmailVerifications
            .Where(v => v.Email == email && v.CreatedAt > DateTime.UtcNow.AddSeconds(-60))
            .FirstOrDefaultAsync();
        if (recentCode is not null)
            return GalaxyResult.Error(429, "发送过于频繁，请稍后重试");

        // 每天最多10次
        var todayCount = await db.EmailVerifications
            .CountAsync(v => v.Email == email && v.CreatedAt > DateTime.UtcNow.Date);
        if (todayCount >= 10)
            return GalaxyResult.Error(429, "发送过于频繁，请稍后重试");

        // 生成6位验证码
        var code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");

        var verification = new EmailVerification
        {
            Email = email,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false
        };
        db.EmailVerifications.Add(verification);
        await db.SaveChangesAsync();

        // 发送邮件
        try
        {
            await SendEmailAsync(db, email, "Galaxy 邮箱验证码", $"您的验证码是：{code}，有效期10分钟。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Galaxy.Auth] 邮件发送失败: {ex.Message}\n{ex.InnerException?.Message}");
            // 删除已创建的验证码记录，避免脏数据
            db.EmailVerifications.Remove(verification);
            await db.SaveChangesAsync();
            return GalaxyResult.Error(500, "邮件发送失败，请稍后重试");
        }

        return GalaxyResult.Ok("验证码已发送");
    }

    public async Task<GalaxyResult<EmailVerification?>> ValidateCodeAsync(string email, string code)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var verification = await db.EmailVerifications
            .Where(v => v.Email == email && v.Code == code && !v.IsUsed && v.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (verification is null)
            return GalaxyResult<EmailVerification?>.Error(400, "验证码无效或已过期");

        verification.IsUsed = true;
        await db.SaveChangesAsync();

        return GalaxyResult<EmailVerification?>.Ok(verification);
    }

    public async Task<bool> IsSmtpEnabledAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await GetSettingAsync(db, "smtp_enabled") == "true";
    }

    private async Task SendEmailAsync(GalaxyDbContext db, string to, string subject, string body)
    {
        var host = await GetSettingAsync(db, "smtp_host");
        var portStr = await GetSettingAsync(db, "smtp_port");
        var username = await GetSettingAsync(db, "smtp_username");
        var passwordEncrypted = await GetSettingAsync(db, "smtp_password");
        var from = await GetSettingAsync(db, "smtp_from");

        var port = int.TryParse(portStr, out var p) ? p : 587;
        var password = string.IsNullOrEmpty(passwordEncrypted)
            ? ""
            : GalaxyDbInitializer.DecryptSmtpPassword(passwordEncrypted, _config.JwtSecret);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Galaxy", from));
        message.To.Add(new MailboxAddress("", to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        // 465 端口用隐式 SSL，587 用 STARTTLS，25 不加密
        var secureOption = port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.Auto
        };

        await client.ConnectAsync(host, port, secureOption);
        await client.AuthenticateAsync(username, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static async Task<string> GetSettingAsync(GalaxyDbContext db, string key)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value ?? "";
    }
}
