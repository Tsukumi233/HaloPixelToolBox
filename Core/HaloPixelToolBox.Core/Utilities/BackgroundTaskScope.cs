namespace HaloPixelToolBox.Core.Utilities;

/// <summary>Owns background work, rejects new work after stopping, and observes every task.</summary>
public sealed class BackgroundTaskScope : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly HashSet<Task> _tasks = [];
    private Task? _stopTask;
    private bool _stopping;

    public CancellationToken Token { get; }

    public BackgroundTaskScope() => Token = _cancellation.Token;

    public void Run(Func<Task> work)
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            var task = Task.Run(async () =>
            {
                try
                {
                    Token.ThrowIfCancellationRequested();
                    await work().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]后台任务失败：{ex}");
                }
            });
            _tasks.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_gate)
                    _tasks.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopTask is not null)
                return _stopTask;
            _stopping = true;
            var tasks = _tasks.ToArray();
            // Cancellation callbacks may schedule/complete work, so cancel outside the lock.
            _stopTask = Task.Run(async () =>
            {
                await _cancellation.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _cancellation.Dispose();
            });
            return _stopTask;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
