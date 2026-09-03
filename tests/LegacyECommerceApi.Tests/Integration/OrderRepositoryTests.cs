using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;
using LegacyECommerceApi.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyECommerceApi.Tests.Integration
{
    /// <summary>
    /// Characterization tests for OrderRepository against a real SQL Server.
    ///
    /// The highest-value suite in the project: Add and Delete are the only transactional code in
    /// the application and both are rewritten in Phase 4, and the read paths carry three separate
    /// shape quirks a refactor would plausibly "fix" without noticing the contract changed.
    /// </summary>
    public class OrderRepositoryTests : DatabaseTestBase
    {
        private static OrderRepository CreateSut() =>
            new(TestDatabase.Configuration(), NullLogger<OrderRepository>.Instance);

        private static Order NewOrder(int customerId, params OrderItem[] items) => new()
        {
            CustomerId = customerId,
            OrderDate = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            TotalAmount = 100.00m,
            Status = "Pending",
            ShippingAddress = "1 Test Street",
            OrderItems = items.ToList()
        };

        private static OrderItem Item(int productId, int quantity = 1, decimal unitPrice = 100.00m) =>
            new() { ProductId = productId, Quantity = quantity, UnitPrice = unitPrice };

        // ---------- Add: the transaction ----------

        [SqlServerFact]
        public async Task Add_WhenASubsequentLineItemFails_RollsBackTheOrderHeader()
        {
            // Arrange - the single most important test in the suite. The first item is valid and the
            // second violates the OrderItems -> Products foreign key, so the failure happens after
            // the header insert has already succeeded inside the transaction.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var sut = CreateSut();
            var order = NewOrder(customerId, Item(productId), Item(999_999));

            // Act
            var ex = Assert.Throws<SqlException>(() => sut.Add(order));

            // Assert - nothing survives: no header, no first line item.
            Assert.Equal(547, ex.Number);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM OrderItems"));
        }

        [SqlServerFact]
        public async Task Add_WritesGeneratedIdsBackOntoTheOrderAndEveryItem()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var sut = CreateSut();
            var order = NewOrder(customerId, Item(productId), Item(productId, 2));

            // Act
            var returned = sut.Add(order);

            // Assert
            Assert.Same(order, returned);
            Assert.True(returned.OrderId > 0);
            Assert.All(returned.OrderItems, item =>
            {
                Assert.True(item.OrderItemId > 0);
                Assert.Equal(returned.OrderId, item.OrderId);
            });
        }

        [SqlServerFact]
        public async Task Add_PersistsClientSuppliedPricesVerbatimWithoutConsultingTheCatalogue()
        {
            // Arrange - PINS BR-1. The product costs 999.99; the order claims one cent. Nothing
            // reads Products.Price during order creation, so the tampered figure is what persists.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync("Laptop", price: 999.99m);
            var sut = CreateSut();
            var order = NewOrder(customerId, Item(productId, 1, 0.01m));
            order.TotalAmount = 0.01m;

            // Act
            var returned = sut.Add(order);

            // Assert
            var storedTotal = await TestDatabase.ScalarAsync<decimal>(
                "SELECT TotalAmount FROM Orders WHERE OrderId = @Id", ("@Id", returned.OrderId));
            var storedUnitPrice = await TestDatabase.ScalarAsync<decimal>(
                "SELECT UnitPrice FROM OrderItems WHERE OrderId = @Id", ("@Id", returned.OrderId));

            Assert.Equal(0.01m, storedTotal);
            Assert.Equal(0.01m, storedUnitPrice);
        }

        [SqlServerFact]
        public async Task Add_LeavesProductStockQuantityCompletelyUnchanged()
        {
            // Arrange - PINS BR-2. Ordering ten thousand units of a product with fifty in stock
            // succeeds and moves nothing.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync(stockQuantity: 50);
            var sut = CreateSut();
            var order = NewOrder(customerId, Item(productId, quantity: 10_000));

            // Act
            sut.Add(order);

            // Assert
            var stock = await TestDatabase.ScalarAsync<int>(
                "SELECT StockQuantity FROM Products WHERE ProductId = @Id", ("@Id", productId));
            Assert.Equal(50, stock);
        }

        [SqlServerFact]
        public async Task Add_WhenCustomerIdIsUnknown_ThrowsForeignKeyViolationInsteadOfValidating()
        {
            // Arrange - PINS BR-5. A client error surfaces as a database exception, which the
            // controller's catch-all turns into a 500 rather than a 400.
            var productId = await TestData.InsertProductAsync();
            var sut = CreateSut();
            var order = NewOrder(999_999, Item(productId));

            // Act + Assert
            var ex = Assert.Throws<SqlException>(() => sut.Add(order));
            Assert.Equal(547, ex.Number);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
        }

        [SqlServerFact]
        public async Task Add_WithAnEmptyItemList_CreatesAHeaderOnlyOrder()
        {
            // Arrange - PINS BR-9. An order with no lines and a positive total is accepted.
            var customerId = await TestData.InsertCustomerAsync();
            var sut = CreateSut();
            var order = NewOrder(customerId);

            // Act
            var returned = sut.Add(order);

            // Assert
            Assert.True(returned.OrderId > 0);
            Assert.Equal(1, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM OrderItems"));
        }

        [SqlServerFact]
        public async Task Add_WithNullShippingAddress_PersistsItAsNull()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            var sut = CreateSut();
            var order = NewOrder(customerId);
            order.ShippingAddress = null;

            // Act
            var returned = sut.Add(order);
            var reloaded = await sut.GetByIdAsync(returned.OrderId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Null(reloaded!.ShippingAddress);
        }

        // ---------- GetByIdAsync ----------

        [SqlServerFact]
        public async Task GetByIdAsync_PopulatesOrderItemsAndTheCustomer()
        {
            // Arrange - the only endpoint that loads line items.
            var customerId = await TestData.InsertCustomerAsync("Ada", "Lovelace", "ada@example.com");
            var productId = await TestData.InsertProductAsync("Widget");
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId, 2, 25.00m);
            var sut = CreateSut();

            // Act
            var order = await sut.GetByIdAsync(orderId);

            // Assert
            Assert.NotNull(order);
            Assert.Equal("Ada", order!.Customer!.FirstName);
            var item = Assert.Single(order.OrderItems);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(25.00m, item.UnitPrice);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_NestedProductIsPartialAndReportsDefaultsAsIfTheyWereData()
        {
            // Arrange - PINS partial map. The items query selects only Name, Description and
            // Category, so Price, StockQuantity and IsActive come from C# field initialisers.
            // A client cannot distinguish these defaults from real values.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync("Laptop", price: 999.99m, stockQuantity: 50);
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId, 1, 999.99m);
            var sut = CreateSut();

            // Act
            var order = await sut.GetByIdAsync(orderId);

            // Assert
            var product = Assert.Single(order!.OrderItems).Product;
            Assert.NotNull(product);
            Assert.Equal("Laptop", product!.Name);
            Assert.Equal(0m, product.Price);          // real price is 999.99
            Assert.Equal(0, product.StockQuantity);   // real stock is 50
            Assert.True(product.IsActive);            // initialiser default, not a database read
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenOrderDoesNotExist_ReturnsNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act + Assert
            Assert.Null(await sut.GetByIdAsync(999_999));
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenOrderHasNoItems_ReturnsEmptyOrderItems()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            var orderId = await TestData.InsertOrderAsync(customerId);
            var sut = CreateSut();

            // Act
            var order = await sut.GetByIdAsync(orderId);

            // Assert
            Assert.NotNull(order);
            Assert.Empty(order!.OrderItems);
        }

        // ---------- List paths ----------

        [SqlServerFact]
        public async Task GetAllAsync_AlwaysReturnsEmptyOrderItemsEvenWhenTheOrderHasLines()
        {
            // Arrange - PINS SQL-6. The list query never touches OrderItems, so an empty array is
            // indistinguishable from an order that genuinely has no lines.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId);
            var sut = CreateSut();

            // Act
            var orders = (await sut.GetAllAsync()).ToList();

            // Assert
            Assert.Empty(Assert.Single(orders).OrderItems);
        }

        [SqlServerFact]
        public async Task GetAllAsync_PopulatesTheCompleteCustomerIncludingContactDetails()
        {
            // Arrange - PINS SEC-4. Every order row carries the customer's email, phone and address.
            var customerId = await TestData.InsertCustomerAsync(
                "Ada", "Lovelace", "ada@example.com", "555-0101", "1 Analytical Way");
            await TestData.InsertOrderAsync(customerId);
            var sut = CreateSut();

            // Act
            var order = Assert.Single(await sut.GetAllAsync());

            // Assert
            Assert.NotNull(order.Customer);
            Assert.Equal("ada@example.com", order.Customer!.Email);
            Assert.Equal("555-0101", order.Customer.Phone);
            Assert.Equal("1 Analytical Way", order.Customer.Address);
        }

        [SqlServerFact]
        public async Task GetAllAsync_OrdersByOrderDateDescending()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            await TestData.InsertOrderAsync(customerId, orderDate: new DateTime(2025, 1, 1));
            await TestData.InsertOrderAsync(customerId, orderDate: new DateTime(2025, 6, 1));
            await TestData.InsertOrderAsync(customerId, orderDate: new DateTime(2025, 3, 1));
            var sut = CreateSut();

            // Act
            var orders = (await sut.GetAllAsync()).ToList();

            // Assert
            Assert.Equal(
                new[] { new DateTime(2025, 6, 1), new DateTime(2025, 3, 1), new DateTime(2025, 1, 1) },
                orders.Select(o => o.OrderDate));
        }

        [SqlServerFact]
        public async Task GetAllAsync_WhenTableIsEmpty_ReturnsEmptySequenceNotNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var orders = await sut.GetAllAsync();

            // Assert
            Assert.NotNull(orders);
            Assert.Empty(orders);
        }

        [SqlServerFact]
        public async Task GetByCustomerIdAsync_ReturnsOnlyThatCustomersOrdersWithEmptyItems()
        {
            // Arrange - PINS SQL-6 for this path too.
            var mine = await TestData.InsertCustomerAsync();
            var theirs = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(mine);
            await TestData.InsertOrderItemAsync(orderId, productId);
            await TestData.InsertOrderAsync(theirs);
            var sut = CreateSut();

            // Act
            var orders = (await sut.GetByCustomerIdAsync(mine)).ToList();

            // Assert
            var order = Assert.Single(orders);
            Assert.Equal(mine, order.CustomerId);
            Assert.Empty(order.OrderItems);
        }

        [SqlServerFact]
        public async Task GetByCustomerIdAsync_WhenCustomerIsUnknown_ReturnsEmptySequenceNotNull()
        {
            // Arrange - no 404 path; an unknown customer looks exactly like one with no orders.
            var sut = CreateSut();

            // Act
            var orders = await sut.GetByCustomerIdAsync(999_999);

            // Assert
            Assert.NotNull(orders);
            Assert.Empty(orders);
        }

        [SqlServerFact]
        public async Task GetByStatus_MatchesTheStoredValueAndReturnsEmptyItems()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var shipped = await TestData.InsertOrderAsync(customerId, status: "Shipped");
            await TestData.InsertOrderItemAsync(shipped, productId);
            await TestData.InsertOrderAsync(customerId, status: "Pending");
            var sut = CreateSut();

            // Act
            var orders = sut.GetByStatus("Shipped").ToList();

            // Assert
            var order = Assert.Single(orders);
            Assert.Equal("Shipped", order.Status);
            Assert.Empty(order.OrderItems);
        }

        [SqlServerFact]
        public async Task GetByStatus_AcceptsAnArbitraryStatusStringThatNoOtherCodeProduces()
        {
            // Arrange - PINS BR-3. Status is free text end to end, so a value like this can be
            // stored by PUT and then queried back successfully.
            var customerId = await TestData.InsertCustomerAsync();
            await TestData.InsertOrderAsync(customerId, status: "banana");
            var sut = CreateSut();

            // Act
            var orders = sut.GetByStatus("banana").ToList();

            // Assert
            Assert.Single(orders);
        }

        [SqlServerTheory]
        [InlineData("NoSuchStatus")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetByStatus_WithUnknownOrEmptyStatus_ReturnsEmptySequenceNotNull(string status)
        {
            // Arrange - a typo returns an empty 200 that looks like a legitimate result.
            var customerId = await TestData.InsertCustomerAsync();
            await TestData.InsertOrderAsync(customerId, status: "Pending");
            var sut = CreateSut();

            // Act
            var orders = sut.GetByStatus(status);

            // Assert
            Assert.NotNull(orders);
            Assert.Empty(orders);
        }

        // ---------- Update ----------

        [SqlServerFact]
        public async Task Update_WritesOnlyTotalAmountStatusAndShippingAddress()
        {
            // Arrange - PINS SQL-5. CustomerId and OrderDate are absent from the SET list, so an
            // order cannot be reassigned or back-dated, and the attempt is silently ignored.
            var originalCustomer = await TestData.InsertCustomerAsync();
            var otherCustomer = await TestData.InsertCustomerAsync();
            var originalDate = new DateTime(2025, 6, 1, 12, 0, 0);
            var orderId = await TestData.InsertOrderAsync(
                originalCustomer, 100.00m, "Pending", "1 Old Road", originalDate);
            var sut = CreateSut();

            // Act
            sut.Update(new Order
            {
                OrderId = orderId,
                CustomerId = otherCustomer,
                OrderDate = new DateTime(1999, 1, 1),
                TotalAmount = 250.00m,
                Status = "Shipped",
                ShippingAddress = "2 New Road"
            });
            var reloaded = await sut.GetByIdAsync(orderId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Equal(250.00m, reloaded!.TotalAmount);
            Assert.Equal("Shipped", reloaded.Status);
            Assert.Equal("2 New Road", reloaded.ShippingAddress);
            Assert.Equal(originalCustomer, reloaded.CustomerId);
            Assert.Equal(originalDate, reloaded.OrderDate);
        }

        [SqlServerFact]
        public async Task Update_SilentlyIgnoresOrderItemsSuppliedInTheBody()
        {
            // Arrange - PINS SQL-5. A caller who edits line items receives 204 and changes nothing,
            // while TotalAmount is rewritten - leaving header and lines permanently inconsistent.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(customerId, 100.00m);
            await TestData.InsertOrderItemAsync(orderId, productId, 1, 100.00m);
            var sut = CreateSut();

            // Act
            sut.Update(new Order
            {
                OrderId = orderId,
                CustomerId = customerId,
                TotalAmount = 9_999.00m,
                Status = "Pending",
                OrderItems = new List<OrderItem> { Item(productId, 99, 1.00m) }
            });
            var reloaded = await sut.GetByIdAsync(orderId);

            // Assert - one unchanged line of 100.00, under a header now claiming 9,999.00.
            var item = Assert.Single(reloaded!.OrderItems);
            Assert.Equal(1, item.Quantity);
            Assert.Equal(100.00m, item.UnitPrice);
            Assert.Equal(9_999.00m, reloaded.TotalAmount);
        }

        [SqlServerFact]
        public async Task Update_WhenOrderDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2.
            var sut = CreateSut();
            var ghost = NewOrder(1);
            ghost.OrderId = 999_999;

            // Act
            var exception = Record.Exception(() => sut.Update(ghost));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
        }

        // ---------- Delete ----------

        [SqlServerFact]
        public async Task Delete_RemovesLineItemsAndTheHeaderTogether()
        {
            // Arrange - a manual cascade in foreign-key-safe order, inside one transaction. This is
            // the only place in the codebase that handles a dependent delete correctly.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId);
            var sut = CreateSut();

            // Act
            sut.Delete(orderId);

            // Assert
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM OrderItems"));
        }

        [SqlServerFact]
        public async Task Delete_SucceedsOnACompletedOrderWithNoStatusGuard()
        {
            // Arrange - PINS BR-6. A completed order and its financial history delete as easily as
            // a pending one.
            var customerId = await TestData.InsertCustomerAsync();
            var orderId = await TestData.InsertOrderAsync(customerId, status: "Completed");
            var sut = CreateSut();

            // Act
            sut.Delete(orderId);

            // Assert
            Assert.Null(await sut.GetByIdAsync(orderId));
        }

        [SqlServerFact]
        public async Task Delete_WhenOrderDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2.
            var sut = CreateSut();

            // Act
            var exception = Record.Exception(() => sut.Delete(999_999));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
        }

        [SqlServerFact]
        public async Task Delete_DoesNotRemoveTheCustomerOrTheProduct()
        {
            // Arrange
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId);
            var sut = CreateSut();

            // Act
            sut.Delete(orderId);

            // Assert
            Assert.Equal(1, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Customers"));
            Assert.Equal(1, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Products"));
        }
    }
}
