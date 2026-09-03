using System.Security.Cryptography;
using System.Text;
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
    /// test updates. When it lands, this class goes with it - and <see cref="Redact"/> moves to
    /// whatever owns log redaction then.
    /// </summary>
    public abstract class LegacyApiController : ControllerBase
    {
        /// <summary>The exact body returned for an unhandled failure today. Do not vary it here.</summary>
        protected const string InternalServerErrorBody = "Internal server error";

        /// <summary>
        /// Exposed so actions can record client errors directly. Callers pass their
        /// ILogger&lt;TController&gt;, so the log category stays per-controller; injecting a bare
        /// ILogger at the call site instead would flatten every controller into one category.
        /// </summary>
        protected ILogger Logger { get; }

        protected LegacyApiController(ILogger logger)
        {
            Logger = logger;
        }

        protected ObjectResult Failure(Exception exception, string message, params object?[] args)
        {
            Logger.LogError(exception, message, args);
            return StatusCode(StatusCodes.Status500InternalServerError, InternalServerErrorBody);
        }

        /// <summary>
        /// A stable, one-way token for an identifying value, so a failure stays correlatable across
        /// log entries without the identifier itself entering the log store (finding LOG-3). Logs
        /// typically have broader access, longer retention and weaker deletion guarantees than the
        /// database, so personal data must not leak into them.
        ///
        /// Not a security boundary: the input space for something like an email address is small
        /// enough to brute-force from a hash. It stops casual exposure and keeps log queries
        /// working; it is not a substitute for controlling who can read the logs.
        /// </summary>
        protected static string Redact(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(empty)";
            }

            var normalised = value.Trim().ToLowerInvariant();
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
            return Convert.ToHexString(digest)[..12];
        }
    }
}
