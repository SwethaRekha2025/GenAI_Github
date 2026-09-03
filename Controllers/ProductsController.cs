using Microsoft.AspNetCore.Mvc;
using LegacyECommerceApi.Models;
using LegacyECommerceApi.Repositories;

namespace LegacyECommerceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : LegacyApiController
    {
        private readonly IProductRepository _productRepository;

        public ProductsController(IProductRepository productRepository, ILogger<ProductsController> logger)
            : base(logger)
        {
            _productRepository = productRepository;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            try
            {
                var products = await _productRepository.GetAllAsync();
                return Ok(products);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving products");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            try
            {
                var product = await _productRepository.GetByIdAsync(id);
                if (product == null)
                {
                    return NotFound();
                }
                return Ok(product);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving product {ProductId}", id);
            }
        }

        [HttpPost]
        public ActionResult<Product> PostProduct(Product product)
        {
            // Unreachable in production: [ApiController] answers 400 with ValidationProblemDetails
            // before the action body runs. Kept as the safety net if that attribute is ever removed.
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var createdProduct = _productRepository.Add(product);
                return CreatedAtAction(nameof(GetProduct), new { id = createdProduct.ProductId }, createdProduct);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error creating product");
            }
        }

        [HttpPut("{id}")]
        public IActionResult PutProduct(int id, Product product)
        {
            if (id != product.ProductId)
            {
                // A caller sending a mismatched id is a client defect worth seeing; without this
                // the rejection is invisible and a rising 4xx rate cannot be diagnosed (LOG-6).
                Logger.LogWarning(
                    "Rejected product update: route id {RouteId} does not match body id {BodyId}",
                    id, product.ProductId);
                return BadRequest("Product ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                _productRepository.Update(product);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error updating product {ProductId}", id);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteProduct(int id)
        {
            try
            {
                _productRepository.Delete(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error deleting product {ProductId}", id);
            }
        }

        [HttpGet("category/{category}")]
        public ActionResult<IEnumerable<Product>> GetProductsByCategory(string category)
        {
            try
            {
                var products = _productRepository.GetByCategory(category);
                return Ok(products);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving products by category {Category}", category);
            }
        }

        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<Product>>> GetActiveProducts()
        {
            try
            {
                var products = await _productRepository.GetActiveProductsAsync();
                return Ok(products);
            }
            catch (Exception ex)
            {
                return Failure(ex, "Error retrieving active products");
            }
        }
    }
}
