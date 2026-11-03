// crash resilience.
//
// Two safety nets for an unclean exit (hard crash, killed from the debugger, power loss):
//   1. A run "sentinel" file written at launch and deleted on a clean shutdown. If it is still
//      present at the next launch, the previous run did not exit cleanly → the app starts in
//      SAFE MODE (skips the riskier float-window layout restore) so a bad layout can't brick it.
//   2. Periodic autosave of the *unsaved* editor buffers into a recovery folder (+ a manifest).
//      After a crash these survive and are offered back to the user until restored or dismissed; a
//      clean shutdown clears only that run's own copies.
//
// Stored under %AppData%/ThIDE/ (XDG fallback on POSIX), mirroring the other JSON services.
//
// Several ThIDE processes can share that folder (a file opened from the OS file association while
// the app already runs starts a second one), so the state is per instance: each run holds its own
// `running.<id>.lock` open with an exclusive lock and autosaves into `recovery/<id>/`. A lock file
// that can be opened exclusively belongs to a run that has ended without cleaning up (a crash: the
// OS released its lock); its recovery copies are the ones offered back. A live instance's are not
// touched. The single `running.lock` + flat `recovery/` of earlier versions is read as a crashed run.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Therion.Workspace.Editing;

namespace ThIDE.Services;

/// <summary>An unsaved editor buffer recovered after a crash.</summary>
public sealed record RecoveredBuffer(string OriginalPath, string Text, DateTime SavedUtc);

public interface ICrashRecoveryService
{
    /// <summary>True when the previous run did not shut down cleanly (set by <see cref="MarkRunning"/>).</summary>
    bool PreviousRunCrashed { get; }

    /// <summary>Records that the app is now running; detects a leftover sentinel from a crashed run.</summary>
    void MarkRunning();
    /// <summary>Records a clean shutdown: removes the sentinel and clears this run's own recovery copies —
    /// unless <paramref name="keepRecovery"/> is set, for an exit that could not ask about unsaved buffers.
    /// Copies left by crashed runs stay until <see cref="ClearRecovery"/> (restored or dismissed).</summary>
    void MarkCleanShutdown(bool keepRecovery = false);

    /// <summary>Writes the given unsaved buffers to the recovery folder (only the changed ones).</summary>
    void SaveBuffers(IEnumerable<(string Path, string Text)> dirty);
    /// <summary>
    /// Returns the recoverable buffers: this run's own plus those left by crashed runs (never those of
    /// another instance that is still running). Empty after a clean shutdown.
    /// </summary>
    IReadOnlyList<RecoveredBuffer> GetRecoverable();
    /// <summary>Deletes this run's and crashed runs' recovery files (e.g. once the user has restored or dismissed them).</summary>
    void ClearRecovery();
}

public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private const string LockPrefix = "running.";
    private const string LockSuffix = ".lock";
    private const string ManifestName = "manifest.json";

    private readonly string _root;         // profile folder holding the locks and recovery/
    private readonly string _recoveryRoot; // recovery/ (one sub-folder per instance)
    private readonly string _id;           // this instance's id
    private readonly string _dir;          // recovery/<id>
    private readonly string _sentinel;     // running.<id>.lock
    private readonly string _manifest;     // recovery/<id>/manifest.json
    private readonly ILogger? _logger;
    private FileStream? _lock;             // held open (exclusively) while this instance runs

    // path → last-written content hash, so the 5 s autosave only rewrites buffers that changed.
    private readonly Dictionary<string, string> _lastHash = new(FilePaths.Comparer);
    // path → recovery file name, so a buffer keeps the same backing file across saves.
    private readonly Dictionary<string, string> _bufferFiles = new(FilePaths.Comparer);

    public bool PreviousRunCrashed { get; private set; }

    public CrashRecoveryService(ILogger<CrashRecoveryService>? logger = null) : this(DefaultRoot(), logger) { }

    public CrashRecoveryService(string root, ILogger<CrashRecoveryService>? logger = null)
    {
        _root = root;
        _recoveryRoot = Path.Combine(root, "recovery");
        _id = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
        _dir = Path.Combine(_recoveryRoot, _id);
        _sentinel = LockPath(_id);
        _manifest = Path.Combine(_dir, ManifestName);
        _logger = logger;
    }

    public void MarkRunning()
    {
        try
        {
            Directory.CreateDirectory(_root);
            // A crashed run's lock is no longer held; a running instance's is. Stale locks are
            // removed once seen, so safe mode applies to the next start only (their recovery
            // copies stay until restored or dismissed).
            var legacy = Path.Combine(_root, "running.lock");
            bool crashed = File.Exists(legacy);
            if (crashed) TryDelete(legacy);
            foreach (var other in Directory.GetFiles(_root, LockPrefix + "*" + LockSuffix))
            {
                if (FilePaths.Same(other, _sentinel) || IsHeld(other)) continue;
                crashed = true;
                TryDelete(other);
            }
            PreviousRunCrashed = crashed;
            _lock?.Dispose();
            _lock = new FileStream(_sentinel, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var stamp = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            _lock.Write(stamp);
            _lock.Flush();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Crash sentinel could not be written."); }
    }

    public void MarkCleanShutdown(bool keepRecovery = false)
    {
        try { _lock?.Dispose(); } catch { /* best-effort */ }
        _lock = null;
        TryDelete(_sentinel);
        if (keepRecovery) return;
        // Only this run's folder: a crashed run's copies that were offered but not restored or
        // dismissed are offered again at the next start.
        _lastHash.Clear();
        _bufferFiles.Clear();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to clear recovery folder."); }
    }

    public void SaveBuffers(IEnumerable<(string Path, string Text)> dirty)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var entries = new List<ManifestEntry>();
            var live = new HashSet<string>(FilePaths.Comparer);

            foreach (var (path, text) in dirty)
            {
                if (string.IsNullOrEmpty(path)) continue;
                live.Add(path);
                var file = _bufferFiles.TryGetValue(path, out var existing) ? existing : NewBufferName(path);
                _bufferFiles[path] = file;
                var full = Path.Combine(_dir, file);

                var hash = Hash(text);
                if (!_lastHash.TryGetValue(path, out var prev) || prev != hash)
                {
                    File.WriteAllText(full, text);
                    _lastHash[path] = hash;
                }
                entries.Add(new ManifestEntry(path, file, DateTime.UtcNow));
            }

            // Drop backing files for buffers that are no longer dirty (saved or closed).
            foreach (var kv in new List<KeyValuePair<string, string>>(_bufferFiles))
                if (!live.Contains(kv.Key))
                {
                    TryDelete(Path.Combine(_dir, kv.Value));
                    _bufferFiles.Remove(kv.Key);
                    _lastHash.Remove(kv.Key);
                }

            File.WriteAllText(_manifest, JsonSerializer.Serialize(entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to autosave recovery buffers."); }
    }

    public IReadOnlyList<RecoveredBuffer> GetRecoverable()
    {
        var result = new List<RecoveredBuffer>();
        foreach (var dir in OwnedRecoveryFolders())
        {
            var manifest = Path.Combine(dir, ManifestName);
            try
            {
                if (!File.Exists(manifest)) continue;
                var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(manifest));
                if (entries is null) continue;
                foreach (var e in entries)
                {
                    var full = Path.Combine(dir, e.RecoveryFile);
                    if (!File.Exists(full)) continue;
                    try { result.Add(new RecoveredBuffer(e.OriginalPath, File.ReadAllText(full), e.SavedUtc)); }
                    catch { /* skip an unreadable buffer */ }
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to read recovery manifest."); }
        }
        return result;
    }

    public void ClearRecovery()
    {
        _lastHash.Clear();
        _bufferFiles.Clear();
        foreach (var dir in OwnedRecoveryFolders())
        {
            try
            {
                if (FilePaths.Same(dir, _recoveryRoot))
                {
                    // Earlier versions' flat layout: only its files; the sub-folders are other instances'.
                    foreach (var f in Directory.GetFiles(dir)) TryDelete(f);
                }
                else if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to clear recovery folder."); }
        }
    }

    /// <summary>
    /// The recovery folders this instance may read and clear: its own, those of runs whose lock is
    /// gone or no longer held (crashed), and the flat folder an earlier version left behind.
    /// </summary>
    private List<string> OwnedRecoveryFolders()
    {
        var result = new List<string> { _dir };
        try
        {
            if (!Directory.Exists(_recoveryRoot)) return result;
            if (File.Exists(Path.Combine(_recoveryRoot, ManifestName))) result.Add(_recoveryRoot);
            foreach (var dir in Directory.GetDirectories(_recoveryRoot))
            {
                var id = Path.GetFileName(dir);
                if (id == _id || IsHeld(LockPath(id))) continue;
                result.Add(dir);
            }
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to list recovery folders."); }
        return result;
    }

    private string LockPath(string id) => Path.Combine(_root, LockPrefix + id + LockSuffix);

    /// <summary>True when another live instance holds <paramref name="lockFile"/> open (its run is ongoing).</summary>
    private static bool IsHeld(string lockFile)
    {
        try
        {
            using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            // Still empty: an instance is between creating and locking it (POSIX) — not a crash.
            return probe.Length == 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }               // sharing violation / lock held
        catch (UnauthorizedAccessException) { return true; } // cannot tell: leave it alone
    }

    private static string NewBufferName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return $"{stem}.{Guid.NewGuid():N}.bak";
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static string DefaultRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE");
    }

    private sealed record ManifestEntry(string OriginalPath, string RecoveryFile, DateTime SavedUtc);
}
