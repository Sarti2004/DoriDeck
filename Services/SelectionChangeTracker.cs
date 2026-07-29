using System.Diagnostics;

namespace DoriDeck.Services;

internal sealed class SelectionChangeTracker
{
    private readonly object _lock = new();

    private TaskCompletionSource<long> _nextSelectionChanged =
        CreateSelectionChangedSource();

    private long _selectionVersion;

    public long CurrentVersion => Volatile.Read(ref _selectionVersion);

    public void NotifySelectionChanged()
    {
        var version = Interlocked.Increment(ref _selectionVersion);
        TaskCompletionSource<long> sourceToComplete;

        lock (_lock)
        {
            sourceToComplete = _nextSelectionChanged;
            _nextSelectionChanged = CreateSelectionChangedSource();
        }

        sourceToComplete.TrySetResult(version);
    }

    public async Task<bool> WaitForChangeAfterAsync(
        long version,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        while (Volatile.Read(ref _selectionVersion) <= version)
        {
            var remaining = timeout - started.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            Task<long> nextChange;

            lock (_lock)
            {
                if (Volatile.Read(ref _selectionVersion) > version)
                {
                    return true;
                }

                nextChange = _nextSelectionChanged.Task;
            }

            try
            {
                await nextChange.WaitAsync(
                    remaining,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return true;
    }

    public async Task WaitForSettleAsync(
        TimeSpan quietPeriod,
        TimeSpan maximumWait,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        while (started.Elapsed < maximumWait)
        {
            var version = Volatile.Read(ref _selectionVersion);

            await Task.Delay(
                quietPeriod,
                cancellationToken);

            if (Volatile.Read(ref _selectionVersion) == version)
            {
                return;
            }
        }
    }

    private static TaskCompletionSource<long> CreateSelectionChangedSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
