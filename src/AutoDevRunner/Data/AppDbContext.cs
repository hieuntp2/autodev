using AutoDevRunner.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoDevRunner.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<RunRecord> Runs => Set<RunRecord>();
    public DbSet<ProviderState> ProviderStates => Set<ProviderState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasIndex(p => p.Name).IsUnique();
        b.Entity<RunRecord>()
            .HasOne(r => r.Project)
            .WithMany(p => p.Runs)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProviderState>().HasIndex(p => p.Provider).IsUnique();

        // Store enums as strings for readable DB rows.
        b.Entity<Project>().Property(p => p.LastRunStatus).HasConversion<string>();
        b.Entity<Project>().Property(p => p.LastProvider).HasConversion<string>();
        b.Entity<RunRecord>().Property(r => r.Status).HasConversion<string>();
        b.Entity<RunRecord>().Property(r => r.Provider).HasConversion<string>();
        b.Entity<ProviderState>().Property(p => p.Provider).HasConversion<string>();
    }
}
