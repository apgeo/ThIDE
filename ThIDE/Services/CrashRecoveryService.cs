// crash resilience.
//
// Two safety nets for an unclean exit (hard crash, killed from the debugger, power loss):
//   1. A run "sentinel" file written at launch and deleted on a clean shutdown. If it is still
//      present at the next launch, the previous run did not exit cleanly → the app starts in
//      SAFE MODE (skips the riskier float-window layout restore) so a bad layout can't brick it.
//   2. Periodic autosave of the *unsaved* editor buffers into a recovery folder (+ a manifest).
//      After a crash these survive and are offered back to the user; a clean shutdown clears them.
//
// Stored under %AppData%/ThIDE/ (XDG fallback on POSIX), mirroring the other JSON services.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ThIDE.Services;

/// <summary>An unsaved editor buffer recovered after a crash.</summary>
public sealed record RecoveredBuffer(string OriginalPath, string Text, DateTime SavedUtc);

public interface ICrashRecoveryService
{
    /// <summary>True when the previous run did not shut down cleanly (set by <see cref="MarkRunning"/>).</summary>
    bool PreviousRunCrashed { get; }

    /// <summary>Records that the app is now running; detects a leftover sentinel from a crashed run.</summary>
    void MarkRunning();
    /// <summary>Records a clean shutdown: removes the sentinel and clears the recovery folder.</summary>
    void MarkCleanShutdown();

    /// <summary>Writes the given unsaved buffers to the recovery folder (only the changed ones).</summary>
    void SaveBuffers(IEnumerable<(string Path, string Text)> dirty);
    /// <summary>Returns the recoverable buffers left by a crashed run (empty after a clean shutdown).</summary>
    IReadOnlyList<RecoveredBuffer> GetRecoverable();
    /// <summary>Deletes all recovery files (e.g. once the user has restored or dismissed them).</summary>
    void ClearRecovery();
}

public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly string _dir;          // recovery folder
    private readonly string _sentinel;     // run lock file
    private readonly string _manifest;     // recovery/manifest.json
    private readonly ILogger? _logger;

    // path → last-written content hash, so the 5 s autosave only rewrites buffers that changed.
    private readonly Dictionary<string, string> _lastHash = new(StringComparer.OrdinalIgnoreCase);
    // path → recovery file name, so a buffer keeps the same backing file across saves.
    private readonly Dictionary<string, string> _bufferFiles = new(StringComparer.OrdinalIgnoreCase);

    public bool PreviousRunCrashed { get; private set; }

    public CrashRecoveryService(ILogger<CrashRecoveryService>? logger = null) : this(DefaultRoot(), logger) { }

    public CrashRecoveryService(string root, ILogger<CrashRecoveryService>? logger = null)
    {
        _dir = Path.Combine(root, "recovery");
        _sentinel = Path.Combine(root, "running.lock");
        _manifest = Path.Combine(_dir, "manifest.json");
        _logger = logger;
    }

    public void MarkRunning()
    {
        try
        {
            PreviousRunCrashed = File.Exists(_sentinel);
            var dir = Path.GetDirectoryName(_sentinel);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_sentinel, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Crash sentinel could not be written."); }
    }

    public void MarkCleanShutdown()
    {
        try { if (File.Exists(_sentinel)) File.Delete(_sentinel); } catch { /* best-effort */ }
        ClearRecovery();
    }

    public void SaveBuffers(IEnumerable<(string Path, string Text)> dirty)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var entries = new List<ManifestEntry>();
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        try
        {
            if (!File.Exists(_manifest)) return result;
            var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(_manifest));
            if (entries is null) return result;
            foreach (var e in entries)
            {
                var full = Path.Combine(_dir, e.RecoveryFile);
                if (!File.Exists(full)) continue;
                try { result.Add(new RecoveredBuffer(e.OriginalPath, File.ReadAllText(full), e.SavedUtc)); }
                catch { /* skip an unreadable buffer */ }
            }
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to read recovery manifest."); }
        return result;
    }

    public void ClearRecovery()
    {
        _lastHash.Clear();
        _bufferFiles.Clear();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to clear recovery folder."); }
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
