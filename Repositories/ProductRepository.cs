using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private const string ConnectionStringName = "DefaultConnection";

        // Column widths mirror DatabaseSetup.sql. Binding parameters at the column's declared size
        // (rather than letting AddWithValue size them to each value) keeps one cached plan per
        // statement instead of one per distinct string length.
        private const int NameSize = 200;
        private const int DescriptionSize = 1000;
        private const int CategorySize = 100;

        private const string SelectColumns =
            "ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive";

        private const string SelectByIdSql =
            $"SELECT {SelectColumns} FROM Products WHERE ProductId = @ProductId";

        private const string SelectAllSql =
            $"SELECT {SelectColumns} FROM Products ORDER BY Name";

        private const string SelectByCategorySql =
            $"SELECT {SelectColumns} FROM Products WHERE Category = @Category AND IsActive = 1 ORDER BY Name";

        private const string SelectActiveSql =
            $"SELECT {SelectColumns} FROM Products WHERE IsActive = 1 AND StockQuantity > 0 ORDER BY Name";

        private const string InsertSql = """
            INSERT INTO Products (Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive)
            VALUES (@Name, @Description, @Price, @StockQuantity, @Category, @CreatedDate, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() as int);
            """;

        private const string UpdateSql = """
            UPDATE Products
            SET Name = @Name, Description = @Description, Price = @Price,
                StockQuantity = @StockQuantity, Category = @Category, IsActive = @IsActive
            WHERE ProductId = @ProductId
            """;

        private const string DeleteSql = "DELETE FROM Products WHERE ProductId = @ProductId";

        private readonly string _connectionString;
        private readonly ILogger<ProductRepository> _logger;

        public ProductRepository(IConfiguration configuration, ILogger<ProductRepository> logger)
        {
            _connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured.");
            _logger = logger;
        }

        public async Task<Product?> GetByIdAsync(int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(SelectByIdSql, connection);
            command.Parameters.Add(Int("@ProductId", id));

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync() ? MapProduct(reader) : null;
        }

        public Task<IEnumerable<Product>> GetAllAsync() => QueryAsync(SelectAllSql);

        public Task<IEnumerable<Product>> GetActiveProductsAsync() => QueryAsync(SelectActiveSql);

        public IEnumerable<Product> GetByCategory(string category) =>
            Query(SelectByCategorySql, command =>
                command.Parameters.Add(Text("@Category", category, CategorySize)));

        public Product Add(Product product)
        {
            // Read the clock once so the stored value and the value handed back are the same
            // instant; the original read UtcNow twice and they could differ by microseconds.
            var createdDate = DateTime.UtcNow;

            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(InsertSql, connection);
            BindWritableColumns(command, product);
            command.Parameters.Add(DateTime2Legacy("@CreatedDate", createdDate));

            connection.Open();
            product.ProductId = ReadGeneratedId(command.ExecuteScalar());
            product.CreatedDate = createdDate;

            _logger.LogInformation("Product created with ID: {ProductId}", product.ProductId);
            return product;
        }

        public void Update(Product product)
        {
            Execute(UpdateSql, command =>
            {
                command.Parameters.Add(Int("@ProductId", product.ProductId));
                BindWritableColumns(command, product);
            });

            _logger.LogInformation("Product updated: {ProductId}", product.ProductId);
        }

        public void Delete(int id)
        {
            Execute(DeleteSql, command => command.Parameters.Add(Int("@ProductId", id)));

            _logger.LogInformation("Product deleted: {ProductId}", id);
        }

        // ----- shared execution -----
        //
        // The seven copies of the open/create/bind/execute/dispose scaffold collapse into these
        // three helpers. Add keeps its own body because it also reads back the generated identity.

        private async Task<IEnumerable<Product>> QueryAsync(
            string sql,
            Action<SqlCommand>? bindParameters = null)
        {
            var products = new List<Product>();

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            bindParameters?.Invoke(command);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                products.Add(MapProduct(reader));
            }

            return products;
        }

        private IEnumerable<Product> Query(string sql, Action<SqlCommand>? bindParameters = null)
        {
            var products = new List<Product>();

            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(sql, connection);
            bindParameters?.Invoke(command);

            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                products.Add(MapProduct(reader));
            }

            return products;
        }

        private void Execute(string sql, Action<SqlCommand> bindParameters)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(sql, connection);
            bindParameters(command);

            connection.Open();
            command.ExecuteNonQuery();
        }

        // ----- parameter binding -----

        private static void BindWritableColumns(SqlCommand command, Product product)
        {
            command.Parameters.Add(Text("@Name", product.Name, NameSize));
            command.Parameters.Add(Text("@Description", product.Description, DescriptionSize));
            command.Parameters.Add(Money("@Price", product.Price));
            command.Parameters.Add(Int("@StockQuantity", product.StockQuantity));
            command.Parameters.Add(Text("@Category", product.Category, CategorySize));
            command.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = product.IsActive });
        }

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

        private static SqlParameter Int(string name, int value) =>
            new(name, SqlDbType.Int) { Value = value };

        /// <summary>
        /// Pins the type without pinning precision or scale. Setting Scale explicitly would make the
        /// driver adjust the value client-side, which can change a stored price (12.345 becoming
        /// 12.34 rather than the 12.35 the server rounds to today). Adopt decimal(18,2) here once
        /// the ProductRepository characterization tests have run against a real server and recorded
        /// the current rounding direction.
        /// </summary>
        private static SqlParameter Money(string name, decimal value) =>
            new(name, SqlDbType.Decimal) { Value = value };

        /// <summary>
        /// The column is datetime2, but AddWithValue has always inferred SqlDbType.DateTime, which
        /// rounds to ~3.33 ms before transmission. Kept deliberately so stored timestamps do not
        /// silently gain precision; switch to SqlDbType.DateTime2 as a separate, tested change.
        /// </summary>
        private static SqlParameter DateTime2Legacy(string name, DateTime value) =>
            new(name, SqlDbType.DateTime) { Value = value };

        private static int ReadGeneratedId(object? scalar) =>
            scalar is null or DBNull
                ? throw new InvalidOperationException(
                    "The INSERT did not return a generated ProductId from SCOPE_IDENTITY().")
                : Convert.ToInt32(scalar);

        private static Product MapProduct(SqlDataReader reader)
        {
            return new Product
            {
                ProductId = reader.GetInt32("ProductId"),
                Name = reader.GetString("Name"),
                Description = reader.IsDBNull("Description") ? null : reader.GetString("Description"),
                Price = reader.GetDecimal("Price"),
                StockQuantity = reader.GetInt32("StockQuantity"),
                Category = reader.IsDBNull("Category") ? null : reader.GetString("Category"),
                CreatedDate = reader.GetDateTime("CreatedDate"),
                IsActive = reader.GetBoolean("IsActive")
            };
        }
    }
}
