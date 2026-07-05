using System.Text;
using SkiaSharp;

// Renders the approved "Compass (wide bold)" ThIDE icon at every platform size and packs
// Windows .ico, macOS .icns and a Linux PNG set. The design mirrors
// build/icons/candidates/icon-5-compass.svg exactly (512-unit space), with the wordmark
// rasterized from the real font so the output carries no font dependency.
//
// Usage:  dotnet run            (from build/icons/tool, or pass the repo root as arg 0)

static string FindRepoRoot(string[] args)
{
    if (args.Length > 0 && Directory.Exists(args[0])) return args[0];
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException(
        "Could not locate ThIDE.sln above the working directory; pass the repo root as an argument.");
}

string repo = FindRepoRoot(args);
string iconsDir = Path.Combine(repo, "build", "icons");
string pngDir = Path.Combine(iconsDir, "png");
string assetsDir = Path.Combine(repo, "ThIDE", "Assets");
Directory.CreateDirectory(pngDir);

static SKColor Hex(uint rgb, byte a = 255) =>
    new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);

void Draw(SKCanvas canvas, int size)
{
    canvas.Clear(SKColors.Transparent);
    float s = size / 512f;
    canvas.Scale(s);

    using var clip = new SKRoundRect(new SKRect(0, 0, 512, 512), 100, 100);
    canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);

    // background gradient
    using (var bg = new SKPaint { IsAntialias = true })
    {
        bg.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, 512),
            new[] { Hex(0x16202f), Hex(0x0d1220) }, null, SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, 512, 512, bg);
    }

    // ---- compass (center 256,150 r108) ----
    using var dial = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Hex(0xcfd8e6), StrokeWidth = 10, StrokeCap = SKStrokeCap.Round };
    canvas.DrawCircle(256, 150, 108, dial);

    // cardinal ticks
    dial.StrokeWidth = 10;
    canvas.DrawLine(256, 42, 256, 60, dial);
    canvas.DrawLine(256, 258, 256, 240, dial);
    canvas.DrawLine(148, 150, 166, 150, dial);
    canvas.DrawLine(364, 150, 346, 150, dial);

    // intercardinal ticks
    using var tick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Hex(0xcfd8e6, 178), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
    canvas.DrawLine(332.4f, 73.6f, 325.3f, 80.7f, tick);
    canvas.DrawLine(332.4f, 226.4f, 325.3f, 219.3f, tick);
    canvas.DrawLine(179.6f, 226.4f, 186.7f, 219.3f, tick);
    canvas.DrawLine(179.6f, 73.6f, 186.7f, 80.7f, tick);

    // glowing cave arch at the foot of the dial
    using (var archDark = new SKPaint { IsAntialias = true, Color = Hex(0x0a0e18) })
    using (var archPath = new SKPath())
    {
        archPath.MoveTo(195, 250); archPath.QuadTo(256, 200, 317, 250); archPath.Close();
        canvas.DrawPath(archPath, archDark);
    }
    using (var glow = new SKPaint { IsAntialias = true, Color = Hex(0xffb347) })
    using (var glowPath = new SKPath())
    {
        glowPath.MoveTo(236, 250); glowPath.QuadTo(256, 224, 276, 250); glowPath.Close();
        canvas.DrawPath(glowPath, glow);
    }

    // needle (rotated 38 deg about the hub)
    canvas.Save();
    canvas.RotateDegrees(38, 256, 150);
    using (var red = new SKPaint { IsAntialias = true, Color = Hex(0xff5b4d) })
    using (var redP = new SKPath())
    {
        redP.MoveTo(256, 62); redP.LineTo(240, 150); redP.LineTo(272, 150); redP.Close();
        canvas.DrawPath(redP, red);
    }
    using (var white = new SKPaint { IsAntialias = true, Color = Hex(0xe8edf5) })
    using (var whiteP = new SKPath())
    {
        whiteP.MoveTo(256, 238); whiteP.LineTo(240, 150); whiteP.LineTo(272, 150); whiteP.Close();
        canvas.DrawPath(whiteP, white);
    }
    canvas.Restore();

    // hub
    using (var hubFill = new SKPaint { IsAntialias = true, Color = Hex(0x16202f) })
        canvas.DrawCircle(256, 150, 12, hubFill);
    using (var hubStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Hex(0xcfd8e6), StrokeWidth = 6 })
        canvas.DrawCircle(256, 150, 12, hubStroke);

    // ---- wordmark: translate(256,396) scale(1.05,1), Segoe UI Bold + fattening stroke ----
    canvas.Save();
    canvas.Translate(256, 396);
    canvas.Scale(1.05f, 1f);
    using var tf = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    using var font = new SKFont(tf, 150) { Edging = SKFontEdging.Antialias, Subpixel = true };
    const string th = "Th", ide = "IDE", all = "ThIDE";
    float wAll = font.MeasureText(all);
    float wTh = font.MeasureText(th);
    float startX = -wAll / 2f;
    using var pTh = new SKPaint { IsAntialias = true, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 6, StrokeJoin = SKStrokeJoin.Round, Color = Hex(0xff5b4d) };
    using var pIde = new SKPaint { IsAntialias = true, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 6, StrokeJoin = SKStrokeJoin.Round, Color = Hex(0xeef2f7) };
    canvas.DrawText(th, startX, 0, font, pTh);
    canvas.DrawText(ide, startX + wTh, 0, font, pIde);
    canvas.Restore();
}

SKBitmap Render(int size)
{
    var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    var bmp = new SKBitmap(info);
    using (var canvas = new SKCanvas(bmp))
        Draw(canvas, size);
    return bmp;
}

byte[] Png(SKBitmap bmp)
{
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// 32bpp BMP (DIB) frame for an ICO: BITMAPINFOHEADER + bottom-up BGRA + empty AND mask.
byte[] BmpFrame(SKBitmap bmp)
{
    int n = bmp.Width;
    byte[] src = bmp.Bytes;              // top-down BGRA rows, stride n*4
    int stride = n * 4;
    int maskStride = ((n + 31) / 32) * 4;
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40);            // biSize
    w.Write(n);             // biWidth
    w.Write(n * 2);         // biHeight (color + mask)
    w.Write((short)1);      // biPlanes
    w.Write((short)32);     // biBitCount
    w.Write(0);             // biCompression BI_RGB
    w.Write(0);             // biSizeImage
    w.Write(0); w.Write(0); // pels per meter
    w.Write(0); w.Write(0); // clrUsed / clrImportant
    for (int y = n - 1; y >= 0; y--)    // color, bottom-up
        w.Write(src, y * stride, stride);
    byte[] maskRow = new byte[maskStride];
    for (int y = 0; y < n; y++)         // AND mask, all zero
        w.Write(maskRow, 0, maskStride);
    return ms.ToArray();
}

void WriteIco(string path, int[] sizes, Dictionary<int, SKBitmap> bmps)
{
    var frames = new List<(int size, bool png, byte[] data)>();
    foreach (int sz in sizes)
        frames.Add((sz, sz >= 256, sz >= 256 ? Png(bmps[sz]) : BmpFrame(bmps[sz])));

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count); // ICONDIR
    int offset = 6 + 16 * frames.Count;
    foreach (var f in frames)
    {
        w.Write((byte)(f.size >= 256 ? 0 : f.size));
        w.Write((byte)(f.size >= 256 ? 0 : f.size));
        w.Write((byte)0);   // colors
        w.Write((byte)0);   // reserved
        w.Write((short)1);  // planes
        w.Write((short)32); // bpp
        w.Write(f.data.Length);
        w.Write(offset);
        offset += f.data.Length;
    }
    foreach (var f in frames) w.Write(f.data);
    File.WriteAllBytes(path, ms.ToArray());
}

void WriteIcns(string path, (string type, int size)[] entries, Dictionary<int, SKBitmap> bmps)
{
    var blobs = new List<(byte[] type, byte[] png)>();
    foreach (var (type, size) in entries)
        blobs.Add((Encoding.ASCII.GetBytes(type), Png(bmps[size])));

    int total = 8 + blobs.Sum(b => 8 + b.png.Length);
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("icns"));
    void BE(int v) { ms.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }); }
    BE(total);
    foreach (var (type, png) in blobs)
    {
        ms.Write(type);
        BE(8 + png.Length);
        ms.Write(png);
    }
    File.WriteAllBytes(path, ms.ToArray());
}

// ---- render every size once, reuse ----
int[] all_sizes = { 16, 24, 32, 48, 64, 128, 256, 512, 1024 };
var bitmaps = new Dictionary<int, SKBitmap>();
foreach (int sz in all_sizes) bitmaps[sz] = Render(sz);

// Linux / generic PNG set
foreach (int sz in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
    File.WriteAllBytes(Path.Combine(pngDir, $"thide-{sz}.png"), Png(bitmaps[sz]));

// Windows .ico (canonical app icon) + a copy under build/icons
int[] icoSizes = { 16, 24, 32, 48, 64, 128, 256 };
WriteIco(Path.Combine(assetsDir, "thide.ico"), icoSizes, bitmaps);
WriteIco(Path.Combine(iconsDir, "thide.ico"), icoSizes, bitmaps);

// macOS .icns
(string, int)[] icnsEntries =
{
    ("icp4", 16), ("icp5", 32), ("icp6", 64),
    ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
    ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
};
WriteIcns(Path.Combine(iconsDir, "thide.icns"), icnsEntries, bitmaps);

foreach (var b in bitmaps.Values) b.Dispose();

Console.WriteLine($"repo: {repo}");
Console.WriteLine("Generated:");
Console.WriteLine("  ThIDE/Assets/thide.ico");
Console.WriteLine("  build/icons/thide.ico");
Console.WriteLine("  build/icons/thide.icns");
Console.WriteLine("  build/icons/png/thide-{16..512}.png");
