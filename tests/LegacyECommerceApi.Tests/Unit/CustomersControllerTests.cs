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
    /// Characterization tests for CustomersController. These record what the controller does today,
    /// including where that is wrong. Tests marked PINS document a known defect from the audit and
    /// are expected to change when that finding is fixed.
    /// </summary>
    public class CustomersControllerTests
    {
        private readonly ICustomerRepository _repository = Substitute.For<ICustomerRepository>();
        private readonly RecordingLogger<CustomersController> _logger = new();
        private readonly CustomersController _sut;

        public CustomersControllerTests()
        {
            _sut = new CustomersController(_repository, _logger);
        }

        private static Customer SampleCustomer(int id = 1) => new()
        {
            CustomerId = id,
            FirstName = "John",
            LastName = "Smith",
            Email = "john.smith@email.com",
            Phone = "555-0101",
            Address = "123 Main St",
            CreatedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // ---------- GetCustomers ----------

        [Fact]
        public async Task GetCustomers_WhenRepositoryReturnsCustomers_ReturnsOkWithSameSequence()
        {
            // Arrange
            var customers = new List<Customer> { SampleCustomer(1), SampleCustomer(2) };
            _repository.GetAllAsync().Returns(customers);

            // Act
            var result = await _sut.GetCustomers();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(customers, ok.Value);
        }

        [Fact]
        public async Task GetCustomers_WhenRepositoryReturnsEmpty_ReturnsOkWithEmptySequence()
        {
            // Arrange
            _repository.GetAllAsync().Returns(new List<Customer>());

            // Act
            var result = await _sut.GetCustomers();

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Customer>>(ok.Value));
        }

        [Fact]
        public async Task GetCustomers_WhenRepositoryThrows_Returns500WithPlainStringBody()
        {
            // Arrange
            _repository.GetAllAsync().Throws(new InvalidOperationException("database down"));

            // Act
            var result = await _sut.GetCustomers();

            // Assert - the body is a bare string today; Phase 3 replaces it with ProblemDetails,
            // and this assertion is what makes that a visible, deliberate change.
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Equal("Internal server error", objectResult.Value);
        }

        [Fact]
        public async Task GetCustomers_WhenRepositoryThrows_LogsErrorOnceWithTheException()
        {
            // Arrange
            var boom = new InvalidOperationException("database down");
            _repository.GetAllAsync().Throws(boom);

            // Act
            await _sut.GetCustomers();

            // Assert - the call and the attached exception are behaviour; the wording is not.
            var error = Assert.Single(_logger.Errors);
            Assert.Same(boom, error.Exception);
        }

        // ---------- GetCustomer ----------

        [Fact]
        public async Task GetCustomer_WhenCustomerExists_ReturnsOkWithSameInstance()
        {
            // Arrange
            var customer = SampleCustomer(7);
            _repository.GetByIdAsync(7).Returns(customer);

            // Act
            var result = await _sut.GetCustomer(7);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(customer, ok.Value);
        }

        [Fact]
        public async Task GetCustomer_WhenRepositoryReturnsNull_ReturnsNotFound()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Returns((Customer?)null);

            // Act
            var result = await _sut.GetCustomer(999);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public async Task GetCustomer_WithNonPositiveId_PassesItThroughUnchanged(int id)
        {
            // Arrange - there is no id validation; the value reaches the repository as supplied.
            _repository.GetByIdAsync(Arg.Any<int>()).Returns((Customer?)null);

            // Act
            var result = await _sut.GetCustomer(id);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
            await _repository.Received(1).GetByIdAsync(id);
        }

        [Fact]
        public async Task GetCustomer_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByIdAsync(Arg.Any<int>()).Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetCustomer(1);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
        }

        // ---------- PostCustomer ----------

        [Fact]
        public void PostCustomer_WhenRepositorySucceeds_ReturnsCreatedAtActionPointingAtGetCustomer()
        {
            // Arrange
            var customer = SampleCustomer(0);
            var created = SampleCustomer(42);
            _repository.Add(customer).Returns(created);

            // Act
            var result = _sut.PostCustomer(customer);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(nameof(CustomersController.GetCustomer), createdResult.ActionName);
            Assert.Equal(42, createdResult.RouteValues!["id"]);
            Assert.Same(created, createdResult.Value);
        }

        [Fact]
        public void PostCustomer_WithNullPhoneAndAddress_StillReachesTheRepository()
        {
            // Arrange - Phone and Address are the two optional columns.
            var customer = SampleCustomer(0);
            customer.Phone = null;
            customer.Address = null;
            _repository.Add(Arg.Any<Customer>()).Returns(ci => ci.Arg<Customer>());

            // Act
            var result = _sut.PostCustomer(customer);

            // Assert
            Assert.IsType<CreatedAtActionResult>(result.Result);
            _repository.Received(1).Add(Arg.Is<Customer>(c => c.Phone == null && c.Address == null));
        }

        [Fact]
        public void PostCustomer_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange - this branch is unreachable in production: [ApiController] short-circuits with
            // a ValidationProblemDetails 400 before the action body runs, and Program.cs never sets
            // SuppressModelStateInvalidFilter. The test documents the dead branch's own return value.
            _sut.ModelState.AddModelError(nameof(Customer.Email), "The Email field is required.");

            // Act
            var result = _sut.PostCustomer(SampleCustomer(0));

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
            _repository.DidNotReceive().Add(Arg.Any<Customer>());
        }

        [Fact]
        public void PostCustomer_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.Add(Arg.Any<Customer>()).Throws(new Exception("unique constraint"));

            // Act
            var result = _sut.PostCustomer(SampleCustomer(0));

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Equal("Internal server error", objectResult.Value);
        }

        // ---------- PutCustomer ----------

        [Fact]
        public void PutCustomer_WhenIdsMatch_ReturnsNoContentAndCallsUpdate()
        {
            // Arrange
            var customer = SampleCustomer(5);

            // Act
            var result = _sut.PutCustomer(5, customer);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Update(customer);
        }

        [Fact]
        public void PutCustomer_WhenRouteIdDiffersFromBodyId_ReturnsBadRequestWithExactMessage()
        {
            // Arrange
            var customer = SampleCustomer(5);

            // Act
            var result = _sut.PutCustomer(6, customer);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Customer ID mismatch", badRequest.Value);
            _repository.DidNotReceive().Update(Arg.Any<Customer>());
        }

        [Fact]
        public void PutCustomer_WhenCustomerDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS SQL-2. Update is void and the affected-row count is discarded, so the
            // controller cannot tell "updated" from "matched nothing". This must become 404.
            var customer = SampleCustomer(999);

            // Act
            var result = _sut.PutCustomer(999, customer);

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void PutCustomer_WhenModelStateIsInvalid_ReturnsBadRequestAndNeverCallsRepository()
        {
            // Arrange
            var customer = SampleCustomer(5);
            _sut.ModelState.AddModelError(nameof(Customer.FirstName), "required");

            // Act
            var result = _sut.PutCustomer(5, customer);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
            _repository.DidNotReceive().Update(Arg.Any<Customer>());
        }

        [Fact]
        public void PutCustomer_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.When(r => r.Update(Arg.Any<Customer>())).Do(_ => throw new Exception("boom"));

            // Act
            var result = _sut.PutCustomer(5, SampleCustomer(5));

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);
        }

        // ---------- DeleteCustomer ----------

        [Fact]
        public void DeleteCustomer_WhenRepositorySucceeds_ReturnsNoContent()
        {
            // Act
            var result = _sut.DeleteCustomer(3);

            // Assert
            Assert.IsType<NoContentResult>(result);
            _repository.Received(1).Delete(3);
        }

        [Fact]
        public void DeleteCustomer_WhenCustomerDoesNotExist_StillReturnsNoContent()
        {
            // Arrange - PINS ERR-4. The repository is a void mock that does nothing, exactly as the
            // real one does for an unmatched id, and the controller still reports success.

            // Act
            var result = _sut.DeleteCustomer(999);

            // Assert
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void DeleteCustomer_WhenRepositoryThrows_Returns500()
        {
            // Arrange - a real foreign-key violation arrives here as SqlException.
            _repository.When(r => r.Delete(Arg.Any<int>())).Do(_ => throw new Exception("FK violation"));

            // Act
            var result = _sut.DeleteCustomer(1);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);
            Assert.Single(_logger.Errors);
        }

        // ---------- GetCustomerByEmail ----------

        [Fact]
        public async Task GetCustomerByEmail_WhenEmailExists_ReturnsOk()
        {
            // Arrange
            var customer = SampleCustomer(1);
            _repository.GetByEmailAsync("john.smith@email.com").Returns(customer);

            // Act
            var result = await _sut.GetCustomerByEmail("john.smith@email.com");

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(customer, ok.Value);
        }

        [Fact]
        public async Task GetCustomerByEmail_WhenEmailIsUnknown_ReturnsNotFound()
        {
            // Arrange - the 200/404 split is what makes this endpoint an existence oracle (SEC-2).
            _repository.GetByEmailAsync(Arg.Any<string>()).Returns((Customer?)null);

            // Act
            var result = await _sut.GetCustomerByEmail("nobody@example.com");

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-email")]
        public async Task GetCustomerByEmail_WithEmptyOrMalformedInput_PassesItThroughUnvalidated(string email)
        {
            // Arrange - there is no format validation on this route parameter.
            _repository.GetByEmailAsync(Arg.Any<string>()).Returns((Customer?)null);

            // Act
            var result = await _sut.GetCustomerByEmail(email);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
            await _repository.Received(1).GetByEmailAsync(email);
        }

        [Fact]
        public async Task GetCustomerByEmail_WhenRepositoryThrows_Returns500()
        {
            // Arrange
            _repository.GetByEmailAsync(Arg.Any<string>()).Throws(new Exception("boom"));

            // Act
            var result = await _sut.GetCustomerByEmail("john.smith@email.com");

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, objectResult.StatusCode);
        }
    }
}
