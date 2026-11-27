// The folders where ThIDE keeps what Blender runs and reads while it renders: a render worker's
// jobs (the bundles, the player, the frames), the app's Blender job folders and its render queue.
// They are never a fixed name in a temporary folder that every user of the machine shares: there,
// another user could make the folder first, and then swap what Blender runs (the player's script)
// for code of theirs, which would run with the rights of whoever renders. So they live under the
// user's own cache folder ($XDG_CACHE_HOME or ~/.cache on Linux, ~/Library/Caches on macOS,
// %LOCALAPPDATA% on Windows), created for the user alone, and a folder that is not the user's, or
// that others may write, is refused.

namespace Therion.Blender.Execution;

/// <summary>A folder that would let another user change what Blender runs or reads.</summary>
public sealed class UnsafeFolderException(string message) : UnauthorizedAccessException(message);

/// <summary>The per-user folders of ThIDE's renders, and the checks that keep them private.</summary>
public static class PrivateFolders
{
    /// <summary>The folder under the user's cache folder that holds ThIDE's render folders.</summary>
    public const string RootName = "ThIDE";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>The user's own folder for ThIDE's render folders (not created here).</summary>
    public static string Root => RootFor(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The root as <see cref="Root"/> works it out, from the environment and the
    /// folders given: on Windows %LOCALAPPDATA%\ThIDE, on macOS ~/Library/Caches/ThIDE, elsewhere
    /// $XDG_CACHE_HOME/ThIDE (when it is an absolute path) or ~/.cache/ThIDE.</summary>
    internal static string RootFor(Func<string, string?> environment, string home, string localAppData)
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(localAppData.Length > 0 ? localAppData : Path.Combine(home, "AppData", "Local"), RootName);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Caches", RootName);
        string? cache = environment("XDG_CACHE_HOME");
        return Path.Combine(!string.IsNullOrEmpty(cache) && Path.IsPathRooted(cache) ? cache : Path.Combine(home, ".cache"), RootName);
    }

    /// <summary>The folder <paramref name="name"/> under <see cref="Root"/> (not created here).</summary>
    public static string Of(string name) => Path.Combine(Root, name);

    /// <summary>
    /// Makes <paramref name="path"/> a folder for the user alone, or checks that it is one, and
    /// returns its full path. On Linux and macOS a folder that is not there is made readable by
    /// the user only (and so are the folders above it that are made with it); one that is there
    /// must be the user's own, no link, and not writable by its group or by others, and the folder
    /// it lies in must not be writable by others (unless, like /tmp, it has the sticky bit, so that
    /// none may move what is not theirs). <paramref name="tighten"/> makes a folder of ThIDE's own
    /// that is the user's readable by the user only, instead of refusing it. On Windows the folder
    /// is made as it is: the user's own folders there are the user's alone.
    /// </summary>
    /// <exception cref="UnsafeFolderException">The folder is not the user's, or others may change it.</exception>
    public static string Ensure(string path, bool tighten = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(full);
            return full;
        }
        if (!Directory.Exists(full))
        {
            // Each folder that is not there yet is made for the user alone (.NET gives the mode
            // to the last one only).
            var missing = new Stack<string>();
            for (string? at = full; at is not null && !Directory.Exists(at); at = Path.GetDirectoryName(at)) missing.Push(at);
            while (missing.Count > 0) Directory.CreateDirectory(missing.Pop(), OwnerOnly);
        }
        var info = new DirectoryInfo(full);
        if (info.LinkTarget is not null)
            throw new UnsafeFolderException($"The folder {full} is a link; a folder renders are kept in is a folder of its own.");
        var mode = info.UnixFileMode;
        if (!IsOwn(full, tighten ? OwnerOnly : mode))
            throw new UnsafeFolderException($"The folder {full} belongs to another user; renders are kept only in a folder of the user who renders.");
        if (!tighten && (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new UnsafeFolderException($"Others may write the folder {full}, and so change what Blender runs; make it the user's alone (chmod 700).");
        if (Path.GetDirectoryName(full) is { } parent && IsOpenToAll(parent))
            throw new UnsafeFolderException($"Anyone may write the folder {parent}, and so swap {full} for a folder of theirs; keep renders in a folder that is not.");
        return full;
    }

    /// <summary>
    /// Why the file at <paramref name="path"/> (a secret, such as a node token; named
    /// <paramref name="what"/> in the answer) may be read or changed by someone other than the
    /// user; null when it may not. On Linux and macOS it must
    /// be the user's, no link, with no permission for its group or for others, in a folder that
    /// not everyone may write. On Windows its access list may let none but the user, the system
    /// and the administrators read it or change it.
    /// </summary>
    public static string? SecretFileProblem(string path, string what = "the file")
    {
        var info = new FileInfo(path);
        if (!info.Exists) return $"{what} is not there";
        if (info.LinkTarget is not null) return $"{what} is a link";
        if (OperatingSystem.IsWindows()) return WindowsSecretProblem(path, what);
        var mode = info.UnixFileMode;
        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                     | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            return $"others may read {what}; make it readable by its owner only (chmod 600)";
        if (!IsOwn(info.FullName, mode)) return $"{what} belongs to another user";
        if (Path.GetDirectoryName(info.FullName) is { } parent && IsOpenToAll(parent))
            return $"anyone may write the folder {what} is in ({parent}), and so swap it; keep it in a folder that is not";
        return null;
    }

    /// <summary>The largest render queue file moved from where it was kept before.</summary>
    private const long MaxQueueFileBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Moves the render queue's file from the folder it was kept in before
    /// (<paramref name="oldRoot"/>, in the shared temp folder) into <paramref name="newRoot"/>
    /// once, so that the renders it had not ended go on: only when there is none in the new place
    /// yet, and only a file that is the user's own, no link, and that others may not write (in a
    /// shared folder anyone else could have put it there). Every path in it under the old root is
    /// put under the new one; the frames that lay there are not taken along (another user could
    /// have changed them), so those renders start again. Returns whether it was moved.
    /// </summary>
    public static bool MoveQueueFile(string oldRoot, string newRoot, string fileName)
    {
        string from = Path.Combine(oldRoot, fileName), to = Path.Combine(newRoot, fileName);
        var info = new FileInfo(from);
        if (File.Exists(to) || !info.Exists || info.LinkTarget is not null || info.Length > MaxQueueFileBytes) return false;
        if (!OperatingSystem.IsWindows())
        {
            var mode = info.UnixFileMode;
            if ((mode & UnixFileMode.OtherWrite) != 0 || !IsOwn(from, mode)) return false;
        }
        System.Text.Json.Nodes.JsonNode? queue;
        try { queue = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(from)); }
        catch (System.Text.Json.JsonException) { return false; }
        if (queue is null) return false;
        string oldFull = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar), newFull = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar);
        Repoint(queue, oldFull, newFull);
        string temporary = to + ".moving";
        File.WriteAllText(temporary, queue.ToJsonString());
        File.Move(temporary, to, overwrite: false);
        try { File.Delete(from); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }

    private static void Repoint(System.Text.Json.Nodes.JsonNode node, string from, string to)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? text) && Under(text, from, comparison))
                        obj[key] = to + text[from.Length..];
                    else if (value is not null) Repoint(value, from, to);
                }
                break;
            case System.Text.Json.Nodes.JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? text) && Under(text, from, comparison))
                        array[i] = to + text[from.Length..];
                    else if (array[i] is { } item) Repoint(item, from, to);
                }
                break;
        }
    }

    private static bool Under(string path, string root, StringComparison comparison)
        => path.StartsWith(root, comparison) && (path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar);

    /// <summary>Only the owner of a file (or the superuser) may change its mode: setting it to
    /// <paramref name="mode"/> tells whether the user owns it.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool IsOwn(string path, UnixFileMode mode)
    {
        try
        {
            File.SetUnixFileMode(path, mode);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>A folder anyone may write in, without the sticky bit that keeps each to their own.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool IsOpenToAll(string folder)
    {
        try
        {
            var mode = File.GetUnixFileMode(folder);
            return (mode & UnixFileMode.OtherWrite) != 0 && (mode & UnixFileMode.StickyBit) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? WindowsSecretProblem(string path, string what)
    {
        var security = new FileInfo(path).GetAccessControl();
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        var trusted = new HashSet<System.Security.Principal.SecurityIdentifier>
        {
            new(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
            new(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        if (user is not null) trusted.Add(user);
        const System.Security.AccessControl.FileSystemRights Reach =
            System.Security.AccessControl.FileSystemRights.ReadData | System.Security.AccessControl.FileSystemRights.WriteData
            | System.Security.AccessControl.FileSystemRights.AppendData | System.Security.AccessControl.FileSystemRights.ChangePermissions
            | System.Security.AccessControl.FileSystemRights.TakeOwnership;
        foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                 security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier)))
        {
            if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow) continue;
            if ((rule.PropagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) != 0) continue;
            if (rule.IdentityReference is System.Security.Principal.SecurityIdentifier sid && trusted.Contains(sid)) continue;
            if ((rule.FileSystemRights & Reach) != 0)
                return $"others may read {what} ({rule.IdentityReference.Value} may); let only you read it (its Properties, Security; or icacls \"{path}\" /inheritance:r /grant:r \"%USERNAME%:R\")";
        }
        return null;
    }
}
