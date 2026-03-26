using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WiSave.Portal.Infrastructure.Database;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=localhost;Database=wisave_portal;Username=wisave;Password=wisave_dev")
            .Options;

        return new PortalDbContext(options);
    }
}
