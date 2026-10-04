using System.Collections.Concurrent;

namespace Tenantry.Core.Tests;

/// <summary>
/// A synchronization context with one thread that runs what is posted to it in order, as a desktop app's UI thread
/// does. Code blocked on that thread holds up everything posted after it.
/// </summary>
internal sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;

    public SingleThreadContext()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = nameof(SingleThreadContext) };
        _thread.Start();
    }

    /// <summary>Whether the calling code runs on this context's thread, with this context current.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _thread.ManagedThreadId && Current == this;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Code under test should not send to the context.");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs <paramref name="work"/> on this context's thread.</summary>
    public Task<T> Run<T>(Func<T> work)
    {
        TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception e)
            {
                result.SetException(e);
            }
        }, null);
        return result.Task;
    }

    /// <summary>Starts <paramref name="work"/> on this context's thread, where its awaits return unless told not to.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(static state =>
        {
            var (start, completion) = ((Func<Task<T>>, TaskCompletionSource<T>))state!;
            _ = CompleteAsync(start, completion);
        }, (work, result));
        return result.Task;
    }

    private static async Task CompleteAsync<T>(Func<Task<T>> work, TaskCompletionSource<T> result)
    {
        try
        {
            result.SetResult(await work());
        }
        catch (Exception e)
        {
            result.SetException(e);
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Pump()
    {
        SetSynchronizationContext(this);

        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }
}
