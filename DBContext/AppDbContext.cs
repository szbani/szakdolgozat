using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using szakdolgozat.DBContext.Models;

namespace szakdolgozat.DBContext;

public class AppDbContext :IdentityDbContext<IdentityUser>
{

    public DbSet<DisplayModel> displays { get; set; }
    public DbSet<PlaylistsModel> playlists { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // builder.Entity<IdentityUser>(b =>
        // {az
        //     b.HasIndex( e => e.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();
        // });
        builder.Entity<PlaylistsModel>(e =>
        {
            e.HasKey(e => e.Id);
            e.ToTable("Playlists");
            e.HasIndex(k => k.PlaylistName).IsUnique();
            e.Property(e => e.PlaylistName).HasMaxLength(256).IsRequired();
            e.Property(e => e.PlaylistDescription).HasMaxLength(256);
        });
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