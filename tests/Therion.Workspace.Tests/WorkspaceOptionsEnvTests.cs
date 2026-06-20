// M5 follow-up — env-var disk-cache controls (default flipped to disabled in Post-M6 D).

using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class WorkspaceOptionsEnvTests
{
    private static IDisposable WithEnv(string name, string? value)
    {
        var prev = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new Restore(name, prev);
    }

    private sealed class Restore : IDisposable
    {
        private readonly string _name;
        private readonly string? _prev;
        public Restore(string name, string? prev) { _name = name; _prev = prev; }
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _prev);
    }

    [Fact]
    public void Disk_cache_is_disabled_by_default()
    {
        using var _1 = WithEnv(WorkspaceOptions.DisableDiskCacheEnvVar, null);
        using var _2 = WithEnv(WorkspaceOptions.EnableDiskCacheEnvVar, null);
        using var _3 = WithEnv(WorkspaceOptions.DiskCacheFormatEnvVar, null);
        var opts = WorkspaceOptions.FromEnvironment();
        Assert.True(opts.DisableDiskCache);
        Assert.Equal(DiskCacheFormat.MessagePack, opts.DiskCacheFormat);
    }

    [Fact]
    public void Enable_env_var_opts_in()
    {
        using var _1 = WithEnv(WorkspaceOptions.DisableDiskCacheEnvVar, null);
        using var _2 = WithEnv(WorkspaceOptions.EnableDiskCacheEnvVar, "1");
        var opts = WorkspaceOptions.FromEnvironment();
        Assert.False(opts.DisableDiskCache);
    }

    [Fact]
    public void Disable_env_var_wins_over_enable()
    {
        using var _1 = WithEnv(WorkspaceOptions.EnableDiskCacheEnvVar, "1");
        using var _2 = WithEnv(WorkspaceOptions.DisableDiskCacheEnvVar, "1");
        var opts = WorkspaceOptions.FromEnvironment();
        Assert.True(opts.DisableDiskCache);
    }

    [Fact]
    public void Format_env_var_selects_backend()
    {
        using var _1 = WithEnv(WorkspaceOptions.DiskCacheFormatEnvVar, "json");
        var opts = WorkspaceOptions.FromEnvironment();
        Assert.Equal(DiskCacheFormat.Json, opts.DiskCacheFormat);

        using var _2 = WithEnv(WorkspaceOptions.DiskCacheFormatEnvVar, "msgpack");
        var opts2 = WorkspaceOptions.FromEnvironment();
        Assert.Equal(DiskCacheFormat.MessagePack, opts2.DiskCacheFormat);
    }
}
