using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace LegacyECommerceApi.Tests.Infrastructure
{
    /// <summary>
    /// Owns the throwaway test database.
    ///
    /// Isolation from production data is structural, not conventional: the database name is
    /// always suffixed "_Tests" and the fixture refuses to run against the production name.
    ///
    /// Schema comes from the real DatabaseSetup.sql so that schema drift breaks tests
    /// instead of hiding. The script's sample INSERTs are wiped by ResetAsync, so every
    /// test controls its own data.
    /// </summary>
    public static class TestDatabase
    {
        public const string ProductionDatabaseName = "LegacyECommerceDb";
        public const string TestDatabaseName = "LegacyECommerceDb_Tests";

        /// <summary>Override with a real server in CI, e.g. a Testcontainers or hosted instance.</summary>
        public const string ConnectionStringEnvironmentVariable = "LEGACY_ECOMMERCE_TEST_SQL";

        private static readonly Lazy<string> LazyConnectionString = new(BuildConnectionString);
        private static readonly Lazy<(bool Available, string Reason)> LazyProbe = new(Probe);

        public static string ConnectionString => LazyConnectionString.Value;

        public static bool IsAvailable => LazyProbe.Value.Available;

        public static string SkipReason => LazyProbe.Value.Reason;

        private static string BuildConnectionString()
        {
            var configured = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

            var builder = string.IsNullOrWhiteSpace(configured)
                ? new SqlConnectionStringBuilder
                {
                    DataSource = @"(localdb)\mssqllocaldb",
                    IntegratedSecurity = true,
                    Encrypt = false
                }
                : new SqlConnectionStringBuilder(configured);

            GuardAgainstProductionDatabase(builder);

            builder.InitialCatalog = TestDatabaseName;
            builder.ConnectTimeout = 5;
            return builder.ConnectionString;
        }

        private static void GuardAgainstProductionDatabase(SqlConnectionStringBuilder builder)
        {
            if (string.Equals(builder.InitialCatalog, ProductionDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to run tests against the production database '{ProductionDatabaseName}'. " +
                    $"Tests always use '{TestDatabaseName}'.");
            }
        }

        private static string MasterConnectionString()
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            return builder.ConnectionString;
        }

        private static (bool, string) Probe()
        {
            try
            {
                using var connection = new SqlConnection(MasterConnectionString());
                connection.Open();
                EnsureDatabase();
                EnsureSchema();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                var source = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is null
                    ? @"the default (localdb)\mssqllocaldb"
                    : $"${ConnectionStringEnvironmentVariable}";

                return (false,
                    $"No SQL Server reachable via {source}. " +
                    $"Set {ConnectionStringEnvironmentVariable} to a server the suite may create " +
                    $"'{TestDatabaseName}' on, then re-run. ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static void EnsureDatabase()
        {
            using var connection = new SqlConnection(MasterConnectionString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE [{TestDatabaseName}]');";
            command.Parameters.AddWithValue("@name", TestDatabaseName);
            command.ExecuteNonQuery();
        }

        private static void EnsureSchema()
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT OBJECT_ID('dbo.Customers', 'U');";
                if (check.ExecuteScalar() is not (null or DBNull))
                {
                    return;
                }
            }

            var scriptPath = Path.Combine(AppContext.BaseDirectory, "DatabaseSetup.sql");
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException(
                    "DatabaseSetup.sql was not copied to the test output directory.", scriptPath);
            }

            // The script contains no GO batch separators, so it runs as a single command.
            using var create = connection.CreateCommand();
            create.CommandText = File.ReadAllText(scriptPath);
            create.CommandTimeout = 120;
            create.ExecuteNonQuery();
        }

        /// <summary>
        /// Delete-and-reseed between tests rather than a wrapping transaction: OrderRepository.Add
        /// and Delete open their own connection and call BeginTransaction themselves, so an ambient
        /// outer transaction would require TransactionScope and distributed escalation.
        /// </summary>
        public static async Task ResetAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM OrderItems;
                DELETE FROM Orders;
                DELETE FROM Products;
                DELETE FROM Customers;
                DBCC CHECKIDENT ('OrderItems', RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT ('Orders',     RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT ('Products',   RESEED, 0) WITH NO_INFOMSGS;
                DBCC CHECKIDENT ('Customers',  RESEED, 0) WITH NO_INFOMSGS;
                """;
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Configuration shaped exactly like the application's, pointed at the test database.</summary>
        public static IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString
                })
                .Build();

        public static async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var result = await command.ExecuteScalarAsync();
            return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
        }
    }
}
