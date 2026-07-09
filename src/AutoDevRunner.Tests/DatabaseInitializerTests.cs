using AutoDevRunner.Data;
using Xunit;

namespace AutoDevRunner.Tests;

public class DatabaseInitializerTests
{
    [Theory]
    [InlineData(false, false, false, DatabaseInitializationAction.MigrateFresh)]
    [InlineData(true, false, false, DatabaseInitializationAction.MigrateFresh)]
    [InlineData(true, true, false, DatabaseInitializationAction.BaselineLegacy)]
    [InlineData(true, true, true, DatabaseInitializationAction.MigrateExisting)]
    public void Selects_safe_initialization_action(
        bool databaseExists,
        bool projectsTableExists,
        bool historyTableExists,
        DatabaseInitializationAction expected)
    {
        var state = new DatabaseSchemaState(databaseExists, projectsTableExists, historyTableExists);

        var action = DatabaseInitializer.SelectAction(state);

        Assert.Equal(expected, action);
    }
}
