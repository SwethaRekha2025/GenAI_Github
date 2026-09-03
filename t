warning: in the working copy of 'Repositories/ProductRepository.cs', LF will be replaced by CRLF the next time Git touches it
[1mdiff --git a/LegacyECommerceApi.csproj b/LegacyECommerceApi.csproj[m
[1mindex 4d319a0..8daa69c 100644[m
[1m--- a/LegacyECommerceApi.csproj[m
[1m+++ b/LegacyECommerceApi.csproj[m
[36m@@ -6,6 +6,16 @@[m
     <ImplicitUsings>enable</ImplicitUsings>[m
   </PropertyGroup>[m
 [m
[32m+[m[32m  <ItemGroup>[m
[32m+[m[32m    <!-- The test project lives under tests/ inside this project's directory, so the Web SDK's[m
[32m+[m[32m         default **/*.cs glob would otherwise compile test sources into the application assembly[m
[32m+[m[32m         and drag xUnit into the shipped output. Compile-only exclusion; no runtime effect. -->[m
[32m+[m[32m    <Compile Remove="tests/**" />[m
[32m+[m[32m    <Content Remove="tests/**" />[m
[32m+[m[32m    <None Remove="tests/**" />[m
[32m+[m[32m    <EmbeddedResource Remove="tests/**" />[m
[32m+[m[32m  </ItemGroup>[m
[32m+[m
   <ItemGroup>[m
     <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.21" />[m
     <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />[m
[1mdiff --git a/Repositories/ProductRepository.cs b/Repositories/ProductRepository.cs[m
[1mindex 30a3854..bf40010 100644[m
[1m--- a/Repositories/ProductRepository.cs[m
[1m+++ b/Repositories/ProductRepository.cs[m
[36m@@ -6,92 +6,90 @@[m [mnamespace LegacyECommerceApi.Repositories[m
 {[m
     public class ProductRepository : IProductRepository[m
     {[m
[32m+[m[32m        private const string ConnectionStringName = "DefaultConnection";[m
[32m+[m
[32m+[m[32m        // Column widths mirror DatabaseSetup.sql. Binding parameters at the column's declared size[m
[32m+[m[32m        // (rather than letting AddWithValue size them to each value) keeps one cached plan per[m
[32m+[m[32m        // statement instead of one per distinct string length.[m
[32m+[m[32m        private const int NameSize = 200;[m
[32m+[m[32m        private const int DescriptionSize = 1000;[m
[32m+[m[32m        private const int CategorySize = 100;[m
[32m+[m
[32m+[m[32m        private const string SelectColumns =[m
[32m+[m[32m            "ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive";[m
[32m+[m
[32m+[m[32m        private const string SelectByIdSql =[m
[32m+[m[32m            $"SELECT {SelectColumns} FROM Products WHERE ProductId = @ProductId";[m
[32m+[m
[32m+[m[32m        private const string SelectAllSql =[m
[32m+[m[32m            $"SELECT {SelectColumns} FROM Products ORDER BY Name";[m
[32m+[m
[32m+[m[32m        private const string SelectByCategorySql =[m
[32m+[m[32m            $"SELECT {SelectColumns} FROM Products WHERE Category = @Category AND IsActive = 1 ORDER BY Name";[m
[32m+[m
[32m+[m[32m        private const string SelectActiveSql =[m
[32m+[m[32m            $"SELECT {SelectColumns} FROM Products WHERE IsActive = 1 AND StockQuantity > 0 ORDER BY Name";[m
[32m+[m
[32m+[m[32m        private const string InsertSql = """[m
[32m+[m[32m            INSERT INTO Products (Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive)[m
[32m+[m[32m            VALUES (@Name, @Description, @Price, @StockQuantity, @Category, @CreatedDate, @IsActive);[m
[32m+[m[32m            SELECT CAST(SCOPE_IDENTITY() as int);[m
[32m+[m[32m            """;[m
[32m+[m
[32m+[m[32m        private const string UpdateSql = """[m
[32m+[m[32m            UPDATE Products[m
[32m+[m[32m            SET Name = @Name, Description = @Description, Price = @Price,[m
[32m+[m[32m                StockQuantity = @StockQuantity, Category = @Category, IsActive = @IsActive[m
[32m+[m[32m            WHERE ProductId = @ProductId[m
[32m+[m[32m            """;[m
[32m+[m
[32m+[m[32m        private const string DeleteSql = "DELETE FROM Products WHERE ProductId = @ProductId";[m
[32m+[m
         private readonly string _connectionString;[m
         private readonly ILogger<ProductRepository> _logger;[m
 [m
         public ProductRepository(IConfiguration configuration, ILogger<ProductRepository> logger)[m
         {[m
[31m-            _connectionString = configuration.GetConnectionString("DefaultConnection") [m
[31m-                ?? throw new ArgumentNullException(nameof(configuration));[m
[32m+[m[32m            _connectionString = configuration.GetConnectionString(ConnectionStringName)[m
[32m+[m[32m                ?? throw new InvalidOperationException([m
[32m+[m[32m                    $"Connection string '{ConnectionStringName}' is not configured.");[m
             _logger = logger;[m
         }[m
 [m
         public async Task<Product?> GetByIdAsync(int id)[m
         {[m
[31m-            const string query = @"[m
[31m-                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive [m
[31m-                FROM Products [m
[31m-                WHERE ProductId = @ProductId";[m
[32m+[m[32m            await using var connection = new SqlConnection(_connectionString);[m
[32m+[m[32m            await using var command = new SqlCommand(SelectByIdSql, connection);[m
[32m+[m[32m            command.Parameters.Add(Int("@ProductId", id));[m
 [m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[31m-            {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    command.Parameters.AddWithValue("@ProductId", id);[m
[31m-                    [m
[31m-                    await connection.OpenAsync();[m
[31m-                    using (var reader = await command.ExecuteReaderAsync())[m
[31m-                    {[m
[31m-                        if (await reader.ReadAsync())[m
[31m-                        {[m
[31m-                            return MapProduct(reader);[m
[31m-                        }[m
[31m-                    }[m
[31m-                }[m
[31m-            }[m
[31m-            return null;[m
[32m+[m[32m            await connection.OpenAsync();[m
[32m+[m[32m            await using var reader = await command.ExecuteReaderAsync();[m
[32m+[m
[32m+[m[32m            return await reader.ReadAsync() ? MapProduct(reader) : null;[m
         }[m
 [m
[31m-        public async Task<IEnumerable<Product>> GetAllAsync()[m
[31m-        {[m
[31m-            const string query = @"[m
[31m-                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive [m
[31m-                FROM Products [m
[31m-                ORDER BY Name";[m
[32m+[m[32m        public Task<IEnumerable<Product>> GetAllAsync() => QueryAsync(SelectAllSql);[m
 [m
[31m-            var products = new List<Product>();[m
[31m-            [m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[31m-            {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    await connection.OpenAsync();[m
[31m-                    using (var reader = await command.ExecuteReaderAsync())[m
[31m-                    {[m
[31m-                        while (await reader.ReadAsync())[m
[31m-                        {[m
[31m-                            products.Add(MapProduct(reader));[m
[31m-                        }[m
[31m-                    }[m
[31m-                }[m
[31m-            }[m
[31m-            return products;[m
[31m-        }[m
[32m+[m[32m        public Task<IEnumerable<Product>> GetActiveProductsAsync() => QueryAsync(SelectActiveSql);[m
[32m+[m
[32m+[m[32m        public IEnumerable<Product> GetByCategory(string category) =>[m
[32m+[m[32m            Query(SelectByCategorySql, command =>[m
[32m+[m[32m                command.Parameters.Add(Text("@Category", category, CategorySize)));[m
 [m
         public Product Add(Product product)[m
         {[m
[31m-            const string query = @"[m
[31m-                INSERT INTO Products (Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive)[m
[31m-                VALUES (@Name, @Description, @Price, @StockQuantity, @Category, @CreatedDate, @IsActive);[m
[31m-                SELECT CAST(SCOPE_IDENTITY() as int);";[m
[32m+[m[32m            // Read the clock once so the stored value and the value handed back are the same[m
[32m+[m[32m            // instant; the original read UtcNow twice and they could differ by microseconds.[m
[32m+[m[32m            var createdDate = DateTime.UtcNow;[m
 [m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[31m-            {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    command.Parameters.AddWithValue("@Name", product.Name);[m
[31m-                    command.Parameters.AddWithValue("@Description", (object?)product.Description ?? DBNull.Value);[m
[31m-                    command.Parameters.AddWithValue("@Price", product.Price);[m
[31m-                    command.Parameters.AddWithValue("@StockQuantity", product.StockQuantity);[m
[31m-                    command.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);[m
[31m-                    command.Parameters.AddWithValue("@CreatedDate", DateTime.UtcNow);[m
[31m-                    command.Parameters.AddWithValue("@IsActive", product.IsActive);[m
[31m-[m
[31m-                    connection.Open();[m
[31m-                    product.ProductId = (int)command.ExecuteScalar();[m
[31m-                    product.CreatedDate = DateTime.UtcNow;[m
[31m-                }[m
[31m-            }[m
[32m+[m[32m            using var connection = new SqlConnection(_connectionString);[m
[32m+[m[32m            using var command = new SqlCommand(InsertSql, connection);[m
[32m+[m[32m            BindWritableColumns(command, product);[m
[32m+[m[32m            command.Parameters.Add(DateTime2Legacy("@CreatedDate", createdDate));[m
[32m+[m
[32m+[m[32m            connection.Open();[m
[32m+[m[32m            product.ProductId = ReadGeneratedId(command.ExecuteScalar());[m
[32m+[m[32m            product.CreatedDate = createdDate;[m
 [m
             _logger.LogInformation("Product created with ID: {ProductId}", product.ProductId);[m
             return product;[m
[36m@@ -99,106 +97,117 @@[m [mnamespace LegacyECommerceApi.Repositories[m
 [m
         public void Update(Product product)[m
         {[m
[31m-            const string query = @"[m
[31m-                UPDATE Products [m
[31m-                SET Name = @Name, Description = @Description, Price = @Price, [m
[31m-                    StockQuantity = @StockQuantity, Category = @Category, IsActive = @IsActive[m
[31m-                WHERE ProductId = @ProductId";[m
[31m-[m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[32m+[m[32m            Execute(UpdateSql, command =>[m
             {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    command.Parameters.AddWithValue("@ProductId", product.ProductId);[m
[31m-                    command.Parameters.AddWithValue("@Name", product.Name);[m
[31m-                    command.Parameters.AddWithValue("@Description", (object?)product.Description ?? DBNull.Value);[m
[31m-                    command.Parameters.AddWithValue("@Price", product.Price);[m
[31m-                    command.Parameters.AddWithValue("@StockQuantity", product.StockQuantity);[m
[31m-                    command.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);[m
[31m-                    command.Parameters.AddWithValue("@IsActive", product.IsActive);[m
[31m-[m
[31m-                    connection.Open();[m
[31m-                    command.ExecuteNonQuery();[m
[31m-                }[m
[31m-            }[m
[32m+[m[32m                command.Parameters.Add(Int("@ProductId", product.ProductId));[m
[32m+[m[32m                BindWritableColumns(command, product);[m
[32m+[m[32m            });[m
 [m
             _logger.LogInformation("Product updated: {ProductId}", product.ProductId);[m
         }[m
 [m
         public void Delete(int id)[m
         {[m
[31m-            const string query = "DELETE FROM Products WHERE ProductId = @ProductId";[m
[31m-[m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[31m-            {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    command.Parameters.AddWithValue("@ProductId", id);[m
[31m-                    [m
[31m-                    connection.Open();[m
[31m-                    command.ExecuteNonQuery();[m
[31m-                }[m
[31m-            }[m
[32m+[m[32m            Execute(DeleteSql, command => command.Parameters.Add(Int("@ProductId", id)));[m
 [m
             _logger.LogInformation("Product deleted: {ProductId}", id);[m
         }[m
 [m
[31m-        public IEnumerable<Product> GetByCategory(string category)[m
[31m-        {[m
[31m-            const string query = @"[m
[31m-                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive [m
[31m-                FROM Products [m
[31m-                WHERE Category = @Category AND IsActive = 1[m
[31m-                ORDER BY Name";[m
[32m+[m[32m        // ----- shared execution -----[m
[32m+[m[32m        //[m
[32m+[m[32m        // The seven copies of the open/create/bind/execute/dispose scaffold collapse into these[m
[32m+[m[32m        // three helpers. Add keeps its own body because it also reads back the generated identity.[m
 [m
[32m+[m[32m        private async Task<IEnumerable<Product>> QueryAsync([m
[32m+[m[32m            string sql,[m
[32m+[m[32m            Action<SqlCommand>? bindParameters = null)[m
[32m+[m[32m        {[m
             var products = new List<Product>();[m
[31m-            [m
[31m-            using (var connection = new SqlConnection(_connectionString))[m
[32m+[m
[32m+[m[32m            await using var connection = new SqlConnection(_connectionString);[m
[32m+[m[32m            await using var command = new SqlCommand(sql, connection);[m
[32m+[m[32m            bindParameters?.Invoke(command);[m
[32m+[m
[32m+[m[32m            await connection.OpenAsync();[m
[32m+[m[32m            await using var reader = await command.ExecuteReaderAsync();[m
[32m+[m[32m            while (await reader.ReadAsync())[m
             {[m
[31m-                using (var command = new SqlCommand(query, connection))[m
[31m-                {[m
[31m-                    command.Parameters.AddWithValue("@Category", category);[m
[31m-                    [m
[31m-                    connection.Open();[m
[31m-                    using (var reader = command.ExecuteReader())[m
[31m-                    {[m
[31m-                        while (reader.Read())[m
[31m-                        {[m
[31m-                            products.Add(MapProduct(reader));[m
[31m-                        }[m
[31m-                    }[m
[31m-                }[m
[32m+[m[32m                products.Add(MapProduct(reader));[m
             }[m
[32m+[m
             return products;[m
         }[m
 [m
[31m-        public async Task<IEnumerable<Product>> GetActiveProductsAsync()[m
[32m+[m[32m        private IEnumerable<Product> Query(string sql, Action<SqlCommand>? bindParameters = null)[m
         {[m
[31m-            const string query = @"[m
[31m-                SELECT ProductId, Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive [m
[31m-                FROM Products [m
[31m-                WHERE IsActive = 1 AND StockQuantity > 0[m
[31m-                ORDER BY Name";[m
[31m-[m
             var products = new List<Product>();[m
[31m-            [m
[31m-            using (var co