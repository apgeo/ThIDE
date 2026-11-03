// The disk preflight must measure the volume that actually holds the job folder: on Linux
// the job root is under the temp directory, which is often a separately-mounted tmpfs.

using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class DiskPreflightVolumeTests
{
    private const long Tolerance = 64L << 20; // 64 MB — other processes write meanwhile

    [Fact]
    public void FreeBytes_ForPathOnSeparateMount_ReportsThatMountNotTheRootFilesystem()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Pick a mount point that is not "/" and whose free space differs from "/" by more
        // than the tolerance, so the two answers are distinguishable.
        var drives = DriveInfo.GetDrives();
        var root = drives.FirstOrDefault(d => d.RootDirectory.FullName == "/");
        var separate = drives.FirstOrDefault(d =>
            d.RootDirectory.FullName is "/dev/shm" or "/tmp" or "/run"
            && root is not null
            && Math.Abs(d.AvailableFreeSpace - root.AvailableFreeSpace) > 4 * Tolerance);
        if (root is null || separate is null) return; // nothing to tell apart on this machine

        long expected = separate.AvailableFreeSpace;
        long? actual = DiskPreflight.TryGetFreeBytes(Path.Combine(separate.RootDirectory.FullName, "thide-probe", "job"));

        Assert.NotNull(actual);
        Assert.True(Math.Abs(actual.Value - expected) <= Tolerance,
            $"free space for a path under {separate.RootDirectory.FullName} was {actual.Value / 1_000_000} MB, " +
            $"but that mount has {expected / 1_000_000} MB free (the root filesystem has {root.AvailableFreeSpace / 1_000_000} MB)");
    }
}
