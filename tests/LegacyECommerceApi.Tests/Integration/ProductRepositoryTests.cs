using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;
using LegacyECommerceApi.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyECommerceApi.Tests.Integration
{
    /// <summary>
    /// Characterization tests for ProductRepository against a real SQL Server.
    /// Skipped with a reason when no server is reachable.
    /// </summary>
    public class ProductRepositoryTests : DatabaseTestBase
    {
        private static ProductRepository CreateSut() =>
            new(TestDatabase.Configuration(), NullLogger<ProductRepository>.Instance);

        private static Product NewProduct(string name = "Widget") => new()
        {
            Name = name,
            Description = "A widget",
            Price = 19.99m,
            StockQuantity = 25,
            Category = "Widgets",
            IsActive = true
        };

        // ---------- GetByIdAsync / GetAllAsync ----------

        [SqlServerFact]
        public async Task GetByIdAsync_WhenProductExists_MapsEveryColumn()
        {
            // Arrange
            var created = new DateTime(2025, 2, 2, 8, 0, 0, DateTimeKind.Utc);
            var id = await TestData.InsertProductAsync(
                "Laptop", 999.99m, 50, "Electronics", true, "A laptop", created);
            var sut = CreateSut();

            // Act
            var product = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(product);
            Assert.Equal("Laptop", product!.Name);
            Assert.Equal("A laptop", product.Description);
            Assert.Equal(999.99m, product.Price);
            Assert.Equal(50, product.StockQuantity);
            Assert.Equal("Electronics", product.Category);
            Assert.Equal(created, product.CreatedDate);
            Assert.True(product.IsActive);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenDescriptionAndCategoryAreNull_MapsThemAsNull()
        {
            // Arrange
            var id = await TestData.InsertProductAsync(description: null, category: null);
            var sut = CreateSut();

            // Act
            var product = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(product);
            Assert.Null(product!.Description);
            Assert.Null(product.Category);
        }

        [SqlServerFact]
        public async Task GetByIdAsync_WhenProductDoesNotExist_ReturnsNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act + Assert
            Assert.Null(await sut.GetByIdAsync(999_999));
        }

        [SqlServerFact]
        public async Task GetByIdAsync_ReturnsInactiveProductsIdenticallyToActiveOnes()
        {
            // Arrange - PINS divergence. No IsActive filter here, unlike GetByCategory.
            var id = await TestData.InsertProductAsync("Retired", isActive: false);
            var sut = CreateSut();

            // Act
            var product = await sut.GetByIdAsync(id);

            // Assert
            Assert.NotNull(product);
            Assert.False(product!.IsActive);
        }

        [SqlServerFact]
        public async Task GetAllAsync_IncludesInactiveProducts()
        {
            // Arrange - PINS divergence. Two list endpoints over one table disagree about what a
            // visible product is: this one shows retired rows, GetByCategory hides them.
            await TestData.InsertProductAsync("Active Widget", isActive: true);
            await TestData.InsertProductAsync("Retired Widget", isActive: false);
            var sut = CreateSut();

            // Act
            var products = (await sut.GetAllAsync()).ToList();

            // Assert
            Assert.Equal(2, products.Count);
            Assert.Contains(products, p => !p.IsActive);
        }

        [SqlServerFact]
        public async Task GetAllAsync_OrdersByName()
        {
            // Arrange
            await TestData.InsertProductAsync("Zebra");
            await TestData.InsertProductAsync("Apple");
            await TestData.InsertProductAsync("Mango");
            var sut = CreateSut();

            // Act
            var products = (await sut.GetAllAsync()).ToList();

            // Assert
            Assert.Equal(new[] { "Apple", "Mango", "Zebra" }, products.Select(p => p.Name));
        }

        [SqlServerFact]
        public async Task GetAllAsync_WhenTableIsEmpty_ReturnsEmptySequenceNotNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var products = await sut.GetAllAsync();

            // Assert
            Assert.NotNull(products);
            Assert.Empty(products);
        }

        // ---------- Add ----------

        [SqlServerFact]
        public void Add_ReturnsTheSameInstanceMutatedWithTheGeneratedId()
        {
            // Arrange
            var sut = CreateSut();
            var product = NewProduct();

            // Act
            var returned = sut.Add(product);

            // Assert
            Assert.Same(product, returned);
            Assert.True(returned.ProductId > 0);
        }

        [SqlServerFact]
        public void Add_OverwritesAnyClientSuppliedCreatedDateWithUtcNow()
        {
            // Arrange
            var sut = CreateSut();
            var product = NewProduct();
            product.CreatedDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var before = DateTime.UtcNow.AddSeconds(-5);

            // Act
            var returned = sut.Add(product);

            // Assert
            Assert.InRange(returned.CreatedDate, before, DateTime.UtcNow.AddSeconds(5));
        }

        [SqlServerFact]
        public async Task Add_HonoursClientSuppliedIsActiveFalse()
        {
            // Arrange - IsActive is not server-controlled; a product can be created already retired.
            var sut = CreateSut();
            var product = NewProduct();
            product.IsActive = false;

            // Act
            var returned = sut.Add(product);
            var reloaded = await sut.GetByIdAsync(returned.ProductId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.False(reloaded!.IsActive);
        }

        [SqlServerFact]
        public async Task Add_WhenPriceHasMoreThanTwoDecimalPlaces_ItIsSilentlyReducedToTwo()
        {
            // Arrange - PINS SQL-4. The column is decimal(18,2) and AddWithValue infers the
            // parameter type from the CLR value, so extra scale is lost without any error.
            // The rounding direction is deliberately not asserted: pin it once observed on your
            // server, since that is the behaviour the SqlDbType fix must preserve.
            var sut = CreateSut();
            var product = NewProduct();
            product.Price = 12.345m;

            // Act
            var returned = sut.Add(product);
            var reloaded = await sut.GetByIdAsync(returned.ProductId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.NotEqual(12.345m, reloaded!.Price);
            Assert.Equal(decimal.Round(reloaded.Price, 2), reloaded.Price);
        }

        [SqlServerFact]
        public async Task Add_WithNullDescriptionAndCategory_PersistsThemAsNull()
        {
            // Arrange
            var sut = CreateSut();
            var product = NewProduct();
            product.Description = null;
            product.Category = null;

            // Act
            var returned = sut.Add(product);
            var reloaded = await sut.GetByIdAsync(returned.ProductId);

            // Assert
            Assert.NotNull(reloaded);
            Assert.Null(reloaded!.Description);
            Assert.Null(reloaded.Category);
        }

        // ---------- Update ----------

        [SqlServerFact]
        public async Task Update_WritesStockQuantity()
        {
            // Arrange - this is the only code path in the whole application that changes stock.
            // Ordering never touches it, which is what makes BR-2 possible.
            var id = await TestData.InsertProductAsync(stockQuantity: 50);
            var sut = CreateSut();
            var product = await sut.GetByIdAsync(id);
            product!.StockQuantity = 7;

            // Act
            sut.Update(product);
            var reloaded = await sut.GetByIdAsync(id);

            // Assert
            Assert.Equal(7, reloaded!.StockQuantity);
        }

        [SqlServerFact]
        public async Task Update_WhenProductDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2.
            var sut = CreateSut();
            var ghost = NewProduct();
            ghost.ProductId = 999_999;

            // Act
            var exception = Record.Exception(() => sut.Update(ghost));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Products"));
        }

        [SqlServerFact]
        public async Task Update_DoesNotModifyCreatedDate()
        {
            // Arrange
            var created = new DateTime(2025, 2, 2, 8, 0, 0, DateTimeKind.Utc);
            var id = await TestData.InsertProductAsync(createdDate: created);
            var sut = CreateSut();
            var product = await sut.GetByIdAsync(id);
            product!.Name = "Renamed";

            // Act
            sut.Update(product);
            var reloaded = await sut.GetByIdAsync(id);

            // Assert
            Assert.Equal(created, reloaded!.CreatedDate);
        }

        [SqlServerFact]
        public async Task Update_ChangingPriceDoesNotRewriteHistoricalOrderItemPrices()
        {
            // Arrange - correct behaviour worth protecting: an order's recorded unit price is a
            // historical fact, not a live lookup.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync(price: 100.00m);
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId, 1, 100.00m);

            var sut = CreateSut();
            var product = await sut.GetByIdAsync(productId);
            product!.Price = 250.00m;

            // Act
            sut.Update(product);

            // Assert
            var storedUnitPrice = await TestDatabase.ScalarAsync<decimal>(
                "SELECT UnitPrice FROM OrderItems WHERE OrderId = @OrderId", ("@OrderId", orderId));
            Assert.Equal(100.00m, storedUnitPrice);
        }

        // ---------- Delete ----------

        [SqlServerFact]
        public async Task Delete_WhenProductIsUnreferenced_RemovesTheRow()
        {
            // Arrange
            var id = await TestData.InsertProductAsync();
            var sut = CreateSut();

            // Act
            sut.Delete(id);

            // Assert
            Assert.Null(await sut.GetByIdAsync(id));
        }

        [SqlServerFact]
        public async Task Delete_WhenProductAppearsOnAnOrder_ThrowsForeignKeyViolation()
        {
            // Arrange - PINS BR-6. A hard delete despite IsActive existing for exactly this purpose.
            var customerId = await TestData.InsertCustomerAsync();
            var productId = await TestData.InsertProductAsync();
            var orderId = await TestData.InsertOrderAsync(customerId);
            await TestData.InsertOrderItemAsync(orderId, productId);
            var sut = CreateSut();

            // Act + Assert
            var ex = Assert.Throws<SqlException>(() => sut.Delete(productId));
            Assert.Equal(547, ex.Number);
        }

        [SqlServerFact]
        public async Task Delete_WhenProductDoesNotExist_CompletesSilently()
        {
            // Arrange - PINS SQL-2.
            var sut = CreateSut();

            // Act
            var exception = Record.Exception(() => sut.Delete(999_999));

            // Assert
            Assert.Null(exception);
            Assert.Equal(0, await TestDatabase.ScalarAsync<int>("SELECT COUNT(*) FROM Products"));
        }

        // ---------- GetByCategory ----------

        [SqlServerFact]
        public async Task GetByCategory_SilentlyExcludesInactiveProducts()
        {
            // Arrange - PINS hidden rule. The IsActive = 1 filter appears nowhere in the route or
            // the method name. It is the behaviour most likely to be "tidied away" in a rewrite.
            await TestData.InsertProductAsync("Live", category: "Tools", isActive: true);
            await TestData.InsertProductAsync("Retired", category: "Tools", isActive: false);
            var sut = CreateSut();

            // Act
            var products = sut.GetByCategory("Tools").ToList();

            // Assert
            Assert.Equal("Live", Assert.Single(products).Name);
        }

        [SqlServerFact]
        public async Task GetByCategory_OrdersByName()
        {
            // Arrange
            await TestData.InsertProductAsync("Zebra", category: "Animals");
            await TestData.InsertProductAsync("Aardvark", category: "Animals");
            var sut = CreateSut();

            // Act
            var products = sut.GetByCategory("Animals").ToList();

            // Assert
            Assert.Equal(new[] { "Aardvark", "Zebra" }, products.Select(p => p.Name));
        }

        [SqlServerTheory]
        [InlineData("NoSuchCategory")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetByCategory_WithUnknownOrEmptyCategory_ReturnsEmptySequenceNotNull(string category)
        {
            // Arrange - no 404 path exists, so an unknown category is indistinguishable from a
            // real but empty one.
            await TestData.InsertProductAsync(category: "Tools");
            var sut = CreateSut();

            // Act
            var products = sut.GetByCategory(category);

            // Assert
            Assert.NotNull(products);
            Assert.Empty(products);
        }

        // ---------- GetActiveProductsAsync ----------

        [SqlServerFact]
        public async Task GetActiveProductsAsync_ExcludesActiveProductWithZeroStock()
        {
            // Arrange - "active" is defined here, and only here, as IsActive AND in stock.
            await TestData.InsertProductAsync("In Stock", stockQuantity: 5, isActive: true);
            await TestData.InsertProductAsync("Out Of Stock", stockQuantity: 0, isActive: true);
            var sut = CreateSut();

            // Act
            var products = (await sut.GetActiveProductsAsync()).ToList();

            // Assert
            Assert.Equal("In Stock", Assert.Single(products).Name);
        }

        [SqlServerFact]
        public async Task GetActiveProductsAsync_ExcludesInactiveProductThatHasStock()
        {
            // Arrange
            await TestData.InsertProductAsync("Live", stockQuantity: 5, isActive: true);
            await TestData.InsertProductAsync("Retired", stockQuantity: 5, isActive: false);
            var sut = CreateSut();

            // Act
            var products = (await sut.GetActiveProductsAsync()).ToList();

            // Assert
            Assert.Equal("Live", Assert.Single(products).Name);
        }

        [SqlServerFact]
        public async Task GetActiveProductsAsync_WhenNothingQualifies_ReturnsEmptySequenceNotNull()
        {
            // Arrange
            await TestData.InsertProductAsync(stockQuantity: 0);
            var sut = CreateSut();

            // Act
            var products = await sut.GetActiveProductsAsync();

            // Assert
            Assert.NotNull(products);
            Assert.Empty(products);
        }
    }
}
