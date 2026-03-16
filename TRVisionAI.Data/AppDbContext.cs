using TRVisionAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TRVisionAI.Data;

public sealed class AppDbContext : DbContext
{
    public DbSet<SessionEntity> Sessions { get; set; } = null!;
    public DbSet<FrameEntity>   Frames   { get; set; } = null!;
    public DbSet<ModuleEntity>  Modules  { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SessionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.CameraIp).HasMaxLength(45);
            e.Property(s => s.CameraModel).HasMaxLength(128);
            e.Property(s => s.Operator).HasMaxLength(128);
            e.HasMany(s => s.Frames)
             .WithOne(f => f.Session)
             .HasForeignKey(f => f.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<FrameEntity>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.SessionId);
            e.HasIndex(f => f.ReceivedAt);
            e.HasIndex(f => f.ApiSentAt);
            e.Property(f => f.SolutionName).HasMaxLength(256);
            e.Property(f => f.ImagePath).HasMaxLength(512);
            e.Property(f => f.MaskImagePath).HasMaxLength(512);
            e.HasMany(f => f.Modules)
             .WithOne(m => m.Frame)
             .HasForeignKey(m => m.FrameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ModuleEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.FrameId);
            e.Property(m => m.ModuleName).HasMaxLength(256);
        });
    }
}
