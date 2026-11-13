using System.IO.Compression;
using System.Text;

namespace QrEncoder;

internal sealed record PackedTransfer(int FileCount, long RawSize, long CompressedSize, byte[] CompressedData);

internal static class FilePacker
{
    private const uint Magic = 0x51524330u; // 'QRC0'

    public static PackedTransfer Pack(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(files.Count);
            foreach (var path in files)
            {
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                var pathBytes = Encoding.UTF8.GetBytes(rel);
                if (pathBytes.Length > ushort.MaxValue)
                    throw new InvalidOperationException($"Path too long: {rel}");
                w.Write((ushort)pathBytes.Length);
                w.Write(pathBytes);

                var content = File.ReadAllBytes(path);
                w.Write((long)content.LongLength);
                w.Write(content);
            }
        }

        var rawBytes = raw.ToArray();
        using var compressed = new MemoryStream();
        using (var gz = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(rawBytes, 0, rawBytes.Length);

        return new PackedTransfer(files.Count, rawBytes.LongLength, compressed.Length, compressed.ToArray());
    }
}
