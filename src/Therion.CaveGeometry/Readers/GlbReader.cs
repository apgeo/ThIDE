// Binary glTF 2.0 (.glb) reader. Walks the default scene's node hierarchy applying each node's
// matrix or translation/rotation/scale, and gathers every triangle primitive (triangles, strips,
// fans; indexed or not) into one mesh with its vertices in the file's own (Y up, metre)
// coordinates. Points primitives become a point cloud only when the file has no triangles.
// Compressed or quantised geometry (Draco, meshopt, KHR_mesh_quantization) is refused by name.

using System.Buffers.Binary;
using System.Text.Json;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.CaveGeometry;

/// <summary>Reads binary glTF files into a <see cref="CaveMesh"/> or a <see cref="CavePointCloud"/>.</summary>
public static class GlbReader
{
    private const uint Magic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const int MaxDepth = 256;

    private static readonly string[] UnsupportedExtensions =
        ["KHR_draco_mesh_compression", "EXT_meshopt_compression", "KHR_mesh_quantization"];

    /// <summary>Reads a glb from the stream's current position to its end.</summary>
    public static ModelFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long available = stream.Length - stream.Position;
        Span<byte> head = stackalloc byte[12];
        if (available < 12 || stream.Read(head) < 12)
            throw new CaveFileFormatException("GLB: file is shorter than the 12-byte header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(head) != Magic)
            throw new CaveFileFormatException("GLB: bad magic (not a binary glTF file).");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        if (version != 2)
            throw new CaveFileFormatException($"GLB: unsupported glTF version {version} (only 2).");
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(head[8..]);
        if (declared > available)
            throw new CaveFileFormatException($"GLB: truncated: the header declares {declared} bytes but the file has {available}.");

        byte[]? json = null;
        byte[]? bin = null;
        long pos = 12;
        Span<byte> chunkHead = stackalloc byte[8];
        while (pos + 8 <= declared)
        {
            if (stream.Read(chunkHead) < 8) throw new CaveFileFormatException($"GLB: truncated chunk header at byte {pos}.");
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(chunkHead);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(chunkHead[4..]);
            pos += 8;
            if (len > declared - pos)
                throw new CaveFileFormatException($"GLB: chunk at byte {pos - 8} claims {len} bytes but only {declared - pos} remain.");
            if (type == JsonChunkType || type == BinChunkType)
            {
                var data = new byte[len];
                stream.ReadExactly(data);
                if (type == JsonChunkType) json ??= data; else bin ??= data;
            }
            else
                stream.Seek(len, SeekOrigin.Current);
            pos += len;
        }
        if (json is null) throw new CaveFileFormatException("GLB: no JSON chunk.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new CaveFileFormatException($"GLB: the JSON chunk is not valid JSON ({e.Message})."); }
        using (doc)
        {
            try { return new Walker(doc.RootElement, bin).Run(); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
            {
                throw new CaveFileFormatException($"GLB: the JSON chunk is not a valid glTF description ({e.Message}).");
            }
        }
    }

    private sealed class Walker(JsonElement root, byte[]? bin)
    {
        private readonly List<CaveVector3> _vertices = new();
        private readonly List<CaveTriangle> _triangles = new();
        private readonly List<CaveVector3> _points = new();
        private readonly List<string> _warnings = new();
        private int _degenerate, _lines, _pointPrims;

        public ModelFile Run()
        {
            if (root.ValueKind != JsonValueKind.Object) throw new CaveFileFormatException("GLB: the JSON chunk is not an object.");
            if (root.TryGetProperty("extensionsRequired", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in req.EnumerateArray())
                {
                    string? name = e.GetString();
                    if (name is not null && Array.IndexOf(UnsupportedExtensions, name) >= 0)
                        throw new CaveFileFormatException($"GLB: the file requires the glTF extension {name}, which is not supported.");
                }
            }

            foreach (int node in RootNodes())
                Visit(node, Identity, 0);

            if (_degenerate > 0) _warnings.Add($"skipped {_degenerate} degenerate faces");
            if (_lines > 0) _warnings.Add($"ignored {_lines} line primitives");

            bool usePoints = _triangles.Count == 0 && _points.Count > 0;
            if (!usePoints && _pointPrims > 0) _warnings.Add($"ignored {_pointPrims} point primitives (the file has triangles)");
            return new ModelFile
            {
                Format = ModelFileFormat.Glb,
                Mesh = usePoints ? null : new CaveMesh { Vertices = _vertices, Triangles = _triangles },
                Points = usePoints ? new CavePointCloud { Points = _points } : null,
                UpAxisHint = ModelUpAxis.Y,
                UnitsKnown = true,
                Warnings = _warnings,
            };
        }

        private IEnumerable<int> RootNodes()
        {
            if (root.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array && scenes.GetArrayLength() > 0)
            {
                int index = root.TryGetProperty("scene", out var sc) && sc.TryGetInt32(out int si) ? si : 0;
                if (index < 0 || index >= scenes.GetArrayLength())
                    throw new CaveFileFormatException($"GLB: default scene {index} does not exist.");
                if (scenes[index].TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                    foreach (var n in nodes.EnumerateArray()) yield return n.GetInt32();
                yield break;
            }
            if (!root.TryGetProperty("nodes", out var all) || all.ValueKind != JsonValueKind.Array) yield break;
            var children = new HashSet<int>();
            foreach (var node in all.EnumerateArray())
                if (node.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    foreach (var c in ch.EnumerateArray()) children.Add(c.GetInt32());
            for (int i = 0; i < all.GetArrayLength(); i++)
                if (!children.Contains(i)) yield return i;
        }

        private void Visit(int nodeIndex, double[] parent, int depth)
        {
            if (depth > MaxDepth) throw new CaveFileFormatException("GLB: the node hierarchy is too deep (or loops).");
            var nodes = Arr("nodes");
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
                throw new CaveFileFormatException($"GLB: node {nodeIndex} does not exist.");
            var node = nodes[nodeIndex];
            var world = Mul(parent, LocalMatrix(node));
            if (node.TryGetProperty("mesh", out var meshRef))
            {
                int mi = meshRef.GetInt32();
                var meshes = Arr("meshes");
                if (mi < 0 || mi >= meshes.GetArrayLength())
                    throw new CaveFileFormatException($"GLB: mesh {mi} does not exist.");
                if (meshes[mi].TryGetProperty("primitives", out var prims) && prims.ValueKind == JsonValueKind.Array)
                    foreach (var prim in prims.EnumerateArray())
                        AddPrimitive(prim, world);
            }
            if (node.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
                foreach (var k in kids.EnumerateArray())
                    Visit(k.GetInt32(), world, depth + 1);
        }

        private JsonElement Arr(string name)
        {
            if (!root.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array)
                throw new CaveFileFormatException($"GLB: the file has no '{name}' array.");
            return a;
        }

        private void AddPrimitive(JsonElement prim, double[] world)
        {
            int mode = prim.TryGetProperty("mode", out var m) ? m.GetInt32() : 4;
            if (mode == 1 || mode == 2 || mode == 3) { _lines++; return; }
            if (mode < 0 || mode > 6) throw new CaveFileFormatException($"GLB: unknown primitive mode {mode}.");
            if (!prim.TryGetProperty("attributes", out var attrs) || !attrs.TryGetProperty("POSITION", out var posRef))
            {
                _warnings.Add("skipped a primitive without POSITION");
                return;
            }
            var pos = Locate(posRef.GetInt32(), 5126, "VEC3", 12, out int count);
            var local = new CaveVector3[count];
            for (int i = 0; i < count; i++)
            {
                long o = pos.Offset + (long)i * pos.Stride;
                var span = bin!.AsSpan((int)o, 12);
                local[i] = Transform(world,
                    BinaryPrimitives.ReadSingleLittleEndian(span),
                    BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[8..]));
            }

            if (mode == 0)
            {
                _pointPrims++;
                _points.AddRange(local);
                return;
            }

            uint[] order = ReadIndices(prim, count);
            bool flip = Determinant3(world) < 0;
            uint baseIndex = (uint)_vertices.Count;
            _vertices.AddRange(local);

            void Emit(uint a, uint b, uint c, bool checkDegenerate)
            {
                if (checkDegenerate && (a == b || b == c || a == c)) { _degenerate++; return; }
                if (flip) (b, c) = (c, b);
                _triangles.Add(new CaveTriangle(a + baseIndex, b + baseIndex, c + baseIndex));
            }

            switch (mode)
            {
                case 4:
                    for (int i = 0; i + 2 < order.Length; i += 3) Emit(order[i], order[i + 1], order[i + 2], false);
                    if (order.Length % 3 != 0) _warnings.Add($"ignored {order.Length % 3} stray indices after the last triangle");
                    break;
                case 5:
                    for (int i = 0; i + 2 < order.Length; i++)
                    {
                        if ((i & 1) == 0) Emit(order[i], order[i + 1], order[i + 2], true);
                        else Emit(order[i + 1], order[i], order[i + 2], true);
                    }
                    break;
                case 6:
                    for (int i = 1; i + 1 < order.Length; i++) Emit(order[0], order[i], order[i + 1], true);
                    break;
            }
        }

        private uint[] ReadIndices(JsonElement prim, int vertexCount)
        {
            if (!prim.TryGetProperty("indices", out var idxRef))
            {
                var seq = new uint[vertexCount];
                for (uint i = 0; i < seq.Length; i++) seq[i] = i;
                return seq;
            }
            int ai = idxRef.GetInt32();
            var accessors = Arr("accessors");
            if (ai < 0 || ai >= accessors.GetArrayLength()) throw new CaveFileFormatException($"GLB: accessor {ai} does not exist.");
            int comp = accessors[ai].TryGetProperty("componentType", out var ct) ? ct.GetInt32() : 0;
            int size = comp switch
            {
                5121 => 1,
                5123 => 2,
                5125 => 4,
                _ => throw new CaveFileFormatException($"GLB: index accessor {ai} has unsupported componentType {comp}."),
            };
            var loc = Locate(ai, comp, "SCALAR", size, out int count);
            var result = new uint[count];
            for (int i = 0; i < count; i++)
            {
                var span = bin!.AsSpan((int)(loc.Offset + (long)i * loc.Stride), size);
                uint v = size switch
                {
                    1 => span[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(span),
                };
                if (v >= vertexCount)
                    throw new CaveFileFormatException($"GLB: index {v} (entry {i} of accessor {ai}) is outside the primitive's {vertexCount} vertices.");
                result[i] = v;
            }
            return result;
        }

        // Finds an accessor's bytes in the BIN chunk, checking every offset against its length.
        private (long Offset, long Stride) Locate(int accessorIndex, int componentType, string type, int elementSize, out int count)
        {
            var accessors = Arr("accessors");
            if (accessorIndex < 0 || accessorIndex >= accessors.GetArrayLength())
                throw new CaveFileFormatException($"GLB: accessor {accessorIndex} does not exist.");
            var acc = accessors[accessorIndex];
            if ((acc.TryGetProperty("componentType", out var ct) ? ct.GetInt32() : 0) != componentType
                || (acc.TryGetProperty("type", out var ty) ? ty.GetString() : null) != type)
                throw new CaveFileFormatException($"GLB: accessor {accessorIndex} is not {type} of component type {componentType}.");
            if (acc.TryGetProperty("sparse", out _))
                throw new CaveFileFormatException($"GLB: accessor {accessorIndex} is sparse, which is not supported.");
            if (!acc.TryGetProperty("bufferView", out var bvRef))
                throw new CaveFileFormatException($"GLB: accessor {accessorIndex} has no bufferView.");
            long n = acc.TryGetProperty("count", out var cn) ? cn.GetInt64() : 0;
            long accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt64() : 0;

            var views = Arr("bufferViews");
            int bi = bvRef.GetInt32();
            if (bi < 0 || bi >= views.GetArrayLength()) throw new CaveFileFormatException($"GLB: bufferView {bi} does not exist.");
            var bv = views[bi];
            if ((bv.TryGetProperty("buffer", out var b) ? b.GetInt32() : 0) != 0 || bin is null)
                throw new CaveFileFormatException("GLB: the geometry is not in the file's own binary chunk (external or embedded buffers are not supported).");
            long bvOffset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt64() : 0;
            long bvLength = bv.TryGetProperty("byteLength", out var bl) ? bl.GetInt64() : 0;
            long stride = bv.TryGetProperty("byteStride", out var bs) && bs.GetInt64() > 0 ? bs.GetInt64() : elementSize;
            if (stride < elementSize) throw new CaveFileFormatException($"GLB: accessor {accessorIndex} has a byteStride smaller than its elements.");

            long start = bvOffset + accOffset;
            long end = n == 0 ? start : start + (n - 1) * stride + elementSize;
            if (n < 0 || n > int.MaxValue || start < 0 || bvOffset + bvLength > bin.Length || end > bvOffset + bvLength)
                throw new CaveFileFormatException($"GLB: accessor {accessorIndex} ({n} elements) reaches past the end of its data.");
            count = (int)n;
            return (start, stride);
        }

        // ---- 4x4 matrices, column-major as in glTF ----

        private static readonly double[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

        private static double[] Mul(double[] a, double[] b)
        {
            var r = new double[16];
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[c * 4 + k];
                    r[c * 4 + row] = sum;
                }
            return r;
        }

        private static CaveVector3 Transform(double[] m, double x, double y, double z) => new(
            m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14]);

        private static double Determinant3(double[] m)
            => m[0] * (m[5] * m[10] - m[9] * m[6])
             - m[4] * (m[1] * m[10] - m[9] * m[2])
             + m[8] * (m[1] * m[6] - m[5] * m[2]);

        private static double[] Numbers(JsonElement node, string name, int length)
        {
            var el = node.GetProperty(name);
            if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() != length)
                throw new CaveFileFormatException($"GLB: node property '{name}' must be {length} numbers.");
            var r = new double[length];
            int i = 0;
            foreach (var v in el.EnumerateArray()) r[i++] = v.GetDouble();
            return r;
        }

        private static double[] LocalMatrix(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out _)) return Numbers(node, "matrix", 16);
            var t = node.TryGetProperty("translation", out _) ? Numbers(node, "translation", 3) : [0, 0, 0];
            var q = node.TryGetProperty("rotation", out _) ? Numbers(node, "rotation", 4) : [0, 0, 0, 1];
            var s = node.TryGetProperty("scale", out _) ? Numbers(node, "scale", 3) : [1, 1, 1];
            double len = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            if (len == 0) throw new CaveFileFormatException("GLB: a node's rotation quaternion is zero.");
            double x = q[0] / len, y = q[1] / len, z = q[2] / len, w = q[3] / len;
            return
            [
                (1 - 2 * (y * y + z * z)) * s[0], (2 * (x * y + z * w)) * s[0], (2 * (x * z - y * w)) * s[0], 0,
                (2 * (x * y - z * w)) * s[1], (1 - 2 * (x * x + z * z)) * s[1], (2 * (y * z + x * w)) * s[1], 0,
                (2 * (x * z + y * w)) * s[2], (2 * (y * z - x * w)) * s[2], (1 - 2 * (x * x + y * y)) * s[2], 0,
                t[0], t[1], t[2], 1,
            ];
        }
    }
}
