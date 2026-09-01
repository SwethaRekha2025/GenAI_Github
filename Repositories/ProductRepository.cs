using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<ProductRepository> _logger;

        public ProductRepository(IConfiguration configuration, ILogger<ProductRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
        }

        public async Task<Product?> GetByIdAsync(int id)
        {
            const string query = @"
                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive 
                FROM Products 
                WHERE ProductId = @ProductId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProductId", id);
                    
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapProduct(reader);
                        }
                    }
                }
            }
            return null;
        }

        public async Task<IEnumerable<Product>> GetAllAsync()
        {
            const string query = @"
                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive 
                FROM Products 
                ORDER BY Name";

            var products = new List<Product>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            products.Add(MapProduct(reader));
                        }
                    }
                }
            }
            return products;
        }

        public Product Add(Product product)
        {
            const string query = @"
                INSERT INTO Products (Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive)
                VALUES (@Name, @Description, @Price, @StockQuantity, @Category, @CreatedDate, @IsActive);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Name", product.Name);
                    command.Parameters.AddWithValue("@Description", (object?)product.Description ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Price", product.Price);
                    command.Parameters.AddWithValue("@StockQuantity", product.StockQuantity);
                    command.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                    command.Parameters.AddWithValue("@CreatedDate", DateTime.UtcNow);
                    command.Parameters.AddWithValue("@IsActive", product.IsActive);

                    connection.Open();
                    product.ProductId = (int)command.ExecuteScalar();
                    product.CreatedDate = DateTime.UtcNow;
                }
            }

            _logger.LogInformation("Product created with ID: {ProductId}", product.ProductId);
            return product;
        }

        public void Update(Product product)
        {
            const string query = @"
                UPDATE Products 
                SET Name = @Name, Description = @Description, Price = @Price, 
                    StockQuantity = @StockQuantity, Category = @Category, IsActive = @IsActive
                WHERE ProductId = @ProductId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProductId", product.ProductId);
                    command.Parameters.AddWithValue("@Name", product.Name);
                    command.Parameters.AddWithValue("@Description", (object?)product.Description ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Price", product.Price);
                    command.Parameters.AddWithValue("@StockQuantity", product.StockQuantity);
                    command.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                    command.Parameters.AddWithValue("@IsActive", product.IsActive);

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            _logger.LogInformation("Product updated: {ProductId}", product.ProductId);
        }

        public void Delete(int id)
        {
            const string query = "DELETE FROM Products WHERE ProductId = @ProductId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProductId", id);
                    
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            _logger.LogInformation("Product deleted: {ProductId}", id);
        }

        public IEnumerable<Product> GetByCategory(string category)
        {
            const string query = @"
                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive 
                FROM Products 
                WHERE Category = @Category AND IsActive = 1
                ORDER BY Name";

            var products = new List<Product>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Category", category);
                    
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            products.Add(MapProduct(reader));
                        }
                    }
                }
            }
            return products;
        }

        public async Task<IEnumerable<Product>> GetActiveProductsAsync()
        {
            const string query = @"
                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive 
                FROM Products 
                WHERE IsActive = 1 AND StockQuantity > 0
                ORDER BY Name";

            var products = new List<Product>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            products.Add(MapProduct(reader));
                        }
                    }
                }
            }
            return products;
        }

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
