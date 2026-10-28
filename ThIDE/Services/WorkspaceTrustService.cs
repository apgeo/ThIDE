// Workspace trust: a project's `.thide/` folder may define hooks, tasks and macros that run
// commands. Nothing project-defined runs until the user has said they trust the folder. The question
// is asked lazily — the first time something would actually execute — and the answer is kept in
// app data (trust.json), readable by a person. Reading project data (snippets, templates) never asks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Therion.Workspace;
using Therion.Workspace.Editing;

namespace ThIDE.Services;

public enum TrustState { NoRoot, Trusted, Untrusted }

public interface IWorkspaceTrustService
{
    TrustState Get(string? root);

    /// <summary>
    /// True when <paramref name="root"/> is trusted, asking the user first when it is not yet;
    /// <paramref name="reason"/> says what wants to run ("it defines hooks that run commands").
    /// </summary>
    Task<bool> EnsureTrustedAsync(string root, string reason);

    /// <summary>
    /// As <see cref="EnsureTrustedAsync"/>, for commands that start on their own (hooks, tasks a hook
    /// starts): one question at a time, and a "no" holds for the rest of the session — Save All must
    /// not ask once per file. <c>Asked</c> is false when the answer came from an earlier "no".
    /// </summary>
    Task<(bool Trusted, bool Asked)> EnsureTrustedOnceAsync(string root, string reason);

    IReadOnlyList<string> TrustedFolders { get; }
    void Trust(string root);
    void Revoke(string root);
    event EventHandler? Changed;
}

public sealed class WorkspaceTrustService : IWorkspaceTrustService
{
    private sealed class Store
    {
        public List<string> Trusted { get; set; } = [];
    }

    private readonly string _path;
    private readonly Func<string, string, Task<bool>> _ask;
    private readonly object _gate = new();
    private List<string> _trusted;
    private readonly HashSet<string> _declined = new(FilePaths.Comparer);
    private readonly System.Threading.SemaphoreSlim _asking = new(1, 1);

    /// <param name="storePath">trust.json (default: app data).</param>
    /// <param name="ask">Asks the user (folder, reason) → trust? Default: no one to ask, so no.</param>
    public WorkspaceTrustService(string? storePath = null, Func<string, string, Task<bool>>? ask = null)
    {
        _path = storePath ?? Path.Combine(Path.GetDirectoryName(ProjectSidecar.DirectoryFor("x"))!, "trust.json");
        _ask = ask ?? ((_, _) => Task.FromResult(false));
        _trusted = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<string> TrustedFolders { get { lock (_gate) return _trusted.ToList(); } }

    public TrustState Get(string? root)
    {
        if (string.IsNullOrEmpty(root)) return TrustState.NoRoot;
        var full = FilePaths.Full(root);
        lock (_gate) return _trusted.Any(t => FilePaths.Comparer.Equals(t, full)) ? TrustState.Trusted : TrustState.Untrusted;
    }

    public async Task<bool> EnsureTrustedAsync(string root, string reason)
    {
        switch (Get(root))
        {
            case TrustState.Trusted: return true;
            case TrustState.NoRoot: return false;
        }
        if (!await _ask(FilePaths.Full(root), reason)) return false;
        Trust(root);
        return true;
    }

    public async Task<(bool Trusted, bool Asked)> EnsureTrustedOnceAsync(string root, string reason)
    {
        if (Get(root) == TrustState.Trusted) return (true, false);
        await _asking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Get(root) == TrustState.Trusted) return (true, false);
            var full = FilePaths.Full(root);
            lock (_gate) if (_declined.Contains(full)) return (false, false);
            if (await EnsureTrustedAsync(root, reason).ConfigureAwait(false)) return (true, true);
            lock (_gate) _declined.Add(full);
            return (false, true);
        }
        finally { _asking.Release(); }
    }

    public void Trust(string root)
    {
        var full = FilePaths.Full(root);
        lock (_gate) _declined.Remove(full);
        lock (_gate)
        {
            if (_trusted.Any(t => FilePaths.Comparer.Equals(t, full))) return;
            _trusted.Add(full);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Revoke(string root)
    {
        var full = FilePaths.Full(root);
        lock (_gate)
        {
            if (_trusted.RemoveAll(t => FilePaths.Comparer.Equals(t, full)) == 0) return;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<string> Load()
    {
        var read = ProjectLocalConfig.TryReadJson<Store>(_path);
        return read.Value?.Trusted.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Store { Trusted = _trusted }, ProjectLocalConfig.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* kept for this session */ }
    }
}
