using System.Collections.Concurrent;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Records every log entry with its category, event id and the properties of the scopes open when it was written.
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    public IReadOnlyList<Entry> For(int eventId, string category = "Tenantry.AspNetCore") =>
        [.. Entries.Where(e => e.EventId.Id == eventId && e.Category == category)];

    public sealed record Entry(
        string Category,
        EventId EventId,
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> ScopeProperties,
        IReadOnlyList<object?> Scopes);

    private sealed class Logger(RecordingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            List<KeyValuePair<string, object?>> properties = [];
            List<object?> scopes = [];
            provider._scopes.ForEachScope(
                (scope, _) =>
                {
                    scopes.Add(scope);

                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        properties.AddRange(pairs);
                    }
                },
                (object?)null);

            provider.Entries.Enqueue(new Entry(category, eventId, logLevel, formatter(state, exception), properties, scopes));
        }
    }
}
