using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Tests.Infrastructure
{
    /// <summary>
    /// Seeds rows with raw SQL rather than through the repositories, so an arrange step never
    /// depends on the behaviour the test is trying to characterise.
    /// </summary>
    public static class TestData
    {
        public static async Task<int> InsertCustomerAsync(
            string firstName = "Test",
            string lastName = "Customer",
            string? email = null,
            string? phone = "555-0000",
            string? address = "1 Test Street",
            DateTime? createdDate = null)
        {
            const string sql = """
                INSERT INTO Customers (FirstName, LastName, Email, Phone, Address, CreatedDate)
                VALUES (@FirstName, @LastName, @Email, @Phone, @Address, @CreatedDate);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            return await InsertAsync(sql,
                ("@FirstName", firstName),
                ("@LastName", lastName),
                ("@Email", email ?? $"{Guid.NewGuid():N}@example.com"),
                ("@Phone", (object?)phone ?? DBNull.Value),
                ("@Address", (object?)address ?? DBNull.Value),
                ("@CreatedDate", createdDate ?? new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
        }

        public static async Task<int> InsertProductAsync(
            string name = "Test Product",
            decimal price = 10.00m,
            int stockQuantity = 100,
            string? category = "TestCategory",
            bool isActive = true,
            string? description = "A test product",
            DateTime? createdDate = null)
        {
            const string sql = """
                INSERT INTO Products (Name, Description, Price, StockQuantity, Category, CreatedDate, IsActive)
                VALUES (@Name, @Description, @Price, @StockQuantity, @Category, @CreatedDate, @IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            return await InsertAsync(sql,
                ("@Name", name),
                ("@Description", (object?)description ?? DBNull.Value),
                ("@Price", price),
                ("@StockQuantity", stockQuantity),
                ("@Category", (object?)category ?? DBNull.Value),
                ("@CreatedDate", createdDate ?? new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
                ("@IsActive", isActive));
        }

        public static async Task<int> InsertOrderAsync(
            int customerId,
            decimal totalAmount = 100.00m,
            string status = "Pending",
            string? shippingAddress = "1 Test Street",
            DateTime? orderDate = null)
        {
            const string sql = """
                INSERT INTO Orders (CustomerId, OrderDate, TotalAmount, Status, ShippingAddress)
                VALUES (@CustomerId, @OrderDate, @TotalAmount, @Status, @ShippingAddress);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            return await InsertAsync(sql,
                ("@CustomerId", customerId),
                ("@OrderDate", orderDate ?? new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc)),
                ("@TotalAmount", totalAmount),
                ("@Status", status),
                ("@ShippingAddress", (object?)shippingAddress ?? DBNull.Value));
        }

        public static async Task<int> InsertOrderItemAsync(
            int orderId,
            int productId,
            int quantity = 1,
            decimal unitPrice = 10.00m)
        {
            const string sql = """
                INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice)
                VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            return await InsertAsync(sql,
                ("@OrderId", orderId),
                ("@ProductId", productId),
                ("@Quantity", quantity),
                ("@UnitPrice", unitPrice));
        }

        private static async Task<int> InsertAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(TestDatabase.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
    }
}
