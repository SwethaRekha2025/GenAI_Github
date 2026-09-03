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
    /// Characterization tests for OrdersController. Tests marked PINS document a known defect
    /// from the audit and are expected to change when that finding is fixed.
    /// </summary>
    public class OrdersControllerTests
    {
        private readonly IOrderRepository _repository = Substitute.For<IOrderRepository>();
        private readonly RecordingLogger<OrdersController> _logger = new();
        private readonly OrdersController _sut;

        public OrdersControllerTests()
        {
            _sut = new OrdersController(_repository, _logger);
        }

        private static Order SampleOrder(int id = 1) => new()
        {
            OrderId = id,
            CustomerId = 3,
            OrderDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 1029.98m,
            Status = "Pending",
            ShippingAddress = "123 Main St",
            OrderItems = new List<OrderItem>
            {
                new() { OrderItemId = 1, OrderId = id, ProductId = 1, Quantity = 1, UnitPrice = 999.99m }
            }
        };

        // ---------- GetOrders ----------

        [Fact]
        public async Task GetOrders_WhenRepositoryReturnsOrders_ReturnsOkWithSameSequence()
        {
            // Arrange
            var orders = new List<Order> { SampleOrder(1), SampleOrder(2) };
            _repository.GetAllAsync().Returns(orders);

            // Act
            var result = await _sut.GetOrders();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(orders, ok.Value);
        }

        [Fact]
        public async Task GetOrders_WhenRepositoryReturnsEmpty_ReturnsOkWithEmptySequence()
        {
            // Arrange
            _repository.GetAllAsync().Returns(new List<Order>());

            // Act
            var result = await _sut.GetOrders();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Order>>(ok.Value));
        }

        [Fact]
        public async Task GetOrders_WhenRepositoryThrows_Returns500AndLogs()
        {
            // Arrange
            var boom = new Exception("database down");
            _repository.GetAllAsync().Throws(boom);

            // Act
            var result = await _sut.GetOrders();

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Equal("Internal server error", objectResult.Value);
            Assert.Same(boom, Assert.Single(_logger.Errors).Exception);
        }

        // ---------- GetOrder ----------

        [Fact]
        public async Task GetOrder_WhenOrderExists_ReturnsOkWithSameInstance()
        {
            // Arrange
            var order = SampleOrder(11);
            _repository.GetByIdAsync(11).Returns(order);

            // Act
            var result = await _sut.GetOrder(11);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(order, ok.Value);
        }

        [Fact]
        public async Task GetOrder_WhenRepositoryReturnsNull_ReturnsNotFound()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Returns((Order?)null);

            // Act
            var result = await _sut.GetOrder(999);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task GetOrder_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetOrder(1);

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }

        // ---------- PostOrder ----------

        [Fact]
        public void PostOrder_WhenRepositorySucceeds_ReturnsCreatedAtActionPointingAtGetOrder()
        {
            // Arrange
            var order = SampleOrder(0);
            var created = SampleOrder(88);
            _repository.Add(order).Returns(created);

            // Act
            var result = _sut.PostOrder(order);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(nameof(OrdersController.GetOrder), createdResult.ActionName);
            Assert.Equal(88, createdResult.RouteValues!["id"]);
            Assert.Same(created, createdResult.Value);
        }

        [Fact]
        public void PostOrder_StampsOrderDateWithUtcNowBeforeCallingRepository()
        {
            // Arrange - the single line of business logic in the HTTP layer. The tolerance window
            // exists only because DateTime.UtcNow is read directly; once TimeProvider is injected
            // (TST-4) this becomes an exact assertion.
            Order? captured = null;
            _repository.Add(Arg.Any<Order>()).Returns(ci =>
            {
                captured = ci.Arg<Order>();
                return captured;
            });

            var order = SampleOrder(0);
            order.OrderDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var before = DateTime.UtcNow.AddSeconds(-1);

            // Act
            _sut.PostOrder(order);
            var after = DateTime.UtcNow.AddSeconds(1);

            // Assert - the client-supplied OrderDate is discarded.
            Assert.NotNull(captured);
            Assert.InRange(captured!.OrderDate, before, after);
        }

        [Fact]
        public void PostOrder_WithEmptyOrderItems_IsAcceptedAndReachesTheRepository()
        {
            // Arrange - PINS BR-9. Nothing requires an order to have at least one line.
            var order = SampleOrder(0);
            order.OrderItems = new List<OrderItem>();
            _repository.Add(Arg.Any<Order>()).Returns(ci => ci.Arg<Order>());

            // Act
            var result = _sut.PostOrder(order);

            // Assert
            Assert.IsType<CreatedAtActionResult>(result.Result);
            _repository.Received(1).Add(Arg.Is<Order>(o => o.OrderItems.Count == 0));
        }

        [Fact]
        public void PostOrder_WithTamperedUnitPrice_PassesItStraightToTheRepository()
        {
            // Arrange - PINS BR-1. The controller never consults Products.Price, so a unit price
            // of one cent on a 999.99 product reaches persistence unchallenged.
            var order = SampleOrder(0);
            order.TotalAmount = 0.01m;
            order.OrderItems[0].UnitPrice = 0.01m;
            _repository.Add(Arg.Any<Order>()).Returns(ci => ci.Arg<Order>());

            // Act
            var result = _sut.PostOrder(order);

            // Assert
            Assert.IsType<CreatedAtActionResult>(result.Result);
            _repository.Received(1).Add(Arg.Is<Order>(o =>
                o.TotalAmount == 0.01m && o.OrderItems[0].UnitPrice == 0.01m));
        }

        [Fact]
        public void PostOrder_WithNullShippingAddress_StillReachesTheRepository()
        {
            // Arrange
            var order = SampleOrder(0);
            order.ShippingAddress = null;
            _repository.Add(Arg.Any<Order>()).Returns(ci => ci.Arg<Order>());

            // Act
            var result = _sut.PostOrder(order);

            // Assert
            Assert.IsType<CreatedAtActionResult>(result.Result);
            _repository.Received(1).Add(Arg.Is<Order>(o => o.ShippingAddress == null));
        }

        [Fact]
        public void PostOrder_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange - unreachable in production; [ApiController] returns 400 before the action runs.
            _sut.ModelState.AddModelError(nameof(Order.TotalAmount), "Total amount must be greater than 0");

            // Act
            var result = _sut.PostOrder(SampleOrder(0));

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
            _repository.DidNotReceive().Add(Arg.Any<Order>());
        }

        [Fact]
        public void PostOrder_WhenRepositoryThrows_Returns500()
        {
            // Arrange - an unknown CustomerId arrives here as SqlException 547 (BR-5).
            _repository.Add(Arg.Any<Order>()).Throws(new Exception("FK violation"));

            // Act
            var result = _sut.PostOrder(SampleOrder(0));

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Equal("Internal server error", objectResult.Value);
        }

        // ---------- PutOrder ----------

        [Fact]
        public void PutOrder_WhenIdsMatch_ReturnsNoContentAndCallsUpdate()
        {
            // Arrange
            var order = SampleOrder(5);

            // Act
            var result = _sut.PutOrder(5, order);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Update(order);
        }

        [Fact]
        public void PutOrder_WhenRouteIdDiffersFromBodyId_ReturnsBadRequestWithExactMessage()
        {
            // Act
            var result = _sut.PutOrder(6, SampleOrder(5));

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Order ID mismatch", badRequest.Value);
            _repository.DidNotReceive().Update(Arg.Any<Order>());
        }

        [Fact]
        public void PutOrder_WithArbitraryStatusString_IsAccepted()
        {
            // Arrange - PINS BR-3. Status is free text; no allowed-value set, no state machine.
            var order = SampleOrder(5);
            order.Status = "banana";

            // Act
            var result = _sut.PutOrder(5, order);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Update(Arg.Is<Order>(o => o.Status == "banana"));
        }

        [Fact]
        public void PutOrder_WhenOrderDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS SQL-2.

            // Act
            var result = _sut.PutOrder(999, SampleOrder(999));

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void PutOrder_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange
            _sut.ModelState.AddModelError(nameof(Order.Status), "required");

            // Act
            var result = _sut.PutOrder(5, SampleOrder(5));

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
            _repository.DidNotReceive().Update(Arg.Any<Order>());
        }

        [Fact]
        public void PutOrder_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.When(r => r.Update(Arg.Any<Order>())).Do(_ => throw new Exception("boom"));

            // Act
            var result = _sut.PutOrder(5, SampleOrder(5));

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        // ---------- DeleteOrder ----------

        [Fact]
        public void DeleteOrder_WhenRepositorySucceeds_ReturnsNoContent()
        {
            // Act
            var result = _sut.DeleteOrder(3);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Delete(3);
        }

        [Fact]
        public void DeleteOrder_WhenOrderDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS ERR-4.

            // Act
            var result = _sut.DeleteOrder(999);

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void DeleteOrder_WhenRepositoryThrows_Returns500AndLogs()
        {
            // Arrange
            _repository.When(r => r.Delete(Arg.Any<int>())).Do(_ => throw new Exception("boom"));

            // Act
            var result = _sut.DeleteOrder(1);

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Single(_logger.Errors);
        }

        // ---------- GetOrdersByCustomer ----------

        [Fact]
        public async Task GetOrdersByCustomer_WhenOrdersExist_ReturnsOk()
        {
            // Arrange
            var orders = new List<Order> { SampleOrder(1) };
            _repository.GetByCustomerIdAsync(3).Returns(orders);

            // Act
            var result = await _sut.GetOrdersByCustomer(3);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(orders, ok.Value);
        }

        [Fact]
        public async Task GetOrdersByCustomer_WhenCustomerIsUnknown_ReturnsOkWithEmptyListNotNotFound()
        {
            // Arrange - an unknown customer is indistinguishable from one with no orders.
            _repository.GetByCustomerIdAsync(Arg.Any<int>()).Returns(new List<Order>());

            // Act
            var result = await _sut.GetOrdersByCustomer(99999);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Order>>(ok.Value));
        }

        [Fact]
        public async Task GetOrdersByCustomer_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByCustomerIdAsync(Arg.Any<int>()).Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetOrdersByCustomer(3);

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }

        // ---------- GetOrdersByStatus ----------

        [Fact]
        public void GetOrdersByStatus_WhenOrdersExist_ReturnsOk()
        {
            // Arrange
            var orders = new List<Order> { SampleOrder(1) };
            _repository.GetByStatus("Pending").Returns(orders);

            // Act
            var result = _sut.GetOrdersByStatus("Pending");

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(orders, ok.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("NotAStatus")]
        public void GetOrdersByStatus_WithEmptyOrUnknownStatus_ReturnsOkWithEmptyList(string status)
        {
            // Arrange - PINS BR-3. No validation against a known status set, so a typo returns an
            // empty 200 that looks exactly like a legitimately empty result.
            _repository.GetByStatus(Arg.Any<string>()).Returns(new List<Order>());

            // Act
            var result = _sut.GetOrdersByStatus(status);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Order>>(ok.Value));
            _repository.Received(1).GetByStatus(status);
        }

        [Fact]
        public void GetOrdersByStatus_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByStatus(Arg.Any<string>()).Throws(new Exception("boom"));

            // Act
            var result = _sut.GetOrdersByStatus("Pending");

            // Assert
            Assert.Equal(500, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        }
    }
}
