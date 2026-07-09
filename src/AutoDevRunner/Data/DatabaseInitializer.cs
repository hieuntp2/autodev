using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoDevRunner.Data;

public enum DatabaseInitializationAction
{
    MigrateFresh,
    BaselineLegacy,
    MigrateExisting
}

public sealed record DatabaseSchemaState(
    bool DatabaseExists,
    bool ProjectsTableExists,
    bool HistoryTableExists);

/// <summary>
/// Initializes the PostgreSQL schema through EF migrations and safely adopts
/// legacy databases that were created before migrations existed.
/// </summary>
public class DatabaseInitializer
{
    private const string ProjectsTable = "Projects";
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string InitialMigrationName = "InitialCreate";

    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseInitializer> _log;

    public DatabaseInitializer(AppDbContext db, ILogger<DatabaseInitializer> log)
    {
        _db = db;
        _log = log;
    }

    public static DatabaseInitializationAction SelectAction(DatabaseSchemaState state)
    {
        if (!state.DatabaseExists || !state.ProjectsTableExists)
            return DatabaseInitializationAction.MigrateFresh;

        return state.HistoryTableExists
            ? DatabaseInitializationAction.MigrateExisting
            : DatabaseInitializationAction.BaselineLegacy;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var state = await InspectSchemaAsync(ct);
            var action = SelectAction(state);
            switch (action)
            {
                case DatabaseInitializationAction.MigrateFresh:
                    await MigrateFreshAsync(ct);
                    break;
                case DatabaseInitializationAction.BaselineLegacy:
                    await BaselineLegacyAsync(ct);
                    break;
                case DatabaseInitializationAction.MigrateExisting:
                    await MigrateExistingAsync(ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown database initialization action: {action}");
            }
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Database initialization failed; startup will abort.");
            throw;
        }
    }

    private async Task MigrateFreshAsync(CancellationToken ct)
    {
        var migrations = _db.Database.GetMigrations().ToList();
        await _db.Database.MigrateAsync(ct);
        _log.LogInformation(
            "Database initialized by applying {Count} migration(s) to a fresh schema.",
            migrations.Count);
    }

    private async Task BaselineLegacyAsync(CancellationToken ct)
    {
        var initialMigrationId = ResolveInitialMigrationId();
        await MarkInitialMigrationAppliedAsync(initialMigrationId, ct);

        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
        await _db.Database.MigrateAsync(ct);
        _log.LogInformation(
            "Database initialized by adopting legacy schema at {InitialMigration}; applied {Count} newer migration(s).",
            initialMigrationId,
            pending.Count);
    }

    private async Task MigrateExistingAsync(CancellationToken ct)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
        await _db.Database.MigrateAsync(ct);
        _log.LogInformation(
            "Database initialized by applying {Count} pending migration(s) to an existing migrated schema.",
            pending.Count);
    }

    private async Task<DatabaseSchemaState> InspectSchemaAsync(CancellationToken ct)
    {
        if (!await _db.Database.CanConnectAsync(ct))
            return new DatabaseSchemaState(false, false, false);

        var projectsExists = await TableExistsAsync(ProjectsTable, ct);
        var historyExists = await TableExistsAsync(HistoryTable, ct);
        return new DatabaseSchemaState(true, projectsExists, historyExists);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var closeWhenDone = connection.State == ConnectionState.Closed;
        if (closeWhenDone)
            await connection.OpenAsync(ct);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = @table_name);
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "table_name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(ct);
            return result is true;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private string ResolveInitialMigrationId()
    {
        var matches = _db.Database.GetMigrations()
            .Where(m => m.EndsWith("_" + InitialMigrationName, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Cannot baseline legacy database because migration '{InitialMigrationName}' was not found."),
            _ => throw new InvalidOperationException(
                $"Cannot baseline legacy database because multiple '{InitialMigrationName}' migrations were found.")
        };
    }

    private async Task MarkInitialMigrationAppliedAsync(string initialMigrationId, CancellationToken ct)
    {
        var history = _db.GetService<IHistoryRepository>();
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
        await _db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigrationId, GetEfProductVersion())),
            ct);
        await transaction.CommitAsync(ct);
    }

    private static string GetEfProductVersion()
    {
        var info = typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var buildMetadata = info.IndexOf('+', StringComparison.Ordinal);
            return buildMetadata >= 0 ? info[..buildMetadata] : info;
        }

        var version = typeof(DbContext).Assembly.GetName().Version;
        return version is null
            ? "8.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
