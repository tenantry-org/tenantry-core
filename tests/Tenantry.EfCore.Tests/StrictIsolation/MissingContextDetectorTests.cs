using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.StrictIsolation;

public sealed class MissingContextDetectorTests
{
    [Fact]
    public void CheckAndWarn_TenantPresent_ReturnsTrue_NoWarningLogged()
    {
        var logger = new TestLogger<MissingContextDetector<string>>();
        MissingContextDetector<string> detector = new(logger);
        var ctx = TestTenantContext.For("acme");

        var result = detector.CheckAndWarn(ctx, "SaveChanges");

        result.Should().BeTrue();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void CheckAndWarn_NoTenant_ReturnsFalse()
    {
        var logger = new TestLogger<MissingContextDetector<string>>();
        MissingContextDetector<string> detector = new(logger);
        var ctx = TestTenantContext.Empty();

        var result = detector.CheckAndWarn(ctx, "SaveChanges");

        result.Should().BeFalse();
    }

    [Fact]
    public void CheckAndWarn_NoTenant_LogsWarning()
    {
        var logger = new TestLogger<MissingContextDetector<string>>();
        MissingContextDetector<string> detector = new(logger);
        var ctx = TestTenantContext.Empty();

        detector.CheckAndWarn(ctx, "SaveChanges");

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].LogLevel.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain("SaveChanges");
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);
    }
    
    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
