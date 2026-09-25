using Microsoft.Extensions.Logging;
using PcDashboard.Core;

namespace PcDashboard.Desktop;

public sealed class LocalLogProvider(SqliteDiagnostics diagnostics, Action onFailure) : ILoggerProvider
{
    private int failureReported;
    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }
    private void Write(string message)
    {
        if (!diagnostics.TryWrite(message) && Interlocked.Exchange(ref failureReported, 1) == 0) onFailure();
    }
    private sealed class LocalLogger(LocalLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) owner.Write($"{level} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
