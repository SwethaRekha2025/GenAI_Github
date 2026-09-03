using Microsoft.AspNetCore.Mvc;
using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;

namespace LegacyECommerceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomersController : LegacyApiController
    {
        private readonly ICustomerRepository _customerRepository;

        public CustomersController(ICustomerRepository customerRepository, ILogger<CustomersController> logger)
            : base(logger)
        {
            _customerRepository = customerRepository;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
        {
            try
            {
                var customers = await _customerRepository.GetAllAsync();
                return Ok(customers);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving customers");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Customer>> GetCustomer(int id)
        {
            try
            {
                var customer = await _customerRepository.GetByIdAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return Ok(customer);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving customer {CustomerId}", id);
            }
        }

        [HttpPost]
        public ActionResult<Customer> PostCustomer(Customer customer)
        {
            // Unreachable in production: [ApiController] answers 400 with ValidationProblemDetails
            // before the action body runs. Kept as the safety net if that attribute is ever removed.
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var createdCustomer = _customerRepository.Add(customer);
                return CreatedAtAction(nameof(GetCustomer), new { id = createdCustomer.CustomerId }, createdCustomer);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error creating customer");
            }
        }

        [HttpPut("{id}")]
        public IActionResult PutCustomer(int id, Customer customer)
        {
            if (id != customer.CustomerId)
            {
                return BadRequest("Customer ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                _customerRepository.Update(customer);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error updating customer {CustomerId}", id);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteCustomer(int id)
        {
            try
            {
                _customerRepository.Delete(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error deleting customer {CustomerId}", id);
            }
        }

        [HttpGet("by-email/{email}")]
        public async Task<ActionResult<Customer>> GetCustomerByEmail(string email)
        {
            try
            {
                var customer = await _customerRepository.GetByEmailAsync(email);
                if (customer == null)
                {
                    return NotFound();
                }
                return Ok(customer);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving customer by email {Email}", email);
            }
        }
    }
}
