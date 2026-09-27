using Galaxy.Core;
using Microsoft.EntityFrameworkCore;

namespace Galaxy.Data;

public class GalaxyDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Mod> Mods => Set<Mod>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();
    public DbSet<DeveloperApplication> DeveloperApplications => Set<DeveloperApplication>();
    public DbSet<OAuthApp> OAuthApps => Set<OAuthApp>();
    public DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();
    public DbSet<OAuthAccessToken> OAuthAccessTokens => Set<OAuthAccessToken>();

    public GalaxyDbContext(DbContextOptions<GalaxyDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Permissions).HasDefaultValue("[]");
            e.Property(u => u.IsDeveloper).HasDefaultValue(false);
            e.HasOne(u => u.DeveloperApplication).WithMany().HasForeignKey(u => u.DeveloperApplicationId);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(a => a.KeyHash).IsUnique();
            e.HasIndex(a => a.KeyPrefix);
            e.HasOne(a => a.User).WithMany(u => u.ApiKeys).HasForeignKey(a => a.UserId);
            e.Property(a => a.Permissions).HasDefaultValue("[]");
            e.Property(a => a.IsSystem).HasDefaultValue(false);
        });

        modelBuilder.Entity<Mod>(e =>
        {
            e.HasIndex(m => m.ModId).IsUnique();
        });

        modelBuilder.Entity<SystemSetting>(e =>
        {
            e.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<EmailVerification>(e =>
        {
            e.HasIndex(v => v.Email);
        });

        modelBuilder.Entity<DeveloperApplication>(e =>
        {
            e.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<OAuthApp>(e =>
        {
            e.HasIndex(o => o.ClientId).IsUnique();
            e.HasOne(o => o.DeveloperUser).WithMany().HasForeignKey(o => o.DeveloperUserId);
        });

        modelBuilder.Entity<OAuthAuthorizationCode>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
            e.HasOne(c => c.OAuthApp).WithMany(o => o.AuthorizationCodes).HasForeignKey(c => c.OAuthAppId);
            e.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
        });

        modelBuilder.Entity<OAuthAccessToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.OAuthApp).WithMany(o => o.AccessTokens).HasForeignKey(t => t.OAuthAppId);
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId);
        });
    }
}
