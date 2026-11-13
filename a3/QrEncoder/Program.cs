using QRCoder;

namespace QrEncoder;

internal static class Program
{
    private const string DefaultInputFolder = "Input";
    private const string OutputFolder = "Output";
    private const int PixelsPerModule = 8;
    // Q (~25% error correction) tolerates more blur/skew/glare than M (~15%) at the cost of ~20% more QRs per transfer.
    private const QRCodeGenerator.ECCLevel ErrorCorrection = QRCodeGenerator.ECCLevel.Q;

    private static int Main(string[] args)
    {
        var inputPath = Path.GetFullPath(args.Length > 0 ? args[0] : DefaultInputFolder);
        if (!Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input folder not found: {inputPath}");
            return 1;
        }

        var outputPath = Path.GetFullPath(OutputFolder);
        Directory.CreateDirectory(outputPath);
        foreach (var stale in Directory.EnumerateFiles(outputPath, "*.png"))
            File.Delete(stale);

        Console.WriteLine($"Input : {inputPath}");
        Console.WriteLine($"Output: {outputPath}");

        var packed = FilePacker.Pack(inputPath);
        if (packed.FileCount == 0)
        {
            Console.Error.WriteLine("Input folder is empty.");
            return 1;
        }
        Console.WriteLine($"Packed {packed.FileCount} file(s): {packed.RawSize:N0} B raw -> {packed.CompressedSize:N0} B gzip");

        var transferId = NewTransferId();
        var chunks = QrChunker.Chunk(packed.CompressedData, QrChunker.RawChunkBytes);
        Console.WriteLine($"Transfer {transferId}: {chunks.Count} chunk(s) of <= {QrChunker.RawChunkBytes} bytes");

        using var generator = new QRCodeGenerator();
        for (var i = 0; i < chunks.Count; i++)
        {
            var index = i + 1;
            var payload = QrChunker.FormatPayload(transferId, index, chunks.Count, chunks[i]);
            using var data = generator.CreateQrCode(payload, ErrorCorrection);
            var png = new PngByteQRCode(data).GetGraphic(PixelsPerModule);
            var name = $"{transferId}_{index:D4}_of_{chunks.Count:D4}.png";
            File.WriteAllBytes(Path.Combine(outputPath, name), png);
        }
        Console.WriteLine($"Wrote {chunks.Count} QR PNG(s) to {outputPath}");
        Console.ReadLine();
        return 0;
    }

    private static string NewTransferId()
    {
        Span<byte> buf = stackalloc byte[4];
        Random.Shared.NextBytes(buf);
        return Convert.ToHexString(buf);
    }
}
