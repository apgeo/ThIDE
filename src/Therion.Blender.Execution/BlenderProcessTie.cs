// Ties a Blender to whoever started it, so that it cannot outlive ThIDE or a render worker that is
// killed or crashes: on Windows the process goes into a Job Object that kills what it holds when
// its last handle closes (when the process that made it ends, however it ends); on Linux it is
// started through setpriv with a parent-death signal, from one thread that lives as long as the
// process (the signal follows the thread that started the child, not the process). macOS has
// neither: there, only a stop that is asked for (a cancel, closing ThIDE) ends Blender.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Therion.Blender.Execution;

internal static class BlenderProcessTie
{
    private static readonly Lazy<string?> Setpriv = new(FindSetpriv);

    /// <summary>
    /// <paramref name="psi"/>, made to start its program through <c>setpriv --pdeathsig KILL</c>
    /// on Linux when setpriv takes that option and the program is there to start; unchanged
    /// otherwise (a program that is not there must fail to start as it would without setpriv).
    /// </summary>
    public static void TieToParent(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsLinux() || Setpriv.Value is not { } setpriv || Resolve(psi.FileName) is not { } program) return;
        var arguments = psi.ArgumentList.ToList();
        psi.FileName = setpriv;
        psi.ArgumentList.Clear();
        foreach (string a in (string[])["--pdeathsig", "KILL", "--", program, .. arguments]) psi.ArgumentList.Add(a);
    }

    /// <summary>Starts <paramref name="process"/>: on Linux from the one thread that never ends,
    /// so that the parent-death signal comes only when the process that started it ends.</summary>
    public static void Start(Process process)
    {
        if (OperatingSystem.IsLinux()) StarterThread.Run(() => process.Start());
        else process.Start();
    }

    /// <summary>A Job Object that kills <paramref name="process"/> and every process it starts
    /// when it is disposed, or when the process holding it ends; null off Windows or when the
    /// process cannot be put in one.</summary>
    public static IDisposable? Job(Process process) => OperatingSystem.IsWindows() ? WindowsJob.For(process) : null;

    private static string? Resolve(string program)
    {
        if (program.Contains('/'))
            return File.Exists(program) && IsExecutable(program) ? Path.GetFullPath(program) : null;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder, program);
            if (File.Exists(candidate) && IsExecutable(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return true;
        try { return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>setpriv, when it is there and takes <c>--pdeathsig</c> (util-linux 2.33 and later).</summary>
    private static string? FindSetpriv()
    {
        if (!OperatingSystem.IsLinux()) return null;
        string? setpriv = Resolve("setpriv");
        if (setpriv is null || Resolve("true") is not { } truth) return null;
        try
        {
            var psi = new ProcessStartInfo(setpriv) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string a in (string[])["--pdeathsig", "KILL", "--", truth]) psi.ArgumentList.Add(a);
            using var probe = Process.Start(psi);
            if (probe is null) return null;
            if (!probe.WaitForExit(5000))
            {
                try { probe.Kill(); } catch (InvalidOperationException) { }
                return null;
            }
            return probe.ExitCode == 0 ? setpriv : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    /// <summary>One thread, for the life of the process, that starts the processes.</summary>
    private static class StarterThread
    {
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> Work = [];
        private static readonly Lazy<Thread> Thread = new(() =>
        {
            var thread = new Thread(() =>
            {
                foreach (var action in Work.GetConsumingEnumerable()) action();
            })
            { IsBackground = true, Name = "Blender starter" };
            thread.Start();
            return thread;
        });

        public static void Run(Action action)
        {
            _ = Thread.Value;
            ExceptionDispatchInfo? failure = null;
            using var done = new ManualResetEventSlim();
            Work.Add(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                finally { done.Set(); }
            });
            done.Wait();
            failure?.Throw();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WindowsJob : IDisposable
    {
        private IntPtr _handle;

        private WindowsJob(IntPtr handle) => _handle = handle;

        public static WindowsJob? For(Process process)
        {
            IntPtr job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
            if (job == IntPtr.Zero) return null;
            var info = new JobObjectExtendedLimitInformation { BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = KillOnJobClose } };
            bool ok = SetInformationJobObject(job, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>());
            try { ok = ok && AssignProcessToJobObject(job, process.Handle); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { ok = false; }
            if (ok) return new WindowsJob(job);
            CloseHandle(job);
            return null;
        }

        public void Dispose()
        {
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }

        private const int ExtendedLimitInformation = 9;
        private const uint KillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
