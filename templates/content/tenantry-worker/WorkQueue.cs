using System.Threading.Channels;

namespace TenantryWorker;

// Stands in for a message broker: replace it with your broker's consumer.
public sealed class WorkQueue
{
    private readonly Channel<OrderMessage> _messages = Channel.CreateUnbounded<OrderMessage>();

    public ValueTask EnqueueAsync(OrderMessage message, CancellationToken cancellationToken = default) =>
        _messages.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<OrderMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _messages.Reader.ReadAllAsync(cancellationToken);
}
