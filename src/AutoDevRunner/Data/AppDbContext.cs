using AutoDevRunner.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoDevRunner.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<RunRecord> Runs => Set<RunRecord>();
    public DbSet<ProviderState> ProviderStates => Set<ProviderState>();
    public DbSet<ProjectBrief> ProjectBriefs => Set<ProjectBrief>();
    public DbSet<PromptDirective> PromptDirectives => Set<PromptDirective>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>().HasIndex(p => p.Name).IsUnique();
        b.Entity<RunRecord>()
            .HasOne(r => r.Project)
            .WithMany(p => p.Runs)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProviderState>().HasIndex(p => p.Provider).IsUnique();

        // Append-only brief versions; one row per (project, version).
        b.Entity<ProjectBrief>()
            .HasOne(br => br.Project)
            .WithMany(p => p.Briefs)
            .HasForeignKey(br => br.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProjectBrief>().HasIndex(br => new { br.ProjectId, br.Version }).IsUnique();
        b.Entity<ProjectBrief>().Property(br => br.Author).HasConversion<string>();

        b.Entity<PromptDirective>()
            .HasOne(pr => pr.Project)
            .WithMany(p => p.PromptDirectives)
            .HasForeignKey(pr => pr.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<PromptDirective>().HasIndex(pr => new { pr.ProjectId, pr.Version }).IsUnique();
        b.Entity<PromptDirective>().Property(pr => pr.Author).HasConversion<string>();

        // Store enums as strings for readable DB rows.
        b.Entity<Project>().Property(p => p.LastRunStatus).HasConversion<string>();
        b.Entity<Project>().Property(p => p.LastProvider).HasConversion<string>();
        b.Entity<RunRecord>().Property(r => r.Status).HasConversion<string>();
        b.Entity<RunRecord>().Property(r => r.Provider).HasConversion<string>();
        b.Entity<ProviderState>().Property(p => p.Provider).HasConversion<string>();
    }
}
