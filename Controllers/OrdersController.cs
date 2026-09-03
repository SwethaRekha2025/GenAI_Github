using Microsoft.AspNetCore.Mvc;
using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;

namespace LegacyECommerceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : LegacyApiController
    {
        private readonly IOrderRepository _orderRepository;

        public OrdersController(IOrderRepository orderRepository, ILogger<OrdersController> logger)
            : base(logger)
        {
            _orderRepository = orderRepository;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            try
            {
                var orders = await _orderRepository.GetAllAsync();
                return Ok(orders);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving orders");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(id);
                if (order == null)
                {
                    return NotFound();
                }
                return Ok(order);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving order {OrderId}", id);
            }
        }

        [HttpPost]
        public ActionResult<Order> PostOrder(Order order)
        {
            // Unreachable in production: [ApiController] answers 400 with ValidationProblemDetails
            // before the action body runs. Kept as the safety net if that attribute is ever removed.
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // The only business rule in the HTTP layer. It belongs in an order service, sourced
                // from an injected TimeProvider; both are later phases.
                order.OrderDate = DateTime.UtcNow;
                var createdOrder = _orderRepository.Add(order);
                return CreatedAtAction(nameof(GetOrder), new { id = createdOrder.OrderId }, createdOrder);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error creating order");
            }
        }

        [HttpPut("{id}")]
        public IActionResult PutOrder(int id, Order order)
        {
            if (id != order.OrderId)
            {
                return BadRequest("Order ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                _orderRepository.Update(order);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error updating order {OrderId}", id);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteOrder(int id)
        {
            try
            {
                _orderRepository.Delete(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error deleting order {OrderId}", id);
            }
        }

        [HttpGet("customer/{customerId}")]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrdersByCustomer(int customerId)
        {
            try
            {
                var orders = await _orderRepository.GetByCustomerIdAsync(customerId);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving orders for customer {CustomerId}", customerId);
            }
        }

        [HttpGet("status/{status}")]
        public ActionResult<IEnumerable<Order>> GetOrdersByStatus(string status)
        {
            try
            {
                var orders = _orderRepository.GetByStatus(status);
                return Ok(orders);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving orders by status {Status}", status);
            }
        }
    }
}
