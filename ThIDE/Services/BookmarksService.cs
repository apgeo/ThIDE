using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThIDE.Services;

public record BookmarkEntry(string FilePath, int Line, string Title, DateTime CreatedAt)
{
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? $"{FileName}:{Line}"
        : Title;
}

public interface IBookmarksService
{
    IReadOnlyList<BookmarkEntry> Bookmarks { get; }
    event EventHandler? BookmarksChanged;
    void AddBookmark(string filePath, int line, string title);
    void RemoveBookmark(BookmarkEntry entry);
    bool HasBookmark(string filePath, int line);
}

public sealed class BookmarksService : IBookmarksService
{
    private readonly List<BookmarkEntry> _bookmarks = new();
    private readonly string _persistPath;

    public IReadOnlyList<BookmarkEntry> Bookmarks => _bookmarks;
    public event EventHandler? BookmarksChanged;

    public BookmarksService()
    {
        _persistPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ThIDE", "bookmarks.json");
        Load();
    }

    public void AddBookmark(string filePath, int line, string title)
    {
        _bookmarks.Add(new BookmarkEntry(filePath, line, title.Trim(), DateTime.Now));
        Save();
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveBookmark(BookmarkEntry entry)
    {
        if (_bookmarks.Remove(entry))
        {
            Save();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasBookmark(string filePath, int line) =>
        _bookmarks.Any(b =>
            string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            b.Line == line);

    private void Load()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;
            var json = File.ReadAllText(_persistPath);
            var dtos = JsonSerializer.Deserialize<List<BookmarkDto>>(json);
            if (dtos is null) return;
            foreach (var d in dtos)
                _bookmarks.Add(new BookmarkEntry(d.FilePath, d.Line, d.Title ?? string.Empty, d.CreatedAt));
        }
        catch { /* corrupt/missing file — start fresh */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_persistPath)!);
            var dtos = _bookmarks.Select(b => new BookmarkDto
            {
                FilePath = b.FilePath,
                Line = b.Line,
                Title = b.Title,
                CreatedAt = b.CreatedAt,
            }).ToList();
            File.WriteAllText(_persistPath, JsonSerializer.Serialize(dtos,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed class BookmarkDto
    {
        [JsonPropertyName("filePath")] public string FilePath { get; set; } = string.Empty;
        [JsonPropertyName("line")] public int Line { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
