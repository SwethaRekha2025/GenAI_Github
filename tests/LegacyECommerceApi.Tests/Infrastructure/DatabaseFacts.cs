using Xunit;

namespace LegacyECommerceApi.Tests.Infrastructure
{
    /// <summary>
    /// A Fact that skips, rather than fails, when no SQL Server is reachable.
    ///
    /// The repositories construct their own SqlConnection, so they cannot be exercised without a
    /// real database. Failing the suite on a machine that simply has no server would make the
    /// build meaningless; skipping with a reason keeps the signal honest either way.
    /// </summary>
    public sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (!TestDatabase.IsAvailable)
            {
                Skip = TestDatabase.SkipReason;
            }
        }
    }

    /// <summary>Theory counterpart of <see cref="SqlServerFactAttribute"/>.</summary>
    public sealed class SqlServerTheoryAttribute : TheoryAttribute
    {
        public SqlServerTheoryAttribute()
        {
            if (!TestDatabase.IsAvailable)
            {
                Skip = TestDatabase.SkipReason;
            }
        }
    }

    /// <summary>
    /// Resets the database before each test in the collection. Database tests share one database,
    /// so they must not run in parallel with one another.
    /// </summary>
    public sealed class DatabaseFixture : IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            if (TestDatabase.IsAvailable)
            {
                await TestDatabase.ResetAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    {
        public const string Name = "sql-server";
    }

    /// <summary>Base class that gives every integration test a clean database.</summary>
    [Collection(DatabaseCollection.Name)]
    public abstract class DatabaseTestBase : IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            if (TestDatabase.IsAvailable)
            {
                await TestDatabase.ResetAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
