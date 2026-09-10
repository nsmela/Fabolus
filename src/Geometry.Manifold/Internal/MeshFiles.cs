using System.Globalization;
using System.Numerics;
using System.Text;

namespace GeometryManifold.Internal;

/// <summary>
/// Readers and writers for the mesh formats the app handles. MeshLib shipped these as native
/// loaders; Manifold has no file I/O at all, so they are implemented here in managed code.
/// </summary>
internal static class MeshFiles
{
    /// <summary>Every triangle in a binary STL occupies this many bytes: a normal, three vertices, an attribute word.</summary>
    private const int BinaryStlTriangleSize = 50;

    private const int BinaryStlHeaderSize = 84;

    public static (Vector3[] Vertices, int[] Triangles) Read(string path, byte[] bytes)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".stl" => ReadStl(bytes),
            ".obj" => ReadObj(Encoding.UTF8.GetString(bytes)),
            ".off" => ReadOff(Encoding.UTF8.GetString(bytes)),
            ".ply" => ReadPly(bytes),
            _ => throw new NotSupportedException($"No reader for '{extension}'."),
        };
    }

    public static byte[] Write(string path, Vector3[] vertices, int[] triangles)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".stl" => WriteBinaryStl(vertices, triangles),
            ".obj" => Encoding.UTF8.GetBytes(WriteObj(vertices, triangles)),
            ".off" => Encoding.UTF8.GetBytes(WriteOff(vertices, triangles)),
            ".ply" => WriteBinaryPly(vertices, triangles),
            _ => throw new NotSupportedException($"No writer for '{extension}'."),
        };
    }

    // ===== STL =====

    private static (Vector3[], int[]) ReadStl(byte[] bytes)
    {
        return IsBinaryStl(bytes) ? ReadBinaryStl(bytes) : ReadAsciiStl(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// An ASCII STL opens with "solid", but so do some binary ones written by careless exporters,
    /// so the triangle count in the header is the reliable test: for a binary file it accounts for
    /// the length exactly.
    /// </summary>
    private static bool IsBinaryStl(byte[] bytes)
    {
        if (bytes.Length < BinaryStlHeaderSize) return false;

        uint triangleCount = BitConverter.ToUInt32(bytes, 80);
        return (long)BinaryStlHeaderSize + (long)triangleCount * BinaryStlTriangleSize == bytes.Length;
    }

    private static (Vector3[], int[]) ReadBinaryStl(byte[] bytes)
    {
        int triangleCount = (int)BitConverter.ToUInt32(bytes, 80);

        var vertices = new Vector3[triangleCount * 3];
        var triangles = new int[triangleCount * 3];

        for (int i = 0; i < triangleCount; i++)
        {
            // Skip the 12-byte face normal: it is redundant with the winding and often wrong.
            int offset = BinaryStlHeaderSize + i * BinaryStlTriangleSize + 12;
            for (int v = 0; v < 3; v++)
            {
                int position = offset + v * 12;
                vertices[i * 3 + v] = new Vector3(
                    BitConverter.ToSingle(bytes, position),
                    BitConverter.ToSingle(bytes, position + 4),
                    BitConverter.ToSingle(bytes, position + 8));
                triangles[i * 3 + v] = i * 3 + v;
            }
        }

        return (vertices, triangles);
    }

    private static (Vector3[], int[]) ReadAsciiStl(string text)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            triangles.Add(vertices.Count);
            vertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
        }

        // Trailing vertices that never completed a facet would index past the triangle list.
        int usable = triangles.Count - triangles.Count % 3;
        return (vertices.ToArray(), triangles.GetRange(0, usable).ToArray());
    }

    private static byte[] WriteBinaryStl(Vector3[] vertices, int[] triangles)
    {
        int triangleCount = triangles.Length / 3;
        var bytes = new byte[BinaryStlHeaderSize + triangleCount * BinaryStlTriangleSize];

        var header = Encoding.ASCII.GetBytes("Exported by Fabolus (Manifold engine)");
        Array.Copy(header, bytes, Math.Min(header.Length, 80));
        BitConverter.GetBytes((uint)triangleCount).CopyTo(bytes, 80);

        for (int i = 0; i < triangleCount; i++)
        {
            var a = vertices[triangles[i * 3]];
            var b = vertices[triangles[i * 3 + 1]];
            var c = vertices[triangles[i * 3 + 2]];

            var normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero;

            int offset = BinaryStlHeaderSize + i * BinaryStlTriangleSize;
            WriteVector(bytes, offset, normal);
            WriteVector(bytes, offset + 12, a);
            WriteVector(bytes, offset + 24, b);
            WriteVector(bytes, offset + 36, c);
            // Bytes 48-49 are the attribute word, left zero.
        }

        return bytes;
    }

    private static void WriteVector(byte[] bytes, int offset, Vector3 v)
    {
        BitConverter.GetBytes(v.X).CopyTo(bytes, offset);
        BitConverter.GetBytes(v.Y).CopyTo(bytes, offset + 4);
        BitConverter.GetBytes(v.Z).CopyTo(bytes, offset + 8);
    }

    // ===== OBJ =====

    public static (Vector3[] Vertices, int[] Triangles) ReadObj(string text)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0] == "v" && parts.Length >= 4)
            {
                vertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                // OBJ faces may be n-gons and may carry texture/normal indices; fan-triangulate
                // and keep only the position index before the first slash.
                var corners = new int[parts.Length - 1];
                for (int i = 1; i < parts.Length; i++)
                {
                    var token = parts[i];
                    int slash = token.IndexOf('/');
                    if (slash >= 0) token = token[..slash];

                    int index = int.Parse(token, CultureInfo.InvariantCulture);
                    corners[i - 1] = index > 0 ? index - 1 : vertices.Count + index; // Negative indices count back.
                }

                for (int i = 1; i + 1 < corners.Length; i++)
                {
                    triangles.Add(corners[0]);
                    triangles.Add(corners[i]);
                    triangles.Add(corners[i + 1]);
                }
            }
        }

        return (vertices.ToArray(), triangles.ToArray());
    }

    private static string WriteObj(Vector3[] vertices, int[] triangles)
    {
        var builder = new StringBuilder();
        foreach (var v in vertices)
        {
            builder.Append("v ").Append(Format(v.X)).Append(' ').Append(Format(v.Y)).Append(' ').Append(Format(v.Z)).Append('\n');
        }
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            builder.Append("f ")
                .Append(triangles[i] + 1).Append(' ')
                .Append(triangles[i + 1] + 1).Append(' ')
                .Append(triangles[i + 2] + 1).Append('\n');
        }
        return builder.ToString();
    }

    // ===== OFF =====

    private static (Vector3[], int[]) ReadOff(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int cursor = 0;
        if (cursor < tokens.Length && tokens[cursor].StartsWith("OFF", StringComparison.OrdinalIgnoreCase)) cursor++;

        int vertexCount = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
        int faceCount = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
        cursor++; // Edge count, unused and often zero.

        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(ParseFloat(tokens[cursor]), ParseFloat(tokens[cursor + 1]), ParseFloat(tokens[cursor + 2]));
            cursor += 3;
        }

        var triangles = new List<int>(faceCount * 3);
        for (int i = 0; i < faceCount; i++)
        {
            int corners = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
            var indices = new int[corners];
            for (int c = 0; c < corners; c++) indices[c] = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);

            for (int c = 1; c + 1 < corners; c++)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[c]);
                triangles.Add(indices[c + 1]);
            }
        }

        return (vertices, triangles.ToArray());
    }

    private static string WriteOff(Vector3[] vertices, int[] triangles)
    {
        var builder = new StringBuilder();
        builder.Append("OFF\n").Append(vertices.Length).Append(' ').Append(triangles.Length / 3).Append(" 0\n");

        foreach (var v in vertices)
        {
            builder.Append(Format(v.X)).Append(' ').Append(Format(v.Y)).Append(' ').Append(Format(v.Z)).Append('\n');
        }
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            builder.Append("3 ").Append(triangles[i]).Append(' ').Append(triangles[i + 1]).Append(' ').Append(triangles[i + 2]).Append('\n');
        }
        return builder.ToString();
    }

    // ===== PLY =====

    private static (Vector3[], int[]) ReadPly(byte[] bytes)
    {
        // The header is always ASCII, however the body is encoded.
        var headerText = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
        int headerEnd = headerText.IndexOf("end_header", StringComparison.OrdinalIgnoreCase);
        if (headerEnd < 0) throw new InvalidDataException("PLY file has no end_header.");

        int bodyStart = headerEnd + "end_header".Length;
        while (bodyStart < bytes.Length && (bytes[bodyStart] == '\r' || bytes[bodyStart] == '\n')) bodyStart++;

        var headerLines = headerText[..headerEnd].Split('\n').Select(l => l.Trim()).ToList();

        bool isAscii = headerLines.Any(l => l.StartsWith("format ascii", StringComparison.OrdinalIgnoreCase));
        bool isLittleEndian = headerLines.Any(l => l.StartsWith("format binary_little_endian", StringComparison.OrdinalIgnoreCase));
        if (!isAscii && !isLittleEndian)
            throw new NotSupportedException("Only ascii and binary_little_endian PLY files are supported.");

        int vertexCount = 0;
        int faceCount = 0;
        var vertexProperties = new List<string>();
        string? current = null;

        foreach (var line in headerLines)
        {
            if (line.StartsWith("element vertex", StringComparison.OrdinalIgnoreCase))
            {
                current = "vertex";
                vertexCount = int.Parse(line.Split(' ')[2], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("element face", StringComparison.OrdinalIgnoreCase))
            {
                current = "face";
                faceCount = int.Parse(line.Split(' ')[2], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("element", StringComparison.OrdinalIgnoreCase))
            {
                current = "other";
            }
            else if (line.StartsWith("property", StringComparison.OrdinalIgnoreCase) && current == "vertex")
            {
                vertexProperties.Add(line);
            }
        }

        if (isAscii)
        {
            var body = Encoding.ASCII.GetString(bytes, bodyStart, bytes.Length - bodyStart);
            var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int cursor = 0;

            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(ParseFloat(tokens[cursor]), ParseFloat(tokens[cursor + 1]), ParseFloat(tokens[cursor + 2]));
                cursor += vertexProperties.Count;
            }

            var triangles = new List<int>(faceCount * 3);
            for (int i = 0; i < faceCount; i++)
            {
                int corners = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
                var indices = new int[corners];
                for (int c = 0; c < corners; c++) indices[c] = int.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
                for (int c = 1; c + 1 < corners; c++)
                {
                    triangles.Add(indices[0]);
                    triangles.Add(indices[c]);
                    triangles.Add(indices[c + 1]);
                }
            }

            return (vertices, triangles.ToArray());
        }

        return ReadBinaryPly(bytes, bodyStart, vertexCount, faceCount, vertexProperties);
    }

    private static (Vector3[], int[]) ReadBinaryPly(
        byte[] bytes, int cursor, int vertexCount, int faceCount, List<string> vertexProperties)
    {
        int vertexStride = vertexProperties.Sum(p => TypeSize(p.Split(' ')[1]));

        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(
                BitConverter.ToSingle(bytes, cursor),
                BitConverter.ToSingle(bytes, cursor + 4),
                BitConverter.ToSingle(bytes, cursor + 8));
            cursor += vertexStride;
        }

        var triangles = new List<int>(faceCount * 3);
        for (int i = 0; i < faceCount; i++)
        {
            int corners = bytes[cursor++]; // uchar count, the near-universal convention.
            var indices = new int[corners];
            for (int c = 0; c < corners; c++)
            {
                indices[c] = BitConverter.ToInt32(bytes, cursor);
                cursor += 4;
            }
            for (int c = 1; c + 1 < corners; c++)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[c]);
                triangles.Add(indices[c + 1]);
            }
        }

        return (vertices, triangles.ToArray());
    }

    private static byte[] WriteBinaryPly(Vector3[] vertices, int[] triangles)
    {
        int triangleCount = triangles.Length / 3;
        var header = new StringBuilder()
            .Append("ply\nformat binary_little_endian 1.0\n")
            .Append("element vertex ").Append(vertices.Length).Append('\n')
            .Append("property float x\nproperty float y\nproperty float z\n")
            .Append("element face ").Append(triangleCount).Append('\n')
            .Append("property list uchar int vertex_indices\n")
            .Append("end_header\n")
            .ToString();

        var headerBytes = Encoding.ASCII.GetBytes(header);
        var bytes = new byte[headerBytes.Length + vertices.Length * 12 + triangleCount * 13];
        headerBytes.CopyTo(bytes, 0);

        int cursor = headerBytes.Length;
        foreach (var v in vertices)
        {
            WriteVector(bytes, cursor, v);
            cursor += 12;
        }
        for (int i = 0; i < triangleCount; i++)
        {
            bytes[cursor++] = 3;
            BitConverter.GetBytes(triangles[i * 3]).CopyTo(bytes, cursor);
            BitConverter.GetBytes(triangles[i * 3 + 1]).CopyTo(bytes, cursor + 4);
            BitConverter.GetBytes(triangles[i * 3 + 2]).CopyTo(bytes, cursor + 8);
            cursor += 12;
        }

        return bytes;
    }

    private static int TypeSize(string type) => type.ToLowerInvariant() switch
    {
        "char" or "uchar" or "int8" or "uint8" => 1,
        "short" or "ushort" or "int16" or "uint16" => 2,
        "int" or "uint" or "int32" or "uint32" or "float" or "float32" => 4,
        "double" or "float64" => 8,
        _ => 4,
    };

    private static float ParseFloat(string token) => float.Parse(token, CultureInfo.InvariantCulture);

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
