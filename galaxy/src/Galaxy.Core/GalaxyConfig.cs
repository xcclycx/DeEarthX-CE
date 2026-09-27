namespace Galaxy.Core;

public class GalaxyConfig
{
    public string JwtSecret { get; set; } = "galaxy-default-secret-change-me";
    public int JwtExpireHours { get; set; } = 72;
    public string DatabasePath { get; set; } = "galaxy.db";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "admin123";
}
