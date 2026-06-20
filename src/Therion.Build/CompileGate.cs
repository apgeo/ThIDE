// Implementation Plan Decision #27 — one compile at a time per workspace.

namespace Therion.Build;

/// <summary>
/// Gates concurrent compile requests. Additional requests while a compile is
/// in progress are rejected; the caller can Cancel + retry.
/// </summary>
public interface ICompileGate
{
    bool IsBusy { get; }
    /// <summary>Returns <c>null</c> if a compile is already in progress.</summary>
    IDisposable? TryAcquire();
}

public sealed class CompileGate : ICompileGate
{
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public IDisposable? TryAcquire()
        => Interlocked.CompareExchange(ref _busy, 1, 0) == 0 ? new Releaser(this) : null;

    private sealed class Releaser : IDisposable
    {
        private readonly CompileGate _gate;
        public Releaser(CompileGate g) { _gate = g; }
        public void Dispose() => Interlocked.Exchange(ref _gate._busy, 0);
    }
}
