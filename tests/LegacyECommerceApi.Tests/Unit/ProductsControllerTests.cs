using LegacyECommerceApi.Controllers;
using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;
using LegacyECommerceApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace LegacyECommerceApi.Tests.Unit
{
    /// <summary>
    /// Characterization tests for ProductsController. Tests marked PINS document a known defect
    /// from the audit and are expected to change when that finding is fixed.
    /// </summary>
    public class ProductsControllerTests
    {
        private readonly IProductRepository _repository = Substitute.For<IProductRepository>();
        private readonly RecordingLogger<ProductsController> _logger = new();
        private readonly ProductsController _sut;

        public ProductsControllerTests()
        {
            _sut = new ProductsController(_repository, _logger);
        }

        private static Product SampleProduct(int id = 1, bool isActive = true) => new()
        {
            ProductId = id,
            Name = "Laptop Computer",
            Description = "High-performance laptop",
            Price = 999.99m,
            StockQuantity = 50,
            Category = "Electronics",
            IsActive = isActive,
            CreatedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // ---------- GetProducts ----------

        [Fact]
        public async Task GetProducts_WhenRepositoryReturnsProducts_ReturnsOkWithSameSequence()
        {
            // Arrange
            var products = new List<Product> { SampleProduct(1), SampleProduct(2) };
            _repository.GetAllAsync().Returns(products);

            // Act
            var result = await _sut.GetProducts();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(products, ok.Value);
        }

        [Fact]
        public async Task GetProducts_WhenRepositoryReturnsEmpty_ReturnsOkWithEmptySequence()
        {
            // Arrange
            _repository.GetAllAsync().Returns(new List<Product>());

            // Act
            var result = await _sut.GetProducts();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Product>>(ok.Value));
        }

        [Fact]
        public async Task GetProducts_WhenRepositoryThrows_Returns500AndLogs()
        {
            // Arrange
            var boom = new Exception("database down");
            _repository.GetAllAsync().Throws(boom);

            // Act
            var result = await _sut.GetProducts();

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Equal("Internal server error", objectResult.Value);
            Assert.Same(boom, Assert.Single(_logger.Errors).Exception);
        }

        // ---------- GetProduct ----------

        [Fact]
        public async Task GetProduct_WhenProductExists_ReturnsOkWithSameInstance()
        {
            // Arrange
            var product = SampleProduct(9);
            _repository.GetByIdAsync(9).Returns(product);

            // Act
            var result = await _sut.GetProduct(9);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(product, ok.Value);
        }

        [Fact]
        public async Task GetProduct_WhenRepositoryReturnsNull_ReturnsNotFound()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Returns((Product?)null);

            // Act
            var result = await _sut.GetProduct(999);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task GetProduct_WhenProductIsInactive_StillReturnsIt()
        {
            // Arrange - PINS divergence. GetByIdAsync applies no IsActive filter, so a retired
            // product is served exactly like a live one. GetByCategory hides the same row.
            var retired = SampleProduct(4, isActive: false);
            _repository.GetByIdAsync(4).Returns(retired);

            // Act
            var result = await _sut.GetProduct(4);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.False(Assert.IsType<Product>(ok.Value).IsActive);
        }

        [Fact]
        public async Task GetProduct_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetProduct(1);

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }

        // ---------- PostProduct ----------

        [Fact]
        public void PostProduct_WhenRepositorySucceeds_ReturnsCreatedAtActionPointingAtGetProduct()
        {
            // Arrange
            var product = SampleProduct(0);
            var created = SampleProduct(77);
            _repository.Add(product).Returns(created);

            // Act
            var result = _sut.PostProduct(product);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(nameof(ProductsController.GetProduct), createdResult.ActionName);
            Assert.Equal(77, createdResult.RouteValues!["id"]);
            Assert.Same(created, createdResult.Value);
        }

        [Fact]
        public void PostProduct_WithNullDescriptionAndCategory_StillReachesTheRepository()
        {
            // Arrange - Description and Category are the two optional columns.
            var product = SampleProduct(0);
            product.Description = null;
            product.Category = null;
            _repository.Add(Arg.Any<Product>()).Returns(ci => ci.Arg<Product>());

            // Act
            var result = _sut.PostProduct(product);

            // Assert
            Assert.IsType<CreatedAtActionResult>(result.Result);
            _repository.Received(1).Add(Arg.Is<Product>(p => p.Description == null && p.Category == null));
        }

        [Fact]
        public void PostProduct_WhenClientSendsIsActiveFalse_ItIsPassedThrough()
        {
            // Arrange - IsActive is client-controlled on create; nothing overrides it.
            var product = SampleProduct(0, isActive: false);
            _repository.Add(Arg.Any<Product>()).Returns(ci => ci.Arg<Product>());

            // Act
            _sut.PostProduct(product);

            // Assert
            _repository.Received(1).Add(Arg.Is<Product>(p => p.IsActive == false));
        }

        [Fact]
        public void PostProduct_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange - unreachable in production; [ApiController] returns 400 before the action runs.
            _sut.ModelState.AddModelError(nameof(Product.Price), "Price must be greater than 0");

            // Act
            var result = _sut.PostProduct(SampleProduct(0));

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
            _repository.DidNotReceive().Add(Arg.Any<Product>());
        }

        [Fact]
        public void PostProduct_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.Add(Arg.Any<Product>()).Throws(new Exception("boom"));

            // Act
            var result = _sut.PostProduct(SampleProduct(0));

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }

        // ---------- PutProduct ----------

        [Fact]
        public void PutProduct_WhenIdsMatch_ReturnsNoContentAndCallsUpdate()
        {
            // Arrange
            var product = SampleProduct(5);

            // Act
            var result = _sut.PutProduct(5, product);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Update(product);
        }

        [Fact]
        public void PutProduct_WhenRouteIdDiffersFromBodyId_ReturnsBadRequestWithExactMessage()
        {
            // Act
            var result = _sut.PutProduct(6, SampleProduct(5));

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Product ID mismatch", badRequest.Value);
            _repository.DidNotReceive().Update(Arg.Any<Product>());
        }

        [Fact]
        public void PutProduct_WhenProductDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS SQL-2. Update is void; the affected-row count is discarded.

            // Act
            var result = _sut.PutProduct(999, SampleProduct(999));

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void PutProduct_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange
            _sut.ModelState.AddModelError(nameof(Product.Name), "required");

            // Act
            var result = _sut.PutProduct(5, SampleProduct(5));

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
            _repository.DidNotReceive().Update(Arg.Any<Product>());
        }

        [Fact]
        public void PutProduct_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.When(r => r.Update(Arg.Any<Product>())).Do(_ => throw new Exception("boom"));

            // Act
            var result = _sut.PutProduct(5, SampleProduct(5));

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        // ---------- DeleteProduct ----------

        [Fact]
        public void DeleteProduct_WhenRepositorySucceeds_ReturnsNoContent()
        {
            // Act
            var result = _sut.DeleteProduct(3);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Delete(3);
        }

        [Fact]
        public void DeleteProduct_WhenProductDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS ERR-4. A void repository call that matched nothing reads as success.

            // Act
            var result = _sut.DeleteProduct(999);

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void DeleteProduct_WhenRepositoryThrows_Returns500AndLogs()
        {
            // Arrange - a real product that appears on an order raises SqlException 547 here.
            _repository.When(r => r.Delete(Arg.Any<int>())).Do(_ => throw new Exception("FK violation"));

            // Act
            var result = _sut.DeleteProduct(1);

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Single(_logger.Errors);
        }

        // ---------- GetProductsByCategory ----------

        [Fact]
        public void GetProductsByCategory_WhenProductsExist_ReturnsOk()
        {
            // Arrange
            var products = new List<Product> { SampleProduct(1) };
            _repository.GetByCategory("Electronics").Returns(products);

            // Act
            var result = _sut.GetProductsByCategory("Electronics");

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(products, ok.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("NoSuchCategory")]
        public void GetProductsByCategory_WithEmptyOrUnknownCategory_ReturnsOkWithEmptyList(string category)
        {
            // Arrange - no validation and no 404; an unknown category is indistinguishable from
            // a real but empty one.
            _repository.GetByCategory(Arg.Any<string>()).Returns(new List<Product>());

            // Act
            var result = _sut.GetProductsByCategory(category);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Product>>(ok.Value));
            _repository.Received(1).GetByCategory(category);
        }

        [Fact]
        public void GetProductsByCategory_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByCategory(Arg.Any<string>()).Throws(new Exception("boom"));

            // Act
            var result = _sut.GetProductsByCategory("Electronics");

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }

        // ---------- GetActiveProducts ----------

        [Fact]
        public async Task GetActiveProducts_WhenProductsExist_ReturnsOk()
        {
            // Arrange
            var products = new List<Product> { SampleProduct(1) };
            _repository.GetActiveProductsAsync().Returns(products);

            // Act
            var result = await _sut.GetActiveProducts();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(products, ok.Value);
        }

        [Fact]
        public async Task GetActiveProducts_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetActiveProductsAsync().Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetActiveProducts();

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }
    }
}
