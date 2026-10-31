// What a stand-alone model page shows: its title and text, how the model is first displayed,
// the places it offers, and how it is packed.

namespace Therion.CaveView;

/// <summary>The model file a page shows: its file name (the extension picks CaveView's reader) and its bytes.</summary>
public sealed record ModelPageModel(string FileName, Func<Stream> Open)
{
    /// <summary>A model read from a file on disk.</summary>
    public static ModelPageModel FromFile(string path) =>
        new(Path.GetFileName(path), () => File.OpenRead(path));
}

/// <summary>A station the page lists, which flies the camera to it when chosen.</summary>
/// <param name="Label">Text of the entry (the station's name when empty).</param>
/// <param name="Station">The station's full dotted path in the model (<c>survey.sub.point</c>).</param>
/// <param name="Note">A line of text under the entry (optional).</param>
public sealed record ModelPagePlace(string Label, string Station, string? Note = null)
{
    /// <summary>
    /// The path's names one by one, for a station whose name has a dot of its own (<c>N32.11</c>),
    /// which the dotted <see cref="Station"/> cannot tell from a survey boundary. Optional.
    /// </summary>
    public IReadOnlyList<string>? StationParts { get; init; }
}

/// <summary>How the model is displayed when the page opens. The reader can change all of it from the toolbar.</summary>
public sealed record ModelPageView
{
    /// <summary>Colour by: <c>height</c>, <c>survey</c>, <c>length</c>, <c>inclination</c> or <c>single</c>.</summary>
    public string Shading { get; init; } = "height";
    public bool Walls { get; init; } = true;
    public bool Splays { get; init; }
    public bool Stations { get; init; }
    public bool StationLabels { get; init; }
    /// <summary>Entrance names.</summary>
    public bool Entrances { get; init; } = true;
    public bool BoundingBox { get; init; }
    /// <summary><c>none</c> (CaveView's default), <c>plan</c>, or a profile: <c>north</c>, <c>east</c>, <c>south</c>, <c>west</c>.</summary>
    public string Orientation { get; init; } = "none";
    public bool Orthographic { get; init; }
    public bool AutoRotate { get; init; }
}

public sealed record ModelPageOptions
{
    public required string Title { get; init; }
    /// <summary>Plain text under the title; line breaks are kept.</summary>
    public string? Description { get; init; }
    /// <summary>Language of the page and of the viewer's controls: one of <see cref="CaveViewRuntime.Languages"/>.</summary>
    public string Language { get; init; } = "en";
    public ModelPageView View { get; init; } = new();
    public IReadOnlyList<ModelPagePlace> Places { get; init; } = Array.Empty<ModelPagePlace>();
    /// <summary>Show the viewer's toolbar (views, colour, layers, rotation, full screen).</summary>
    public bool Toolbar { get; init; } = true;
    /// <summary>Whether going to a place zooms in on it (the reader can switch it beside the list).</summary>
    public bool CloseUp { get; init; } = true;
    /// <summary>Width of survey, in metres, a close-up takes in.</summary>
    public double CloseUpSpan { get; init; } = 30;
    /// <summary>
    /// Carry the model inside a script (<c>model/model.js</c>, base64) instead of as its own file,
    /// so the page also works opened straight from a disk, where a browser refuses to fetch a
    /// file beside the page. About a third larger; a page on a web server does not need it.
    /// </summary>
    public bool EmbedModel { get; init; }
    /// <summary>Named in the footer and in the page's generator tag, e.g. <c>ThIDE 1.4</c>.</summary>
    public string? Generator { get; init; }
}
