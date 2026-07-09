using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoDevRunner.Migrations
{
    /// <inheritdoc />
    public partial class AddRunMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostUsd",
                table: "Runs",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "Runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotVerified",
                table: "Runs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "Runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptChars",
                table: "Runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptEstTokens",
                table: "Runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepairAttempts",
                table: "Runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Resumed",
                table: "Runs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionResult",
                table: "Runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValidationInferred",
                table: "Runs",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostUsd",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "NotVerified",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "PromptChars",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "PromptEstTokens",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "RepairAttempts",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Resumed",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "SessionResult",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ValidationInferred",
                table: "Runs");
        }
    }
}
