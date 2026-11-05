// Progress<T> posts each report to the thread pool when there is no synchronization context, so a
// test that reads its list right after the await races the callbacks ("Collection was modified",
// or a report not there yet). This reports on the caller's thread, before Report returns, one report
// at a time (the runner reads stdout and stderr on separate threads).

namespace Therion.Blender.Tests;

internal sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
{
    private readonly object _gate = new();

    public void Report(T value) { lock (_gate) onReport(value); }
}
