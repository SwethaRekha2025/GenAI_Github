using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;
using LegacyECommerceApi.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyECommerceApi.Tests.Integration
{
    /// <summary>
    /// Characterization tests for CustomerRepository against a real SQL Server.
    ///
    /// The repository constructs its own SqlConnection, so there is no seam that would allow a
    /// unit test; its constructor does take IConfiguration, which is why these need no production
    /// change. Skipped with a reason when no server is reachable.
    /// </summary>
    public class CustomerRepositoryTests : DatabaseTestBase
    {
        private static CustomerRepository CreateSut() =>
            new(TestDatabase.Configuration(), NullLogger<CustomerRepository>.Instance);

        private static Customer NewCustomer(string? email = null) => new()
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = email ?? $"{Guid.NewGuid():N}@example.com",
            Phone = "555-0101",
            Address = "1 Analytical Way"
        };

        // ---------- GetByIdAsync ----------

        [SqlServerFact]
        public async Task GetByIdAsync_WhenCustomerExists_MapsEveryColumn()
        {
            // Arrange
            var created = new DateTime(2025, 3, 4, 9, 30, 0, DateTimeKind.Utc);
            var id = await TestData.InsertCustomerAsync(
                "Grace", "Hopper", "grace@example.com", "555-1234", "2 Compiler Road", created);
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(customer);
            Assert.Equal(id, customer!.CustomerId);
            Assert.Equal("Grace", customer.FirstName);
            Assert.Equal("Hopper", customer.LastName);
            Assert.Equal("grace@example.com", customer.Email);
            Assert.Equal("555-1234", customer.Phone);
            Assert.Equal("2 Compiler Road", customer.Address);
            Assert.Equal(created, customer.CreatedDate);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenPhoneAndAddressAreNull_MapsThemAsNullNotEmptyString()
        {
            // Arrange - the two optional columns; IsDBNull handling is the mapper's only real logic.
            var id = await TestData.InsertCustomerAsync(phone: null, address: null);
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(customer);
            Assert.Null(customer!.Phone);
            Assert.Null(customer.Address);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenCustomerDoesNotExist_ReturnsNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByIdAsync(999_999);

            // Assert
            Assert.Null(customer);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_StoredUtcDateReadsBackWithUnspecifiedKind()
        {
            // Arrange - datetime2 carries no offset, so a UTC value round-trips as Unspecified and
            // therefore serialises without a trailing 'Z'. Pinned because a TimeProvider or DTO
            // refactor would be tempted to change it.
            var id = await TestData.InsertCustomerAsync(
                createdDate: new DateTime(2025, 3, 4, 9, 30, 0, DateTimeKind.Utc));
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(customer);
            Assert.Equal(DateTimeKind.Unspecified, customer!.CreatedDate.Kind);
        }

        // ---------- GetAllAsync ----------

        [SqlServerFact]
        public async Task GetAllAsync_OrdersByLastNameThenFirstName()
        {
            // Arrange - inserted in an order that does not match the expected output.
            await TestData.InsertCustomerAsync("Zoe", "Adams");
            await TestData.InsertCustomerAsync("Alan", "Turing");
            await TestData.InsertCustomerAsync("Bob", "Adams");
            var sut = CreateSut();

            // Act
            var customers = (await sut.GetAllAsync()).ToList();

            // Assert - the ORDER BY is part of the observed contract, so assert the full sequence.
            Assert.Equal(
                new[] { "Adams/Bob", "Adams/Zoe", "Turing/Alan" },
                customers.Select(c => $"{c.LastName}/{c.FirstName}"));
        }

        [SqlServerFact]
        public async Task GetAllAsync_WhenTableIsEmpty_ReturnsEmptySequenceNotNull()
        {
            // Arrange - the fixture reset leaves every table empty.
            var sut = CreateSut();

            // Act
            var customers = await sut.GetAllAsync();

            // Assert
            Assert.NotNull(customers);
            Assert.Empty(customers);
        }

        // ---------- Add ----------

        [SqlServerFact]
        public async Task Add_ReturnsTheSameInstanceMutatedWithTheGeneratedId()
        {
            // Arrange
            var sut = CreateSut();
            var customer = NewCustomer();

            // Act
            var returned = sut.Add(customer);

            // Assert - the caller's own object is mutated; it is not a copy.
            Assert.Same(customer, returned);
            Assert.True(returned.CustomerId > 0);
            Assert.Equal(1, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Customers"));
        }

        [SqlServerFact]
        public void Add_OverwritesAnyClientSuppliedCreatedDateWithUtcNow()
        {
            // Arrange
            var sut = CreateSut();
            var customer = NewCustomer();
            customer.CreatedDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var before = DateTime.UtcNow.AddSeconds(-5);

            // Act
            var returned = sut.Add(customer);

            // Assert
            Assert.InRange(returned.CreatedDate, before, DateTime.UtcNow.AddSeconds(5));
        }

        [SqlServerFact]
        public async Task Add_WithNullPhoneAndAddress_PersistsThemAsNull()
        {
            // Arrange
            var sut = CreateSut();
            var customer = NewCustomer();
            customer.Phone = null;
            customer.Address = null;

            // Act
            var returned = sut.Add(customer);
            var reloaded = await sut.GetByIdAsync(returned.CustomerId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Null(reloaded!.Phone);
            Assert.Null(reloaded.Address);
        }

        [SqlServerFact]
        public async Task Add_WhenEmailAlreadyExists_ThrowsSqlExceptionRatherThanReportingConflict()
        {
            // Arrange - PINS BR-7. The UNIQUE constraint on Email is the only thing enforcing this,
            // and the raw SqlException reaches the controller, which turns it into a 500 not a 409.
            await TestData.InsertCustomerAsync(email: "taken@example.com");
            var sut = CreateSut();
            var duplicate = NewCustomer("taken@example.com");

            // Act + Assert
            var ex = Assert.Throws<SqlException>(() => sut.Add(duplicate));
            Assert.Contains(ex.Number, new[] { 2601, 2627 });
        }

        // ---------- Update ----------

        [SqlServerFact]
        public async Task Update_PersistsTheChangedFields()
        {
            // Arrange
            var id = await TestData.InsertCustomerAsync("Old", "Name", "old@example.com");
            var sut = CreateSut();
            var customer = new Customer
            {
                CustomerId = id,
                FirstName = "New",
                LastName = "Name",
                Email = "new@example.com",
                Phone = "555-9999",
                Address = "9 New Street"
            };

            // Act
            sut.Update(customer);
            var reloaded = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Equal("New", reloaded!.FirstName);
            Assert.Equal("new@example.com", reloaded.Email);
            Assert.Equal("555-9999", reloaded.Phone);
        }

        [SqlServerFact]
        public async Task Update_WhenCustomerDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2. ExecuteNonQuery returns 0, the return value is discarded, and
            // the void signature gives the caller no way to learn nothing was updated.
            var sut = CreateSut();
            var ghost = NewCustomer();
            ghost.CustomerId = 999_999;

            // Act
            var exception = Record.Exception(() => sut.Update(ghost));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Customers"));
        }

        [SqlServerFact]
        public async Task Update_DoesNotModifyCreatedDate()
        {
            // Arrange - CreatedDate is deliberately absent from the SET list.
            var created = new DateTime(2025, 3, 4, 9, 30, 0, DateTimeKind.Utc);
            var id = await TestData.InsertCustomerAsync(createdDate: created);
            var sut = CreateSut();

            // Act
            sut.Update(new Customer
            {
                CustomerId = id,
                FirstName = "Changed",
                LastName = "Changed",
                Email = $"{Guid.NewGuid():N}@example.com"
            });
            var reloaded = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Equal(created, reloaded!.CreatedDate);
        }

        // ---------- Delete ----------

        [SqlServerFact]
        public async Task Delete_WhenCustomerHasNoOrders_RemovesTheRow()
        {
            // Arrange
            var id = await TestData.InsertCustomerAsync();
            var sut = CreateSut();

            // Act
            sut.Delete(id);

            // Assert
            Assert.Null(await sut.GetByIdAsync(id));
        }

        [SqlServerFact]
        public async Task Delete_WhenCustomerDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2.
            var sut = CreateSut();

            // Act
            var exception = Record.Exception(() => sut.Delete(999_999));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Customers"));
        }

        [SqlServerFact]
        public async Task Delete_WhenCustomerHasOrders_ThrowsForeignKeyViolation()
        {
            // Arrange - PINS BR-6. A hard delete with no dependency check; the FK is the only guard,
            // and its exception becomes an unexplained 500 at the API.
            var customerId = await TestData.InsertCustomerAsync();
            await TestData.InsertOrderAsync(customerId);
            var sut = CreateSut();

            // Act + Assert
            var ex = Assert.Throws<SqlException>(() => sut.Delete(customerId));
            Assert.Equal(547, ex.Number);
        }

        // ---------- GetByEmailAsync ----------

        [SqlServerFact]
        public async Task GetByEmailAsync_WhenEmailExists_ReturnsTheCustomer()
        {
            // Arrange - matching is an exact predicate in SQL; whether it is case sensitive depends
            // on the server collation, not on application code, so only exact case is asserted here.
            var id = await TestData.InsertCustomerAsync(email: "ada@example.com");
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByEmailAsync("ada@example.com");

            // Assert
            Assert.NotNull(customer);
            Assert.Equal(id, customer!.CustomerId);
        }

        [SqlServerFact]
        public async Task GetByEmailAsync_WhenEmailIsUnknown_ReturnsNull()
        {
            // Arrange
            await TestData.InsertCustomerAsync(email: "someone@example.com");
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByEmailAsync("nobody@example.com");

            // Assert
            Assert.Null(customer);
        }

        [SqlServerTheory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-email")]
        public async Task GetByEmailAsync_WithEmptyOrMalformedInput_ReturnsNullWithoutThrowing(string email)
        {
            // Arrange - the value is parameterised, so nothing is injectable and nothing validates it.
            await TestData.InsertCustomerAsync(email: "someone@example.com");
            var sut = CreateSut();

            // Act
            var customer = await sut.GetByEmailAsync(email);

            // Assert
            Assert.Null(customer);
        }
    }
}
