using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private const string ConnectionStringName = "DefaultConnection";

        // Column widths mirror DatabaseSetup.sql. Binding parameters at the column's declared size
        // (rather than letting AddWithValue size them to each value) keeps one cached plan per
        // statement instead of one per distinct string length.
        private const int StatusSize = 50;
        private const int ShippingAddressSize = 500;

        /// <summary>
        /// The order/customer projection shared by all four read paths, which differ only in their
        /// WHERE and ORDER BY clauses. It selects the customer's full contact details onto every
        /// row; that is pinned deliberately by the characterization tests (finding SEC-4). Narrowing
        /// it is a response-DTO change for a later phase, not something to slip in here.
        /// </summary>
        private const string BaseOrderQuery = """
            SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.ShippingAddress,
                   c.FirstName, c.LastName, c.Email, c.Phone, c.Address, c.CreatedDate
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.CustomerId
            """;

        private const string SelectOrderByIdSql =
            $"{BaseOrderQuery} WHERE o.OrderId = @OrderId";

        private const string SelectAllOrdersSql =
            $"{BaseOrderQuery} ORDER BY o.OrderDate DESC";

        private const string SelectOrdersByCustomerSql =
            $"{BaseOrderQuery} WHERE o.CustomerId = @CustomerId ORDER BY o.OrderDate DESC";

        private const string SelectOrdersByStatusSql =
            $"{BaseOrderQuery} WHERE o.Status = @Status ORDER BY o.OrderDate DESC";

        /// <summary>
        /// Selects only Name, Description and Category from Products, so the nested Product on each
        /// item reports Price 0, StockQuantity 0 and IsActive true from C# defaults rather than from
        /// data. Pinned by the characterization tests; fixing it is a separate, deliberate change.
        /// </summary>
        private const string SelectOrderItemsSql = """
            SELECT oi.OrderItemId, oi.OrderId, oi.ProductId, oi.Quantity, oi.UnitPrice,
                   p.Name, p.Description, p.Category
            FROM OrderItems oi
            INNER JOIN Products p ON oi.ProductId = p.ProductId
            WHERE oi.OrderId = @OrderId
            """;

        private const string InsertOrderSql = """
            INSERT INTO Orders (CustomerId, OrderDate, TotalAmount, Status, ShippingAddress)
            VALUES (@CustomerId, @OrderDate, @TotalAmount, @Status, @ShippingAddress);
            SELECT CAST(SCOPE_IDENTITY() as int);
            """;

        private const string InsertOrderItemSql = """
            INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice)
            VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice);
            SELECT CAST(SCOPE_IDENTITY() as int);
            """;

        private const string UpdateOrderSql = """
            UPDATE Orders
            SET TotalAmount = @TotalAmount, Status = @Status, ShippingAddress = @ShippingAddress
            WHERE OrderId = @OrderId
            """;

        private const string DeleteOrderItemsSql = "DELETE FROM OrderItems WHERE OrderId = @OrderId";

        private const string DeleteOrderSql = "DELETE FROM Orders WHERE OrderId = @OrderId";

        private readonly string _connectionString;
        private readonly ILogger<OrderRepository> _logger;

        public OrderRepository(IConfiguration configuration, ILogger<OrderRepository> logger)
        {
            _connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured.");
            _logger = logger;
        }

        /// <summary>
        /// Two queries over one connection: the header, then the line items only if the header was
        /// found. Each read lives in its own method so its reader is disposed before the next one
        /// opens - a single connection cannot hold two open readers without MARS.
        /// </summary>
        public async Task<Order?> GetByIdAsync(int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var order = await ReadOrderAsync(connection, id);
            if (order != null)
            {
                order.OrderItems.AddRange(await ReadOrderItemsAsync(connection, id));
            }

            return order;
        }

        public Task<IEnumerable<Order>> GetAllAsync() => QueryOrdersAsync(SelectAllOrdersSql);

        public Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId) =>
            QueryOrdersAsync(SelectOrdersByCustomerSql, command =>
                command.Parameters.Add(Int("@CustomerId", customerId)));

        public IEnumerable<Order> GetByStatus(string status) =>
            QueryOrders(SelectOrdersByStatusSql, command =>
                command.Parameters.Add(Text("@Status", status, StatusSize)));

        public Order Add(Order order)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                order.OrderId = InsertOrder(connection, transaction, order);

                foreach (var item in order.OrderItems)
                {
                    // Assignment order matches the original: the generated id lands first, so a
                    // mid-loop failure leaves the same partial object state as before.
                    item.OrderItemId = InsertOrderItem(connection, transaction, order.OrderId, item);
                    item.OrderId = order.OrderId;
                }

                transaction.Commit();
            }
            catch
            {
                RollbackQuietly(transaction);
                throw;
            }

            _logger.LogInformation("Order created with ID: {OrderId}", order.OrderId);
            return order;
        }

        public void Update(Order order)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(UpdateOrderSql, connection);
            command.Parameters.Add(Int("@OrderId", order.OrderId));
            command.Parameters.Add(Money("@TotalAmount", order.TotalAmount));
            command.Parameters.Add(Text("@Status", order.Status, StatusSize));
            command.Parameters.Add(Text("@ShippingAddress", order.ShippingAddress, ShippingAddressSize));

            connection.Open();
            command.ExecuteNonQuery();

            _logger.LogInformation("Order updated: {OrderId}", order.OrderId);
        }

        public void Delete(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Children before parent, so the OrderItems -> Orders foreign key stays satisfied.
                ExecuteForOrder(connection, transaction, DeleteOrderItemsSql, id);
                ExecuteForOrder(connection, transaction, DeleteOrderSql, id);

                transaction.Commit();
            }
            catch
            {
                RollbackQuietly(transaction);
                throw;
            }

            _logger.LogInformation("Order deleted: {OrderId}", id);
        }

        // ----- reads -----

        private static async Task<Order?> ReadOrderAsync(SqlConnection connection, int orderId)
        {
            await using var command = new SqlCommand(SelectOrderByIdSql, connection);
            command.Parameters.Add(Int("@OrderId", orderId));

            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? MapOrder(reader) : null;
        }

        private static async Task<List<OrderItem>> ReadOrderItemsAsync(SqlConnection connection, int orderId)
        {
            var items = new List<OrderItem>();

            await using var command = new SqlCommand(SelectOrderItemsSql, connection);
            command.Parameters.Add(Int("@OrderId", orderId));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(MapOrderItem(reader));
            }

            return items;
        }

        private async Task<IEnumerable<Order>> QueryOrdersAsync(
            string sql,
            Action<SqlCommand>? bindParameters = null)
        {
            var orders = new List<Order>();

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            bindParameters?.Invoke(command);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                orders.Add(MapOrder(reader));
            }

            return orders;
        }

        private IEnumerable<Order> QueryOrders(string sql, Action<SqlCommand>? bindParameters = null)
        {
            var orders = new List<Order>();

            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(sql, connection);
            bindParameters?.Invoke(command);

            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(MapOrder(reader));
            }

            return orders;
        }

        // ----- transactional writes -----

        private static int InsertOrder(SqlConnection connection, SqlTransaction transaction, Order order)
        {
            using var command = new SqlCommand(InsertOrderSql, connection, transaction);
            command.Parameters.Add(Int("@CustomerId", order.CustomerId));
            command.Parameters.Add(DateTime2Legacy("@OrderDate", order.OrderDate));
            command.Parameters.Add(Money("@TotalAmount", order.TotalAmount));
            command.Parameters.Add(Text("@Status", order.Status, StatusSize));
            command.Parameters.Add(Text("@ShippingAddress", order.ShippingAddress, ShippingAddressSize));

            return ReadGeneratedId(command.ExecuteScalar(), "OrderId");
        }

        /// <summary>
        /// A fresh command per line item, matching the original. Hoisting one command out of the
        /// loop and re-binding values would be faster but changes what goes over the wire, so it is
        /// deferred until the transaction is covered by an executed test.
        /// </summary>
        private static int InsertOrderItem(
            SqlConnection connection,
            SqlTransaction transaction,
            int orderId,
            OrderItem item)
        {
            using var command = new SqlCommand(InsertOrderItemSql, connection, transaction);
            command.Parameters.Add(Int("@OrderId", orderId));
            command.Parameters.Add(Int("@ProductId", item.ProductId));
            command.Parameters.Add(Int("@Quantity", item.Quantity));
            command.Parameters.Add(Money("@UnitPrice", item.UnitPrice));

            return ReadGeneratedId(command.ExecuteScalar(), "OrderItemId");
        }

        private static void ExecuteForOrder(
            SqlConnection connection,
            SqlTransaction transaction,
            string sql,
            int orderId)
        {
            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add(Int("@OrderId", orderId));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Rolls back without letting a rollback failure escape. An unguarded Rollback() in a catch
        /// block replaces the exception that caused the rollback - which is the one worth reporting
        /// (finding ERR-6).
        /// </summary>
        private void RollbackQuietly(SqlTransaction transaction)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackFailure)
            {
                _logger.LogError(rollbackFailure, "Transaction rollback failed; reporting the original error.");
            }
        }

        // ----- parameter binding -----

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

        private static SqlParameter Int(string name, int value) =>
            new(name, SqlDbType.Int) { Value = value };

        /// <summary>
        /// Pins the type without pinning precision or scale. Setting Scale explicitly would make the
        /// driver adjust the value client-side, which can change a stored amount (12.345 becoming
        /// 12.34 rather than the 12.35 the server rounds to today). Adopt decimal(18,2) here once
        /// the OrderRepository characterization tests have run against a real server.
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

        private static int ReadGeneratedId(object? scalar, string idColumn) =>
            scalar is null or DBNull
                ? throw new InvalidOperationException(
                    $"The INSERT did not return a generated {idColumn} from SCOPE_IDENTITY().")
                : Convert.ToInt32(scalar);

        // ----- mapping -----

        private static Order MapOrder(SqlDataReader reader)
        {
            return new Order
            {
                OrderId = reader.GetInt32("OrderId"),
                CustomerId = reader.GetInt32("CustomerId"),
                OrderDate = reader.GetDateTime("OrderDate"),
                TotalAmount = reader.GetDecimal("TotalAmount"),
                Status = reader.GetString("Status"),
                ShippingAddress = reader.IsDBNull("ShippingAddress") ? null : reader.GetString("ShippingAddress"),
                Customer = new Customer
                {
                    CustomerId = reader.GetInt32("CustomerId"),
                    FirstName = reader.GetString("FirstName"),
                    LastName = reader.GetString("LastName"),
                    Email = reader.GetString("Email"),
                    Phone = reader.IsDBNull("Phone") ? null : reader.GetString("Phone"),
                    Address = reader.IsDBNull("Address") ? null : reader.GetString("Address"),
                    CreatedDate = reader.GetDateTime("CreatedDate")
                }
            };
        }

        private static OrderItem MapOrderItem(SqlDataReader reader)
        {
            return new OrderItem
            {
                OrderItemId = reader.GetInt32("OrderItemId"),
                OrderId = reader.GetInt32("OrderId"),
                ProductId = reader.GetInt32("ProductId"),
                Quantity = reader.GetInt32("Quantity"),
                UnitPrice = reader.GetDecimal("UnitPrice"),
                Product = new Product
                {
                    ProductId = reader.GetInt32("ProductId"),
                    Name = reader.GetString("Name"),
                    Description = reader.IsDBNull("Description") ? null : reader.GetString("Description"),
                    Category = reader.IsDBNull("Category") ? null : reader.GetString("Category")
                }
            };
        }
    }
}
