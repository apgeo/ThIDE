using ThIDE.Services;

namespace ThIDE.Tests;

public class WorkspaceTrustServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-trust-").FullName;
    private string Store => Path.Combine(_dir, "trust.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Asks_once_then_remembers_across_instances()
    {
        int asked = 0;
        var trust = new WorkspaceTrustService(Store, (_, _) => { asked++; return Task.FromResult(true); });
        var cave = Path.Combine(_dir, "cave");

        Assert.Equal(TrustState.Untrusted, trust.Get(cave));
        Assert.True(await trust.EnsureTrustedAsync(cave, "hooks"));
        Assert.True(await trust.EnsureTrustedAsync(cave, "hooks"));
        Assert.Equal(1, asked);

        Assert.Equal(TrustState.Trusted, new WorkspaceTrustService(Store).Get(cave));
    }

    [Fact]
    public async Task Declining_trusts_nothing_and_revoke_forgets()
    {
        var cave = Path.Combine(_dir, "cave");
        var no = new WorkspaceTrustService(Store, (_, _) => Task.FromResult(false));
        Assert.False(await no.EnsureTrustedAsync(cave, "tasks"));
        Assert.Equal(TrustState.Untrusted, no.Get(cave));

        no.Trust(cave);
        no.Revoke(cave);
        Assert.Equal(TrustState.Untrusted, new WorkspaceTrustService(Store).Get(cave));
    }

    [Fact]
    public async Task A_no_to_an_automatic_run_is_not_asked_again()
    {
        int asked = 0;
        var trust = new WorkspaceTrustService(Store, (_, _) => { asked++; return Task.FromResult(false); });
        var cave = Path.Combine(_dir, "cave");

        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => trust.EnsureTrustedOnceAsync(cave, "hooks")));

        Assert.Equal(1, asked);
        Assert.Single(answers, a => a.Asked);
        Assert.All(answers, a => Assert.False(a.Trusted));
    }

    [Fact]
    public void No_root_is_never_trusted()
    {
        Assert.Equal(TrustState.NoRoot, new WorkspaceTrustService(Store).Get(null));
    }

    [Fact]
    public void Case_rule_follows_the_platform()
    {
        var trust = new WorkspaceTrustService(Store);
        trust.Trust(Path.Combine(_dir, "Cave"));

        var other = trust.Get(Path.Combine(_dir, "cave"));
        Assert.Equal(OperatingSystem.IsLinux() ? TrustState.Untrusted : TrustState.Trusted, other);
    }
}
