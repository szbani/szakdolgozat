using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using szakdolgozat.Models;

namespace szakdolgozat;

public class AppDbContext :IdentityDbContext<IdentityUser>
{

    public DbSet<DisplayModel> displays { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<DisplayModel>(b =>
        {
            b.HasKey(e => e.Id);
            b.ToTable("Displays");
            b.Property(e => e.DisplayName).HasMaxLength(256);
            b.Property(e => e.DisplayDescription).HasMaxLength(256);
            b.Property(e => e.macAddress).IsRequired().HasMaxLength(17);
            b.Property(e => e.KioskName).IsRequired().HasMaxLength(256);
        });
    }
}