using Galaxy.Core;
using Galaxy.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Galaxy.OAuth;

public class DeveloperService
{
    private readonly IDbContextFactory<GalaxyDbContext> _dbFactory;

    public DeveloperService(IDbContextFactory<GalaxyDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // 提交开发者申请
    public async Task<GalaxyResult> ApplyAsync(int userId, string developerName, string purpose, string? websiteUrl, string? contactInfo)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return GalaxyResult.Error(404, "用户不存在");
        if (user.IsDeveloper) return GalaxyResult.Error(400, "您已是开发者");

        // 检查是否有待审核的申请
        var existingPending = await db.DeveloperApplications
            .AnyAsync(d => d.UserId == userId && d.Status == ApplicationStatus.Pending);
        if (existingPending) return GalaxyResult.Error(409, "您已提交申请，请等待审核");

        if (string.IsNullOrWhiteSpace(developerName))
            return GalaxyResult.Error(400, "开发者名称不能为空");
        if (string.IsNullOrWhiteSpace(purpose))
            return GalaxyResult.Error(400, "申请用途说明不能为空");

        var application = new DeveloperApplication
        {
            UserId = userId,
            DeveloperName = developerName,
            Purpose = purpose,
            WebsiteUrl = websiteUrl,
            ContactInfo = contactInfo,
            Status = ApplicationStatus.Pending
        };
        db.DeveloperApplications.Add(application);

        // 检查是否需要审核
        var requireApproval = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "developer_require_approval");
        if (requireApproval?.Value == "false")
        {
            // 无需审核，直接通过
            application.Status = ApplicationStatus.Approved;
            application.ReviewedAt = DateTime.UtcNow;
            user.IsDeveloper = true;
            user.DeveloperApplicationId = application.Id;

            // 添加开发者权限
            var perms = JsonSerializer.Deserialize<List<string>>(user.Permissions) ?? [];
            foreach (var p in GalaxyPermissions.Developer)
            {
                if (!perms.Contains(p)) perms.Add(p);
            }
            user.Permissions = JsonSerializer.Serialize(perms);
            user.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return GalaxyResult.Ok(application.Status == ApplicationStatus.Approved ? "已自动通过" : "申请已提交，请等待审核");
    }

    // 查看自己的申请状态
    public async Task<GalaxyResult<DeveloperApplicationInfo?>> GetMyStatusAsync(int userId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return GalaxyResult<DeveloperApplicationInfo?>.Error(404, "用户不存在");

        if (user.IsDeveloper)
        {
            return GalaxyResult<DeveloperApplicationInfo?>.Ok(new DeveloperApplicationInfo
            {
                IsDeveloper = true,
                Status = "approved"
            });
        }

        var latestApp = await db.DeveloperApplications
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestApp is null)
        {
            return GalaxyResult<DeveloperApplicationInfo?>.Ok(new DeveloperApplicationInfo
            {
                IsDeveloper = false,
                Status = "none"
            });
        }

        return GalaxyResult<DeveloperApplicationInfo?>.Ok(new DeveloperApplicationInfo
        {
            IsDeveloper = false,
            Status = latestApp.Status.ToString().ToLower(),
            DeveloperName = latestApp.DeveloperName,
            Purpose = latestApp.Purpose,
            WebsiteUrl = latestApp.WebsiteUrl,
            ContactInfo = latestApp.ContactInfo,
            ReviewNote = latestApp.ReviewNote,
            CreatedAt = latestApp.CreatedAt
        });
    }

    // 管理员：获取所有开发者申请
    public async Task<GalaxyResult<List<DeveloperApplicationDetail>>> ListApplicationsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var apps = await db.DeveloperApplications
            .Include(d => d.User)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DeveloperApplicationDetail
            {
                Id = d.Id,
                UserId = d.UserId,
                Username = d.User.Username,
                DeveloperName = d.DeveloperName,
                Purpose = d.Purpose,
                WebsiteUrl = d.WebsiteUrl,
                ContactInfo = d.ContactInfo,
                Status = d.Status.ToString().ToLower(),
                ReviewNote = d.ReviewNote,
                ReviewedAt = d.ReviewedAt,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return GalaxyResult<List<DeveloperApplicationDetail>>.Ok(apps);
    }

    // 管理员：审核开发者申请
    public async Task<GalaxyResult> ReviewAsync(int applicationId, bool approved, string? reviewNote, int reviewerId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.DeveloperApplications.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == applicationId);
        if (app is null) return GalaxyResult.Error(404, "申请不存在");
        if (app.Status != ApplicationStatus.Pending) return GalaxyResult.Error(400, "该申请已处理");

        app.Status = approved ? ApplicationStatus.Approved : ApplicationStatus.Rejected;
        app.ReviewNote = reviewNote;
        app.ReviewedAt = DateTime.UtcNow;
        app.ReviewedBy = reviewerId;

        if (approved)
        {
            app.User.IsDeveloper = true;
            app.User.DeveloperApplicationId = app.Id;

            // 添加开发者权限
            var perms = JsonSerializer.Deserialize<List<string>>(app.User.Permissions) ?? [];
            foreach (var p in GalaxyPermissions.Developer)
            {
                if (!perms.Contains(p)) perms.Add(p);
            }
            app.User.Permissions = JsonSerializer.Serialize(perms);
            app.User.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return GalaxyResult.Ok(approved ? "已通过" : "已拒绝");
    }
}

public class DeveloperApplicationInfo
{
    public bool IsDeveloper { get; set; }
    public string Status { get; set; } = "none"; // none, pending, approved, rejected
    public string? DeveloperName { get; set; }
    public string? Purpose { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? ContactInfo { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class DeveloperApplicationDetail
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string DeveloperName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string? WebsiteUrl { get; set; }
    public string? ContactInfo { get; set; }
    public string Status { get; set; } = "pending";
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
