// Wavefront OBJ reader: v (with an optional w or r g b), f in all index forms (v, v/vt, v//vn,
// v/vt/vn, negative relative indices), polygons fan-triangulated, backslash line continuation and
// # comments. Texture coordinates, normals, groups, materials, lines and points are ignored.
// No faces means a point cloud.

using System.Globalization;
using System.Text;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

/// <summary>Reads OBJ files into a <see cref="CaveMesh"/> or a <see cref="CavePointCloud"/>.</summary>
public static class ObjReader
{
    /// <summary>Reads an OBJ from the stream's current position to its end.</summary>
    public static ModelFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var text = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16, leaveOpen: true);

        var points = new List<CaveVector3>();
        var colors = new List<CaveColor>();
        var triangles = new List<CaveTriangle>();
        var face = new List<uint>();
        long maxPositive = 0;
        int maxPositiveLine = 0;
        int ignoredLines = 0, ignoredPoints = 0, skippedFaces = 0;
        int lineNo = 0;

        string? raw;
        while ((raw = text.ReadLine()) != null)
        {
            lineNo++;
            string line = raw;
            while (line.EndsWith('\\'))
            {
                string? next = text.ReadLine();
                lineNo++;
                line = line[..^1] + " " + (next ?? "");
            }
            int hash = line.IndexOf('#');
            var s = (hash >= 0 ? line.AsSpan(0, hash) : line.AsSpan()).Trim();
            if (s.IsEmpty) continue;

            int pos = 0;
            var keyword = NextToken(s, ref pos);
            if (keyword.SequenceEqual("v"))
            {
                var nums = new double[6];
                int n = 0;
                while (n < 6)
                {
                    var tok = NextToken(s, ref pos);
                    if (tok.IsEmpty) break;
                    nums[n++] = ParseNumber(tok, lineNo);
                }
                if (n < 3) throw new CaveFileFormatException($"OBJ: line {lineNo}: a vertex needs x y z.");
                points.Add(new CaveVector3(nums[0], nums[1], nums[2]));
                if (n >= 6) colors.Add(new CaveColor(Channel(nums[3]), Channel(nums[4]), Channel(nums[5])));
            }
            else if (keyword.SequenceEqual("f"))
            {
                face.Clear();
                ReadOnlySpan<char> tok;
                while (!(tok = NextToken(s, ref pos)).IsEmpty)
                {
                    int slash = tok.IndexOf('/');
                    var idxText = slash >= 0 ? tok[..slash] : tok;
                    if (!long.TryParse(idxText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long idx) || idx == 0)
                        throw new CaveFileFormatException($"OBJ: line {lineNo}: bad face index '{idxText.ToString()}'.");
                    long resolved;
                    if (idx < 0)
                    {
                        resolved = points.Count + idx;
                        if (resolved < 0)
                            throw new CaveFileFormatException($"OBJ: line {lineNo}: relative face index {idx} reaches before the first of {points.Count} vertices.");
                    }
                    else
                    {
                        resolved = idx - 1;
                        if (idx > maxPositive) { maxPositive = idx; maxPositiveLine = lineNo; }
                    }
                    if (resolved > uint.MaxValue)
                        throw new CaveFileFormatException($"OBJ: line {lineNo}: face index {idx} is too large.");
                    face.Add((uint)resolved);
                }
                if (face.Count < 3) { skippedFaces++; continue; }
                for (int j = 1; j + 1 < face.Count; j++)
                    triangles.Add(new CaveTriangle(face[0], face[j], face[j + 1]));
            }
            else if (keyword.SequenceEqual("l")) ignoredLines++;
            else if (keyword.SequenceEqual("p")) ignoredPoints++;
        }

        if (maxPositive > points.Count)
            throw new CaveFileFormatException($"OBJ: line {maxPositiveLine}: face index {maxPositive} is out of range (the file has {points.Count} vertices).");
        foreach (var t in triangles)
        {
            if (t.A >= points.Count || t.B >= points.Count || t.C >= points.Count)
                throw new CaveFileFormatException($"OBJ: a face refers to a vertex beyond the {points.Count} defined.");
        }

        var warnings = new List<string>();
        if (ignoredLines > 0) warnings.Add($"ignored {ignoredLines} line elements");
        if (ignoredPoints > 0) warnings.Add($"ignored {ignoredPoints} point elements");
        if (skippedFaces > 0) warnings.Add($"skipped {skippedFaces} degenerate faces");
        List<CaveColor>? vertexColors = null;
        if (colors.Count == points.Count && points.Count > 0) vertexColors = colors;
        else if (colors.Count > 0) warnings.Add("ignored vertex colours given for only some vertices");

        if (triangles.Count == 0 && skippedFaces == 0)
        {
            return new ModelFile
            {
                Format = ModelFileFormat.Obj,
                Points = new CavePointCloud { Points = points, Colors = vertexColors },
                Warnings = warnings,
            };
        }
        return new ModelFile
        {
            Format = ModelFileFormat.Obj,
            Mesh = new CaveMesh { Vertices = points, Triangles = triangles, VertexColors = vertexColors },
            Warnings = warnings,
        };
    }

    private static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);

    private static double ParseNumber(ReadOnlySpan<char> s, int lineNo)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            throw new CaveFileFormatException($"OBJ: line {lineNo}: '{s.ToString()}' is not a number.");
        return d;
    }

    // The next run of non-blank characters from pos, or empty at the end.
    private static ReadOnlySpan<char> NextToken(ReadOnlySpan<char> s, scoped ref int pos)
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t')) pos++;
        int start = pos;
        while (pos < s.Length && s[pos] != ' ' && s[pos] != '\t') pos++;
        return s[start..pos];
    }
}
