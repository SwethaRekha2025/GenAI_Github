using Microsoft.Extensions.Logging;

namespace LegacyECommerceApi.Tests.Infrastructure
{
    /// <summary>
    /// Captures log calls so tests can assert that a failure was logged.
    /// The plan is deliberate about this: verify that LogError was invoked and that the
    /// exception was attached, never the message wording, which is not behaviour and will churn.
    /// </summary>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IReadOnlyList<LogEntry> Errors =>
            _entries.Where(e => e.Level == LogLevel.Error).ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
