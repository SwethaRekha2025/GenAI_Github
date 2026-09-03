using LegacyECommerceApi.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Scoped so each repository reads the connection string once per request. They hold no state
// between calls, and each opens and closes its own SqlConnection per operation.
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Logging is registered by WebApplication.CreateBuilder; an explicit AddLogging() call here would
// add nothing. What is missing is configuration - no scopes, no correlation id, no telemetry.

var app = builder.Build();

// Swagger is Development-only; the API is otherwise undocumented at runtime.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// WARNING: this enforces nothing today. No authentication scheme is registered and no controller
// or action carries [Authorize], so the middleware finds no policy to apply and every endpoint is
// reachable anonymously - including DELETE /api/customers/{id}. Do not read this line as evidence
// the API is protected. Adding authentication is a deliberate, separate change.
app.UseAuthorization();

// No exception handler is registered either, so anything thrown outside a controller's own
// try/catch surfaces as a bare 500 in Production and as the developer page in Development.
app.MapControllers();

app.Run();

/// <summary>
/// Top-level statements generate an <c>internal</c> Program class, which
/// <c>WebApplicationFactory&lt;Program&gt;</c> cannot reach from the test assembly. Declaring it
/// public here is what allows integration tests to boot the real pipeline. Compile-time only:
/// it changes no runtime behaviour.
/// </summary>
public partial class Program { }
