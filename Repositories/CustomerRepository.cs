using LegacyECommerceApi.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace LegacyECommerceApi.Repositories
{
    public class CustomerRepository : ICustomerRepository
    {
        private const string ConnectionStringName = "DefaultConnection";

        // Column widths mirror DatabaseSetup.sql. Binding parameters at the column's declared size
        // (rather than letting AddWithValue size them to each value) keeps one cached plan per
        // statement instead of one per distinct string length, which matters most for the Email
        // lookup: a fixed nvarchar(255) seeks the unique index without a per-length plan.
        private const int FirstNameSize = 100;
        private const int LastNameSize = 100;
        private const int EmailSize = 255;
        private const int PhoneSize = 20;
        private const int AddressSize = 500;

        private const string SelectColumns =
            "CustomerId, FirstName, LastName, Email, Phone, Address, CreatedDate";

        private const string SelectByIdSql =
            $"SELECT {SelectColumns} FROM Customers WHERE CustomerId = @CustomerId";

        private const string SelectByEmailSql =
            $"SELECT {SelectColumns} FROM Customers WHERE Email = @Email";

        private const string SelectAllSql =
            $"SELECT {SelectColumns} FROM Customers ORDER BY LastName, FirstName";

        private const string InsertSql = """
            INSERT INTO Customers (FirstName, LastName, Email, Phone, Address, CreatedDate)
            VALUES (@FirstName, @LastName, @Email, @Phone, @Address, @CreatedDate);
            SELECT CAST(SCOPE_IDENTITY() as int);
            """;

        private const string UpdateSql = """
            UPDATE Customers
            SET FirstName = @FirstName, LastName = @LastName, Email = @Email,
                Phone = @Phone, Address = @Address
            WHERE CustomerId = @CustomerId
            """;

        private const string DeleteSql = "DELETE FROM Customers WHERE CustomerId = @CustomerId";

        private readonly string _connectionString;
        private readonly ILogger<CustomerRepository> _logger;

        public CustomerRepository(IConfiguration configuration, ILogger<CustomerRepository> logger)
        {
            _connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured.");
            _logger = logger;
        }

        public Task<Customer?> GetByIdAsync(int id) =>
            QuerySingleAsync(SelectByIdSql, command =>
                command.Parameters.Add(Int("@CustomerId", id)));

        public Task<Customer?> GetByEmailAsync(string email) =>
            QuerySingleAsync(SelectByEmailSql, command =>
                command.Parameters.Add(Text("@Email", email, EmailSize)));

        public async Task<IEnumerable<Customer>> GetAllAsync()
        {
            // Kept inline rather than behind a shared helper: this is the only list query in the
            // class, so there is no duplication to remove, only nesting to flatten.
            var customers = new List<Customer>();

            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(SelectAllSql, connection);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                customers.Add(MapCustomer(reader));
            }

            return customers;
        }

        public Customer Add(Customer customer)
        {
            // Read the clock once so the stored value and the value handed back are the same
            // instant; the original read UtcNow twice and they could differ by microseconds.
            var createdDate = DateTime.UtcNow;

            using var connection = new SqlConnection(_connectionString);
            using var command = new SqlCommand(InsertSql, connection);
            BindWritableColumns(command, customer);
            command.Parameters.Add(DateTime2Legacy("@CreatedDate", createdDate));

            connection.Open();
            customer.CustomerId = ReadGeneratedId(command.ExecuteScalar());
            customer.CreatedDate = createdDate;

            _logger.LogInformation("Customer created with ID: {CustomerId}", customer.CustomerId);
            return customer;
        }

        public void Update(Customer customer)
        {
            Execute(UpdateSql, command =>
            {
                command.Parameters.Add(Int("@CustomerId", customer.CustomerId));
                BindWritableColumns(command, customer);
            });

            _logger.LogInformation("Customer updated: {CustomerId}", customer.CustomerId);
        }

        public void Delete(int id)
        {
            Execute(DeleteSql, command => command.Parameters.Add(Int("@CustomerId", id)));

            _logger.LogInformation("Customer deleted: {CustomerId}", id);
        }

        // ----- shared execution -----
        //
        // GetByIdAsync and GetByEmailAsync differed only in their SQL and their single parameter;
        // Update and Delete differed only in their SQL and parameters. Both pairs collapse here.

        private async Task<Customer?> QuerySingleAsync(string sql, Action<SqlCommand> bindParameters)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = new SqlCommand(sql, connection);
            bindParameters(command);

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync() ? MapCustomer(reader) : null;
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

        /// <summary>
        /// The single definition of the columns a caller may write. Add supplies CreatedDate on top
        /// of these; Update deliberately does not, which is what keeps CreatedDate immutable.
        /// </summary>
        private static void BindWritableColumns(SqlCommand command, Customer customer)
        {
            command.Parameters.Add(Text("@FirstName", customer.FirstName, FirstNameSize));
            command.Parameters.Add(Text("@LastName", customer.LastName, LastNameSize));
            command.Parameters.Add(Text("@Email", customer.Email, EmailSize));
            command.Parameters.Add(Text("@Phone", customer.Phone, PhoneSize));
            command.Parameters.Add(Text("@Address", customer.Address, AddressSize));
        }

        private static SqlParameter Text(string name, string? value, int size) =>
            new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

        private static SqlParameter Int(string name, int value) =>
            new(name, SqlDbType.Int) { Value = value };

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
                    "The INSERT did not return a generated CustomerId from SCOPE_IDENTITY().")
                : Convert.ToInt32(scalar);

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
