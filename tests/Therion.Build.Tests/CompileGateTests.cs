// M5b — CompileGate tests.

using Therion.Build;

namespace Therion.Build.Tests;

public class CompileGateTests
{
    [Fact]
    public void First_acquire_succeeds_second_returns_null()
    {
        var gate = new CompileGate();
        using var first = gate.TryAcquire();
        Assert.NotNull(first);
        Assert.True(gate.IsBusy);
        Assert.Null(gate.TryAcquire());
    }

    [Fact]
    public void Release_allows_subsequent_acquire()
    {
        var gate = new CompileGate();
        var first = gate.TryAcquire();
        Assert.NotNull(first);
        first!.Dispose();
        Assert.False(gate.IsBusy);

        using var second = gate.TryAcquire();
        Assert.NotNull(second);
    }
}
