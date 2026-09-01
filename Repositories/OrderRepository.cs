using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<OrderRepository> _logger;

        public OrderRepository(IConfiguration configuration, ILogger<OrderRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
        }

        public async Task<Order?> GetByIdAsync(int id)
        {
            const string orderQuery = @"
                SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.ShippingAddress,
                       c.FirstName, c.LastName, c.Email, c.Phone, c.Address, c.CreatedDate
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.CustomerId
                WHERE o.OrderId = @OrderId";

            const string itemsQuery = @"
                SELECT oi.OrderItemId, oi.OrderId, oi.ProductId, oi.Quantity, oi.UnitPrice,
                       p.Name, p.Description, p.Category
                FROM OrderItems oi
                INNER JOIN Products p ON oi.ProductId = p.ProductId
                WHERE oi.OrderId = @OrderId";

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                Order? order = null;
                
                using (var command = new SqlCommand(orderQuery, connection))
                {
                    command.Parameters.AddWithValue("@OrderId", id);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            order = MapOrder(reader);
                        }
                    }
                }

                if (order != null)
                {
                    using (var command = new SqlCommand(itemsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@OrderId", id);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                order.OrderItems.Add(MapOrderItem(reader));
                            }
                        }
                    }
                }

                return order;
            }
        }

        public async Task<IEnumerable<Order>> GetAllAsync()
        {
            const string query = @"
                SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.ShippingAddress,
                       c.FirstName, c.LastName, c.Email, c.Phone, c.Address, c.CreatedDate
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.CustomerId
                ORDER BY o.OrderDate DESC";

            var orders = new List<Order>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            orders.Add(MapOrder(reader));
                        }
                    }
                }
            }
            return orders;
        }

        public Order Add(Order order)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        const string orderQuery = @"
                            INSERT INTO Orders (CustomerId, OrderDate, TotalAmount, Status, ShippingAddress)
                            VALUES (@CustomerId, @OrderDate, @TotalAmount, @Status, @ShippingAddress);
                            SELECT CAST(SCOPE_IDENTITY() as int);";

                        using (var command = new SqlCommand(orderQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@CustomerId", order.CustomerId);
                            command.Parameters.AddWithValue("@OrderDate", order.OrderDate);
                            command.Parameters.AddWithValue("@TotalAmount", order.TotalAmount);
                            command.Parameters.AddWithValue("@Status", order.Status);
                            command.Parameters.AddWithValue("@ShippingAddress", (object?)order.ShippingAddress ?? DBNull.Value);

                            order.OrderId = (int)command.ExecuteScalar();
                        }

                        foreach (var item in order.OrderItems)
                        {
                            const string itemQuery = @"
                                INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice)
                                VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice);
                                SELECT CAST(SCOPE_IDENTITY() as int);";

                            using (var command = new SqlCommand(itemQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@OrderId", order.OrderId);
                                command.Parameters.AddWithValue("@ProductId", item.ProductId);
                                command.Parameters.AddWithValue("@Quantity", item.Quantity);
                                command.Parameters.AddWithValue("@UnitPrice", item.UnitPrice);

                                item.OrderItemId = (int)command.ExecuteScalar();
                                item.OrderId = order.OrderId;
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            _logger.LogInformation("Order created with ID: {OrderId}", order.OrderId);
            return order;
        }

        public void Update(Order order)
        {
            const string query = @"
                UPDATE Orders 
                SET TotalAmount = @TotalAmount, Status = @Status, ShippingAddress = @ShippingAddress
                WHERE OrderId = @OrderId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@OrderId", order.OrderId);
                    command.Parameters.AddWithValue("@TotalAmount", order.TotalAmount);
                    command.Parameters.AddWithValue("@Status", order.Status);
                    command.Parameters.AddWithValue("@ShippingAddress", (object?)order.ShippingAddress ?? DBNull.Value);

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            _logger.LogInformation("Order updated: {OrderId}", order.OrderId);
        }

        public void Delete(int id)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        const string deleteItemsQuery = "DELETE FROM OrderItems WHERE OrderId = @OrderId";
                        using (var command = new SqlCommand(deleteItemsQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@OrderId", id);
                            command.ExecuteNonQuery();
                        }

                        const string deleteOrderQuery = "DELETE FROM Orders WHERE OrderId = @OrderId";
                        using (var command = new SqlCommand(deleteOrderQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@OrderId", id);
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            _logger.LogInformation("Order deleted: {OrderId}", id);
        }

        public async Task<IEnumerable<Order>> GetByCustomerIdAsync(int customerId)
        {
            const string query = @"
                SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.ShippingAddress,
                       c.FirstName, c.LastName, c.Email, c.Phone, c.Address, c.CreatedDate
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.CustomerId
                WHERE o.CustomerId = @CustomerId
                ORDER BY o.OrderDate DESC";

            var orders = new List<Order>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CustomerId", customerId);
                    
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            orders.Add(MapOrder(reader));
                        }
                    }
                }
            }
            return orders;
        }

        public IEnumerable<Order> GetByStatus(string status)
        {
            const string query = @"
                SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.ShippingAddress,
                       c.FirstName, c.LastName, c.Email, c.Phone, c.Address, c.CreatedDate
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.CustomerId
                WHERE o.Status = @Status
                ORDER BY o.OrderDate DESC";

            var orders = new List<Order>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Status", status);
                    
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            orders.Add(MapOrder(reader));
                        }
                    }
                }
            }
            return orders;
        }

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
