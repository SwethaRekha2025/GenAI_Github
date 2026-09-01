using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<CustomerRepository> _logger;

        public CustomerRepository(IConfiguration configuration, ILogger<CustomerRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
        }

        public async Task<Customer?> GetByIdAsync(int id)
        {
            const string query = @"
                SELECT CustomerId, FirstName, LastName, Email, Phone, Address, CreatedDate 
                FROM Customers 
                WHERE CustomerId = @CustomerId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CustomerId", id);
                    
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapCustomer(reader);
                        }
                    }
                }
            }
            return null;
        }

        public async Task<IEnumerable<Customer>> GetAllAsync()
        {
            const string query = @"
                SELECT CustomerId, FirstName, LastName, Email, Phone, Address, CreatedDate 
                FROM Customers 
                ORDER BY LastName, FirstName";

            var customers = new List<Customer>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            customers.Add(MapCustomer(reader));
                        }
                    }
                }
            }
            return customers;
        }

        public Customer Add(Customer customer)
        {
            const string query = @"
                INSERT INTO Customers (FirstName, LastName, Email, Phone, Address, CreatedDate)
                VALUES (@FirstName, @LastName, @Email, @Phone, @Address, @CreatedDate);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FirstName", customer.FirstName);
                    command.Parameters.AddWithValue("@LastName", customer.LastName);
                    command.Parameters.AddWithValue("@Email", customer.Email);
                    command.Parameters.AddWithValue("@Phone", (object?)customer.Phone ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Address", (object?)customer.Address ?? DBNull.Value);
                    command.Parameters.AddWithValue("@CreatedDate", DateTime.UtcNow);

                    connection.Open();
                    customer.CustomerId = (int)command.ExecuteScalar();
                    customer.CreatedDate = DateTime.UtcNow;
                }
            }

            _logger.LogInformation("Customer created with ID: {CustomerId}", customer.CustomerId);
            return customer;
        }

        public void Update(Customer customer)
        {
            const string query = @"
                UPDATE Customers 
                SET FirstName = @FirstName, LastName = @LastName, Email = @Email, 
                    Phone = @Phone, Address = @Address
                WHERE CustomerId = @CustomerId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CustomerId", customer.CustomerId);
                    command.Parameters.AddWithValue("@FirstName", customer.FirstName);
                    command.Parameters.AddWithValue("@LastName", customer.LastName);
                    command.Parameters.AddWithValue("@Email", customer.Email);
                    command.Parameters.AddWithValue("@Phone", (object?)customer.Phone ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Address", (object?)customer.Address ?? DBNull.Value);

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            _logger.LogInformation("Customer updated: {CustomerId}", customer.CustomerId);
        }

        public void Delete(int id)
        {
            const string query = "DELETE FROM Customers WHERE CustomerId = @CustomerId";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CustomerId", id);
                    
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            _logger.LogInformation("Customer deleted: {CustomerId}", id);
        }

        public async Task<Customer?> GetByEmailAsync(string email)
        {
            const string query = @"
                SELECT CustomerId, FirstName, LastName, Email, Phone, Address, CreatedDate 
                FROM Customers 
                WHERE Email = @Email";

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Email", email);
                    
                    await connection.OpenAsync();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapCustomer(reader);
                        }
                    }
                }
            }
            return null;
        }

        private static Customer MapCustomer(SqlDataReader reader)
        {
            return new Customer
            {
                CustomerId = reader.GetInt32("CustomerId"),
                FirstName = reader.GetString("FirstName"),
                LastName = reader.GetString("LastName"),
                Email = reader.GetString("Email"),
                Phone = reader.IsDBNull("Phone") ? null : reader.GetString("Phone"),
                Address = reader.IsDBNull("Address") ? null : reader.GetString("Address"),
                CreatedDate = reader.GetDateTime("CreatedDate")
            };
        }
    }
}
