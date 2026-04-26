using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WiSave.Portal.Auth.Models;

namespace WiSave.Portal.Infrastructure.Database;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");
    }
}
