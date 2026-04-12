using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WiSave.Portal.Auth.Models;

namespace WiSave.Portal.Infrastructure.Database;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<PlanPermission> PlanPermissions => Set<PlanPermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("public");
        

        builder.Entity<Plan>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(50);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Permission>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<PlanPermission>(e =>
        {
            e.HasKey(pp => new { pp.PlanId, pp.PermissionId });
            e.HasOne(pp => pp.Plan).WithMany(p => p.PlanPermissions).HasForeignKey(pp => pp.PlanId);
            e.HasOne(pp => pp.Permission).WithMany(p => p.PlanPermissions).HasForeignKey(pp => pp.PermissionId);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.PlanId).HasMaxLength(50).HasDefaultValue("free");
        });
    }
}
