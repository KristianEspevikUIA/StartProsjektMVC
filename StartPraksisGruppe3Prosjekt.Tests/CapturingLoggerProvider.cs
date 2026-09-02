using Microsoft.Extensions.Logging;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Keeps errors the application logs, so a failing integration test can say what actually
/// went wrong instead of only reporting the status code the browser would have seen.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _errors;

    public CapturingLoggerProvider(List<string> errors) => _errors = errors;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _errors);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly List<string> _errors;

        public CapturingLogger(string category, List<string> errors)
        {
            _category = category;
            _errors = errors;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{logLevel} {_category}: {formatter(state, exception)}";

            if (exception is not null)
            {
                message += Environment.NewLine + exception;
            }

            // The list is shared across the parallel requests one test can make.
            lock (_errors)
            {
                _errors.Add(message);
            }
        }
    }
}
