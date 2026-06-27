// VIS-03/05 — in-app rasterization of generated map outputs to an Avalonia bitmap.
//   * PNG/JPG/BMP : native Avalonia Bitmap.
//   * SVG         : Svg.Skia core renders the picture → PNG bytes → Avalonia Bitmap.
//   * PDF         : Docnet.Core (bundled PDFium) renders a page → BGRA bytes → WriteableBitmap.
// All paths are defensive: a missing native lib or a malformed file yields an Error, never a crash,
// so the viewer can fall back to "open externally".

using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace TherionProc.Services;

/// <summary>The result of rendering one page of a map file.</summary>
public sealed record RenderedMap(Bitmap? Image, int PageCount, string? Error)
{
    public bool Ok => Image is not null;
}

public interface IMapRenderService
{
    /// <summary>True if the file extension is one we can rasterize in-app.</summary>
    bool CanRender(string path);
    /// <summary>Renders 0-based <paramref name="page"/> of the file at <paramref name="scale"/> (1 = native).</summary>
    RenderedMap Render(string path, int page, double scale);
}

public sealed class MapRenderService : IMapRenderService
{
    public bool CanRender(string path) => Kind(path) != MapKind.Unknown;

    public RenderedMap Render(string path, int page, double scale)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new RenderedMap(null, 0, "File not found.");
        scale = Math.Clamp(scale, 0.1, 8.0);
        try
        {
            return Kind(path) switch
            {
                MapKind.Raster => new RenderedMap(new Bitmap(path), 1, null),
                MapKind.Svg => RenderSvg(path),
                MapKind.Pdf => RenderPdf(path, page, scale),
                _ => new RenderedMap(null, 0, "Unsupported format."),
            };
        }
        catch (Exception ex)
        {
            return new RenderedMap(null, 0, ex.Message);
        }
    }

    // ---- SVG (Svg.Skia core) ----------------------------------------------------------------

    private static RenderedMap RenderSvg(string path)
    {
        using var svg = new Svg.Skia.SKSvg();
        svg.Load(path);
        if (svg.Picture is not { } picture) return new RenderedMap(null, 1, "Could not parse the SVG.");

        var rect = picture.CullRect;
        int w = Math.Max(1, (int)Math.Ceiling(rect.Width));
        int h = Math.Max(1, (int)Math.Ceiling(rect.Height));
        // Cap the raster so a huge map doesn't allocate gigabytes; the viewer zooms it up.
        const int maxDim = 4000;
        float fit = Math.Min(1f, maxDim / (float)Math.Max(w, h));
        using var surface = SKSurface.Create(new SKImageInfo((int)(w * fit), (int)(h * fit)));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(fit);
        canvas.DrawPicture(picture);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new RenderedMap(new Bitmap(ms), 1, null);
    }

    // ---- PDF (Docnet.Core / PDFium) ---------------------------------------------------------

    private static RenderedMap RenderPdf(string path, int page, double scale)
    {
        var bytes = File.ReadAllBytes(path);
        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(scale * 1.4));
        int pageCount = docReader.GetPageCount();
        if (pageCount == 0) return new RenderedMap(null, 0, "The PDF has no pages.");
        page = Math.Clamp(page, 0, pageCount - 1);

        using var pageReader = docReader.GetPageReader(page);
        int w = pageReader.GetPageWidth();
        int h = pageReader.GetPageHeight();
        var raw = pageReader.GetImage(); // BGRA, length w*h*4
        if (raw.Length < w * h * 4) return new RenderedMap(null, pageCount, "Empty PDF render.");

        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            // PDFium gives a transparent background; paint white behind so maps read on dark themes.
            for (int i = 0; i + 3 < raw.Length; i += 4)
            {
                byte a = raw[i + 3];
                if (a == 255) continue;
                int inv = 255 - a;
                raw[i] = (byte)(raw[i] + inv);       // B
                raw[i + 1] = (byte)(raw[i + 1] + inv); // G
                raw[i + 2] = (byte)(raw[i + 2] + inv); // R
                raw[i + 3] = 255;
            }
            Marshal.Copy(raw, 0, fb.Address, raw.Length);
        }
        return new RenderedMap(wb, pageCount, null);
    }

    private enum MapKind { Unknown, Raster, Svg, Pdf }

    private static MapKind Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => MapKind.Raster,
        ".svg" => MapKind.Svg,
        ".pdf" => MapKind.Pdf,
        _ => MapKind.Unknown,
    };
}
