using Microsoft.AspNetCore.Mvc;

namespace LegacyECommerceApi.Controllers
{
    /// <summary>
    /// Holds the one piece of behaviour every controller action repeats: log the failure, then
    /// answer 500 with a fixed body. Before this, the status code and the literal below were
    /// written out in all twenty actions, so changing the error contract meant twenty correct edits.
    ///
    /// This is deliberately a thin, temporary seam. The real fix is a global IExceptionHandler with
    /// AddProblemDetails registered in Program.cs, which replaces the bare string with RFC 7807 and
    /// lets every try/catch here be deleted outright. That is a separate change: it touches
    /// Program.cs and it alters the response contract, so it needs its own decision and its own
    /// test updates. When it lands, this class goes with it.
    /// </summary>
    public abstract class LegacyApiController : ControllerBase
    {
        /// <summary>The exact body returned for an unhandled failure today. Do not vary it here.</summary>
        protected const string InternalServerErrorBody = "Internal server error";

        private readonly ILogger _logger;

        /// <summary>
        /// Takes the non-generic ILogger, but callers pass their ILogger&lt;TController&gt;, so the
        /// log category stays per-controller. Injecting a bare ILogger at the call site instead
        /// would silently flatten every controller's logs into one category.
        /// </summary>
        protected LegacyApiController(ILogger logger)
        {
            _logger = logger;
        }

        protected ObjectResult Failure(Exception exception, string message, params object?[] args)
        {
            _logger.LogError(exception, message, args);
            return StatusCode(StatusCodes.Status500InternalServerError, InternalServerErrorBody);
        }
    }
}
