using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProjectPromptServiceTests
{
    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("prompt-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<int> AddProject(AppDbContext db)
    {
        var project = new Project { Name = "P", RepoPath = "C:/repo" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    [Fact]
    public async Task Adds_versions_and_skips_unchanged_content()
    {
        await using var db = Db();
        var projectId = await AddProject(db);

        var v1 = await ProjectPromptService.AddVersionIfChangedAsync(
            db, projectId, "Use concise diffs.", PromptDirectiveAuthor.User, "initial");
        await db.SaveChangesAsync();
        var skipped = await ProjectPromptService.AddVersionIfChangedAsync(
            db, projectId, "  Use concise diffs.  ", PromptDirectiveAuthor.Ai, "same");
        var v2 = await ProjectPromptService.AddVersionIfChangedAsync(
            db, projectId, "Prefer focused tests.", PromptDirectiveAuthor.Ai, "proposal");
        await db.SaveChangesAsync();

        Assert.Equal(1, v1?.Version);
        Assert.Null(skipped);
        Assert.Equal(2, v2?.Version);
        Assert.Equal("Prefer focused tests.", await ProjectPromptService.GetLatestContentAsync(db, projectId));
    }

    [Fact]
    public async Task History_is_newest_first()
    {
        await using var db = Db();
        var projectId = await AddProject(db);
        await ProjectPromptService.AddVersionIfChangedAsync(db, projectId, "one", PromptDirectiveAuthor.User, null);
        await db.SaveChangesAsync();
        await ProjectPromptService.AddVersionIfChangedAsync(db, projectId, "two", PromptDirectiveAuthor.Ai, null);
        await db.SaveChangesAsync();

        var history = await ProjectPromptService.GetHistoryAsync(db, projectId);

        Assert.Equal(new[] { 2, 1 }, history.Select(h => h.Version).ToArray());
    }

    [Fact]
    public async Task Revert_clones_old_content_as_new_user_version()
    {
        await using var db = Db();
        var projectId = await AddProject(db);
        await ProjectPromptService.AddVersionIfChangedAsync(db, projectId, "first", PromptDirectiveAuthor.User, null);
        await db.SaveChangesAsync();
        await ProjectPromptService.AddVersionIfChangedAsync(db, projectId, "second", PromptDirectiveAuthor.Ai, null);
        await db.SaveChangesAsync();

        var reverted = await ProjectPromptService.RevertAsNewVersionAsync(db, projectId, version: 1);
        await db.SaveChangesAsync();

        Assert.NotNull(reverted);
        Assert.Equal(3, reverted.Version);
        Assert.Equal("first", reverted.Content);
        Assert.Equal(PromptDirectiveAuthor.User, reverted.Author);
        Assert.Equal("reverted to version 1", reverted.Note);
    }
}
