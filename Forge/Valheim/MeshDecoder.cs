using System.Numerics;
using AssetsTools.NET;

namespace DrakesForge.Valheim;

/// <summary>Reads a Unity (2019.3+) Mesh asset's vertex streams and index buffer.</summary>
internal static class MeshDecoder
{
    private const int ChannelPosition = 0;
    private const int ChannelNormal = 1;
    private const int ChannelUv0 = 4;

    internal sealed class Decoded
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector2[] Uvs { get; init; }
        public required List<(int[] Indices, int BaseVertex)> SubMeshes { get; init; }

        /// <summary>
        /// Into prefab-root space, mirrored from Unity's left-handed axes to the viewer's right-handed ones
        /// (X flipped, winding reversed).
        /// </summary>
        public ModelPart ToPart(int sub, Matrix4x4 toRoot, string name, int materialSlot)
        {
            var (src, baseVertex) = SubMeshes[sub];
            var used = new Dictionary<int, int>();
            var indices = new int[src.Length];
            var order = new List<int>();
            for (var i = 0; i < src.Length; i++)
            {
                var v = src[i] + baseVertex;
                if (!used.TryGetValue(v, out var mapped))
                {
                    mapped = order.Count;
                    used[v] = mapped;
                    order.Add(v);
                }

                indices[i] = mapped;
            }

            for (var i = 0; i + 2 < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

            var positions = new float[order.Count * 3];
            var normals = new float[order.Count * 3];
            var uvs = new float[order.Count * 2];
            for (var i = 0; i < order.Count; i++)
            {
                var v = order[i];
                var p = Vector3.Transform(Positions[v], toRoot);
                var n = Normals.Length > v ? Vector3.Normalize(Vector3.TransformNormal(Normals[v], toRoot)) : Vector3.UnitY;
                positions[i * 3] = -p.X;
                positions[i * 3 + 1] = p.Y;
                positions[i * 3 + 2] = p.Z;
                normals[i * 3] = -n.X;
                normals[i * 3 + 1] = n.Y;
                normals[i * 3 + 2] = n.Z;
                if (Uvs.Length > v)
                {
                    uvs[i * 2] = Uvs[v].X;
                    uvs[i * 2 + 1] = Uvs[v].Y;
                }
            }

            return new ModelPart { Name = name, Positions = positions, Normals = normals, Uvs = uvs, Indices = indices, MaterialSlot = materialSlot };
        }
    }

    public static Decoded Decode(AssetSession s, AssetRef meshRef)
    {
        var bf = s.Manager.GetBaseField(meshRef.File, meshRef.File.file.GetAssetInfo(meshRef.PathId));
        if (!bf["m_MeshCompression"].IsDummy && bf["m_MeshCompression"].AsInt != 0)
            throw new NotSupportedException("compressed meshes can't be previewed yet");

        var vertexData = bf["m_VertexData"];
        var vertexCount = vertexData["m_VertexCount"].AsInt;
        var data = vertexData["m_DataSize"].AsByteArray;
        if (data == null || data.Length == 0)
            data = ReadStreamed(meshRef, bf["m_StreamData"]);

        var channels = vertexData["m_Channels.Array"].Children
            .Select(c => new Channel(c["stream"].AsInt, c["offset"].AsInt, c["format"].AsInt, c["dimension"].AsInt & 0xF))
            .ToList();

        // Streams are laid out back to back, each padded to 16 bytes.
        var streamCount = channels.Count == 0 ? 0 : channels.Max(c => c.Stream) + 1;
        var strides = new int[streamCount];
        foreach (var c in channels.Where(c => c.Dimension > 0))
            strides[c.Stream] += FormatSize(c.Format) * c.Dimension;
        var streamOffsets = new long[streamCount];
        long offset = 0;
        for (var i = 0; i < streamCount; i++)
        {
            streamOffsets[i] = offset;
            offset += (long)vertexCount * strides[i];
            offset = (offset + 15) & ~15L;
        }

        Vector3[] Read3(int channelIndex)
        {
            if (channelIndex >= channels.Count || channels[channelIndex].Dimension < 3)
                return Array.Empty<Vector3>();
            var c = channels[channelIndex];
            var result = new Vector3[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                var at = streamOffsets[c.Stream] + (long)v * strides[c.Stream] + c.Offset;
                result[v] = new Vector3(Component(data, at, c.Format, 0), Component(data, at, c.Format, 1), Component(data, at, c.Format, 2));
            }

            return result;
        }

        Vector2[] Read2(int channelIndex)
        {
            if (channelIndex >= channels.Count || channels[channelIndex].Dimension < 2)
                return Array.Empty<Vector2>();
            var c = channels[channelIndex];
            var result = new Vector2[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                var at = streamOffsets[c.Stream] + (long)v * strides[c.Stream] + c.Offset;
                result[v] = new Vector2(Component(data, at, c.Format, 0), Component(data, at, c.Format, 1));
            }

            return result;
        }

        var indexBuffer = bf["m_IndexBuffer.Array"].AsByteArray ?? Array.Empty<byte>();
        var wide = bf["m_IndexFormat"].AsInt == 1;
        var subMeshes = new List<(int[], int)>();
        foreach (var sm in bf["m_SubMeshes.Array"].Children)
        {
            if (sm["topology"].AsInt != 0)
                continue; // only triangle lists
            var first = (int)sm["firstByte"].AsUInt;
            var count = (int)sm["indexCount"].AsUInt;
            var indices = new int[count];
            for (var i = 0; i < count; i++)
                indices[i] = wide ? (int)BitConverter.ToUInt32(indexBuffer, first + i * 4) : BitConverter.ToUInt16(indexBuffer, first + i * 2);
            subMeshes.Add((indices, sm["baseVertex"].AsInt));
        }

        return new Decoded
        {
            Positions = Read3(ChannelPosition),
            Normals = Read3(ChannelNormal),
            Uvs = Read2(ChannelUv0),
            SubMeshes = subMeshes
        };
    }

    private readonly record struct Channel(int Stream, int Offset, int Format, int Dimension);

    // Unity VertexAttributeFormat.
    private static int FormatSize(int format) => format switch
    {
        0 => 4, // Float32
        1 => 2, // Float16
        2 or 3 or 6 or 7 => 1, // UNorm8, SNorm8, UInt8, SInt8
        4 or 5 or 8 or 9 => 2, // UNorm16, SNorm16, UInt16, SInt16
        10 or 11 => 4, // UInt32, SInt32
        _ => throw new NotSupportedException($"vertex format {format}")
    };

    private static float Component(byte[] data, long at, int format, int index)
    {
        var i = (int)(at + index * FormatSize(format));
        return format switch
        {
            0 => BitConverter.ToSingle(data, i),
            1 => (float)BitConverter.ToHalf(data, i),
            2 => data[i] / 255f,
            3 => Math.Max((sbyte)data[i] / 127f, -1f),
            4 => BitConverter.ToUInt16(data, i) / 65535f,
            5 => Math.Max(BitConverter.ToInt16(data, i) / 32767f, -1f),
            6 => data[i],
            7 => (sbyte)data[i],
            8 => BitConverter.ToUInt16(data, i),
            9 => BitConverter.ToInt16(data, i),
            10 => BitConverter.ToUInt32(data, i),
            _ => BitConverter.ToInt32(data, i)
        };
    }

    /// <summary>Vertex data kept in the bundle's .resS side file.</summary>
    private static byte[] ReadStreamed(AssetRef meshRef, AssetTypeValueField streamData)
    {
        var path = streamData["path"].AsString;
        var size = (int)streamData["size"].AsUInt;
        if (string.IsNullOrEmpty(path) || size == 0)
            throw new InvalidDataException("mesh has no vertex data");

        var bundle = meshRef.File.parentBundle ?? throw new InvalidDataException("streamed mesh outside a bundle");
        var name = path[(path.LastIndexOf('/') + 1)..];
        var index = bundle.file.GetAllFileNames().FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidDataException($"'{name}' not in bundle");

        bundle.file.GetFileRange(index, out var fileOffset, out _);
        var reader = bundle.file.DataReader;
        reader.Position = fileOffset + (long)streamData["offset"].AsULong;
        return reader.ReadBytes(size);
    }
}
