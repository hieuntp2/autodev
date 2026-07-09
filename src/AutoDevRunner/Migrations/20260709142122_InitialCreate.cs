using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AutoDevRunner.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RepoPath = table.Column<string>(type: "text", nullable: false),
                    BriefPath = table.Column<string>(type: "text", nullable: false),
                    AllowAiEditBrief = table.Column<bool>(type: "boolean", nullable: false),
                    ProjectType = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ProviderPriority = table.Column<string>(type: "text", nullable: false),
                    AiBranchPrefix = table.Column<string>(type: "text", nullable: false),
                    AllowRunOnMainBranch = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCommit = table.Column<bool>(type: "boolean", nullable: false),
                    AutoPush = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationCommand = table.Column<string>(type: "text", nullable: true),
                    MaxRunMinutes = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CurrentTask = table.Column<string>(type: "text", nullable: true),
                    LastSummary = table.Column<string>(type: "text", nullable: true),
                    CurrentBranch = table.Column<string>(type: "text", nullable: true),
                    ProviderSessionId = table.Column<string>(type: "text", nullable: true),
                    LastRunStatus = table.Column<string>(type: "text", nullable: true),
                    LastProvider = table.Column<string>(type: "text", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAuthErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastQuotaLimitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastQuotaResetHint = table.Column<string>(type: "text", nullable: true),
                    LastKnownUsage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBriefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBriefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBriefs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Branch = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    ChangedFiles = table.Column<string>(type: "text", nullable: true),
                    ValidationRun = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationOutput = table.Column<string>(type: "text", nullable: true),
                    Usage = table.Column<string>(type: "text", nullable: true),
                    LogPath = table.Column<string>(type: "text", nullable: true),
                    EmailSent = table.Column<bool>(type: "boolean", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: true),
                    TaskTitle = table.Column<string>(type: "text", nullable: true),
                    TaskSource = table.Column<string>(type: "text", nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    CreativePlan = table.Column<string>(type: "text", nullable: true),
                    Stage = table.Column<string>(type: "text", nullable: true),
                    Risk = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBriefs_ProjectId_Version",
                table: "ProjectBriefs",
                columns: new[] { "ProjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderStates_Provider",
                table: "ProviderStates",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProjectId",
                table: "Runs",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectBriefs");

            migrationBuilder.DropTable(
                name: "ProviderStates");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
