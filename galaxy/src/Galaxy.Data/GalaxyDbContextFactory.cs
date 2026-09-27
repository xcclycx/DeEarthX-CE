using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Galaxy.Data;

public class GalaxyDbContextFactory : IDesignTimeDbContextFactory<GalaxyDbContext>
{
    public GalaxyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GalaxyDbContext>()
            .UseSqlite("Data Source=galaxy.db")
            .Options;
        return new GalaxyDbContext(options);
    }
}
