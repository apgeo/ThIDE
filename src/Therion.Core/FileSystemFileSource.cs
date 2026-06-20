// Implementation Plan §6 (Workspace I/O). Filesystem-backed IFileSource.

namespace Therion.Core;

/// <summary>Default <see cref="IFileSource"/> backed by the OS filesystem.</summary>
public sealed class FileSystemFileSource : IFileSource
{
    private readonly FileInfo _info;

    public FileSystemFileSource(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _info = new FileInfo(Path);
    }

    public string Path { get; }

    public DateTime LastWriteUtc => _info.Exists ? _info.LastWriteTimeUtc : DateTime.MinValue;

    public long Length => _info.Exists ? _info.Length : 0;

    public async ValueTask<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
    {
        // Therion files default to UTF-8 unless an `encoding` directive says otherwise
        // (Implementation Plan §3 lexical rules). The two-phase encoding handling
        // will live in the lexer; here we read raw UTF-8.
        return await File.ReadAllTextAsync(Path, System.Text.Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }
}
