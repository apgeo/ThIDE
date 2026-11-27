// PLY reader: ascii, binary_little_endian and binary_big_endian, every scalar type. Reads the
// vertex positions, optional normals, colours, texture coordinates (s/t) and the reveal value
// ThIDE's own writer adds, and the face list (polygons fan-triangulated). Unknown elements and
// properties, lists included, are skipped. No faces means a point cloud.

using System.Buffers.Binary;
using System.Globalization;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

/// <summary>Reads PLY files into a <see cref="CaveMesh"/> or a <see cref="CavePointCloud"/>.</summary>
public static class PlyReader
{
    private enum PlyType { I8, U8, I16, U16, I32, U32, F32, F64 }

    private sealed class Prop
    {
        public required string Name;
        public required PlyType Type;
        public bool IsList;
        public PlyType CountType;
    }

    private sealed class Element
    {
        public required string Name;
        public required long Count;
        public List<Prop> Props = new();
    }

    private static int SizeOf(PlyType t) => t switch
    {
        PlyType.I8 or PlyType.U8 => 1,
        PlyType.I16 or PlyType.U16 => 2,
        PlyType.I32 or PlyType.U32 or PlyType.F32 => 4,
        _ => 8,
    };

    private static PlyType ParseType(string name, int lineNo) => name switch
    {
        "char" or "int8" => PlyType.I8,
        "uchar" or "uint8" => PlyType.U8,
        "short" or "int16" => PlyType.I16,
        "ushort" or "uint16" => PlyType.U16,
        "int" or "int32" => PlyType.I32,
        "uint" or "uint32" => PlyType.U32,
        "float" or "float32" => PlyType.F32,
        "double" or "float64" => PlyType.F64,
        _ => throw new CaveFileFormatException($"PLY: header line {lineNo}: unknown property type '{name}'."),
    };

    /// <summary>Reads a PLY from the stream's current position to its end.</summary>
    public static ModelFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new ByteReader(stream);
        var elements = ReadHeader(reader, out bool ascii, out bool bigEndian);
        var body = new Body(reader, ascii, bigEndian);
        var warnings = new List<string>();

        List<CaveVector3>? points = null;
        List<CaveVector3>? normals = null;
        List<CaveColor>? colors = null;
        List<CaveUv>? uvs = null;
        List<double>? reveal = null;
        var triangles = new List<CaveTriangle>();
        long maxIndex = -1;
        long skippedFaces = 0;
        bool sawFaceElement = false;

        foreach (var el in elements)
        {
            if (el.Count == 0) continue;
            if (el.Props.Count == 0) continue;
            GuardCount(el, reader, ascii);
            try
            {
                if (el.Name == "vertex" && points is null)
                    ReadVertices(body, el, out points, out normals, out colors, out uvs, out reveal);
                else if (el.Name == "face" && FindFaceProp(el) >= 0)
                {
                    sawFaceElement = true;
                    ReadFaces(body, el, triangles, ref maxIndex, ref skippedFaces);
                }
                else
                {
                    for (long i = 0; i < el.Count; i++)
                        foreach (var p in el.Props) body.Skip(p);
                    warnings.Add($"ignored {el.Count} {el.Name} elements");
                }
            }
            catch (EndOfStreamException)
            {
                throw new CaveFileFormatException($"PLY: truncated inside element '{el.Name}' (at byte {reader.Offset}).");
            }
        }

        if (points is null)
            throw new CaveFileFormatException("PLY: no vertex element with x, y, z properties.");
        if (maxIndex >= points.Count)
            throw new CaveFileFormatException($"PLY: a face refers to vertex {maxIndex} but there are only {points.Count} vertices.");
        if (skippedFaces > 0)
            warnings.Add($"skipped {skippedFaces} degenerate faces");

        if (!sawFaceElement || triangles.Count == 0 && skippedFaces == 0)
        {
            return new ModelFile
            {
                Format = ModelFileFormat.Ply,
                Points = new CavePointCloud { Points = points, Normals = normals, Colors = colors },
                Warnings = warnings,
            };
        }
        return new ModelFile
        {
            Format = ModelFileFormat.Ply,
            Mesh = new CaveMesh { Vertices = points, Triangles = triangles, VertexColors = colors, TextureCoordinates = uvs, Reveal = reveal },
            Warnings = warnings,
        };
    }

    private static int FindFaceProp(Element el)
        => el.Props.FindIndex(p => p.IsList && (p.Name == "vertex_indices" || p.Name == "vertex_index"));

    // A count that the remaining bytes cannot hold is garbled; refuse it before reading anything.
    private static void GuardCount(Element el, ByteReader reader, bool ascii)
    {
        long minPerItem = 0;
        foreach (var p in el.Props)
            minPerItem += ascii ? 2 : SizeOf(p.IsList ? p.CountType : p.Type);
        if (ascii) minPerItem--;
        if (minPerItem < 1) minPerItem = 1;
        long remaining = reader.Remaining;
        if (el.Count > remaining / minPerItem)
            throw new CaveFileFormatException(
                $"PLY: element '{el.Name}' declares {el.Count} entries but only {remaining} bytes of data follow.");
    }

    private static List<Element> ReadHeader(ByteReader r, out bool ascii, out bool bigEndian)
    {
        ascii = false;
        bigEndian = false;
        string? line = r.ReadLine(4096);
        if (line is null || line.Trim() != "ply")
            throw new CaveFileFormatException("PLY: file does not start with \"ply\".");
        var elements = new List<Element>();
        bool haveFormat = false;
        for (int lineNo = 2; lineNo < 100000; lineNo++)
        {
            line = r.ReadLine(4096);
            if (line is null) throw new CaveFileFormatException("PLY: header ends without end_header.");
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            switch (t[0])
            {
                case "comment":
                case "obj_info":
                    break;
                case "format":
                    if (t.Length < 2) throw new CaveFileFormatException($"PLY: header line {lineNo}: malformed format line.");
                    haveFormat = true;
                    switch (t[1])
                    {
                        case "ascii": ascii = true; break;
                        case "binary_little_endian": break;
                        case "binary_big_endian": bigEndian = true; break;
                        default: throw new CaveFileFormatException($"PLY: unknown format '{t[1]}'.");
                    }
                    break;
                case "element":
                    if (t.Length < 3 || !long.TryParse(t[2], NumberStyles.None, CultureInfo.InvariantCulture, out long count))
                        throw new CaveFileFormatException($"PLY: header line {lineNo}: malformed element line.");
                    elements.Add(new Element { Name = t[1], Count = count });
                    break;
                case "property":
                    if (elements.Count == 0) throw new CaveFileFormatException($"PLY: header line {lineNo}: property before any element.");
                    if (t.Length >= 5 && t[1] == "list")
                        elements[^1].Props.Add(new Prop { Name = t[4], IsList = true, CountType = ParseType(t[2], lineNo), Type = ParseType(t[3], lineNo) });
                    else if (t.Length >= 3)
                        elements[^1].Props.Add(new Prop { Name = t[2], Type = ParseType(t[1], lineNo) });
                    else
                        throw new CaveFileFormatException($"PLY: header line {lineNo}: malformed property line.");
                    break;
                case "end_header":
                    if (!haveFormat) throw new CaveFileFormatException("PLY: header has no format line.");
                    return elements;
                default:
                    break; // unknown header keyword
            }
        }
        throw new CaveFileFormatException("PLY: header is too long.");
    }

    private static void ReadVertices(Body body, Element el,
        out List<CaveVector3>? points, out List<CaveVector3>? normals, out List<CaveColor>? colors,
        out List<CaveUv>? uvs, out List<double>? reveal)
    {
        int ix = -1, iy = -1, iz = -1, inx = -1, iny = -1, inz = -1, ir = -1, ig = -1, ib = -1, iu = -1, iv = -1, irev = -1;
        for (int i = 0; i < el.Props.Count; i++)
        {
            if (el.Props[i].IsList) continue;
            switch (el.Props[i].Name)
            {
                case "x": ix = i; break;
                case "y": iy = i; break;
                case "z": iz = i; break;
                case "nx": inx = i; break;
                case "ny": iny = i; break;
                case "nz": inz = i; break;
                case "red" or "diffuse_red": ir = i; break;
                case "green" or "diffuse_green": ig = i; break;
                case "blue" or "diffuse_blue": ib = i; break;
                case "s" or "u" or "texture_u" or "texture_s": if (iu < 0) iu = i; break;
                case "t" or "v" or "texture_v" or "texture_t": if (iv < 0) iv = i; break;
                case "reveal": irev = i; break;
            }
        }
        if (ix < 0 || iy < 0 || iz < 0)
            throw new CaveFileFormatException("PLY: vertex element lacks x, y or z.");
        bool hasN = inx >= 0 && iny >= 0 && inz >= 0;
        bool hasC = ir >= 0 && ig >= 0 && ib >= 0;
        bool hasUv = iu >= 0 && iv >= 0;
        bool hasRev = irev >= 0;

        int n = checked((int)el.Count);
        var pts = new List<CaveVector3>(n);
        normals = hasN ? new List<CaveVector3>(n) : null;
        colors = hasC ? new List<CaveColor>(n) : null;
        uvs = hasUv ? new List<CaveUv>(n) : null;
        reveal = hasRev ? new List<double>(n) : null;
        var values = new double[el.Props.Count];
        for (int k = 0; k < n; k++)
        {
            for (int i = 0; i < el.Props.Count; i++)
            {
                if (el.Props[i].IsList) body.Skip(el.Props[i]);
                else values[i] = body.Scalar(el.Props[i].Type);
            }
            pts.Add(new CaveVector3(values[ix], values[iy], values[iz]));
            normals?.Add(new CaveVector3(values[inx], values[iny], values[inz]));
            colors?.Add(new CaveColor(ToByte(values[ir]), ToByte(values[ig]), ToByte(values[ib])));
            uvs?.Add(new CaveUv(values[iu], values[iv]));
            reveal?.Add(values[irev]);
        }
        points = pts;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    private static void ReadFaces(Body body, Element el, List<CaveTriangle> triangles, ref long maxIndex, ref long skipped)
    {
        int faceProp = FindFaceProp(el);
        var idx = new List<uint>();
        for (long f = 0; f < el.Count; f++)
        {
            for (int pi = 0; pi < el.Props.Count; pi++)
            {
                var p = el.Props[pi];
                if (pi != faceProp) { body.Skip(p); continue; }
                long k = (long)body.Scalar(p.CountType);
                body.GuardList(k, p.Type);
                idx.Clear();
                for (long j = 0; j < k; j++)
                {
                    double v = body.Scalar(p.Type);
                    if (v < 0 || v > uint.MaxValue)
                        throw new CaveFileFormatException($"PLY: face {f} has index {v}, which is not a vertex number.");
                    idx.Add((uint)v);
                    if (v > maxIndex) maxIndex = (long)v;
                }
                if (idx.Count < 3) { skipped++; continue; }
                for (int j = 1; j + 1 < idx.Count; j++)
                    triangles.Add(new CaveTriangle(idx[0], idx[j], idx[j + 1]));
            }
        }
    }

    // Reads values of the body in the file's encoding.
    private sealed class Body(ByteReader reader, bool ascii, bool bigEndian)
    {
        private readonly byte[] _scratch = new byte[8];

        public double Scalar(PlyType type)
        {
            if (ascii)
            {
                string? tok = reader.ReadToken();
                if (tok is null) throw new EndOfStreamException();
                if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new CaveFileFormatException($"PLY: '{tok}' is not a number (at byte {reader.Offset}).");
                return d;
            }
            int size = SizeOf(type);
            var s = _scratch.AsSpan(0, size);
            reader.ReadExactly(s);
            if (bigEndian && size > 1) s.Reverse();
            return type switch
            {
                PlyType.I8 => (sbyte)s[0],
                PlyType.U8 => s[0],
                PlyType.I16 => BinaryPrimitives.ReadInt16LittleEndian(s),
                PlyType.U16 => BinaryPrimitives.ReadUInt16LittleEndian(s),
                PlyType.I32 => BinaryPrimitives.ReadInt32LittleEndian(s),
                PlyType.U32 => BinaryPrimitives.ReadUInt32LittleEndian(s),
                PlyType.F32 => BinaryPrimitives.ReadSingleLittleEndian(s),
                _ => BinaryPrimitives.ReadDoubleLittleEndian(s),
            };
        }

        public void GuardList(long count, PlyType itemType)
        {
            long per = ascii ? 2 : SizeOf(itemType);
            if (count < 0 || count > reader.Remaining / per + 1)
                throw new CaveFileFormatException($"PLY: a list of {count} entries cannot fit in the {reader.Remaining} bytes that remain (at byte {reader.Offset}).");
        }

        public void Skip(Prop p)
        {
            if (!p.IsList) { Scalar(p.Type); return; }
            long k = (long)Scalar(p.CountType);
            GuardList(k, p.Type);
            if (ascii) { for (long i = 0; i < k; i++) Scalar(p.Type); }
            else reader.Skip(k * SizeOf(p.Type));
        }
    }
}
