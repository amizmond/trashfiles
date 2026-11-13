using QRCoder;

namespace QrEncoder.Web.Services;

/// <summary>A folder packed into QR images that live only in memory - nothing is written to disk.</summary>
public sealed record QrTransfer(
    string TransferId,
    string InputPath,
    int FileCount,
    long RawSize,
    long CompressedSize,
    IReadOnlyList<byte[]> Images,
    IReadOnlyList<string> Payloads);

public sealed class QrTransferService
{
    private const int PixelsPerModule = 8;
    // Q (~25% error correction) tolerates more blur/skew/glare than M (~15%) at the cost of ~20% more QRs per transfer.
    private const QRCodeGenerator.ECCLevel ErrorCorrection = QRCodeGenerator.ECCLevel.Q;

    public QrTransfer Encode(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Enter a folder to encode.", nameof(folder));

        var inputPath = Path.GetFullPath(folder.Trim().Trim('"'));
        if (!Directory.Exists(inputPath))
            throw new DirectoryNotFoundException($"Input folder not found: {inputPath}");

        var packed = FilePacker.Pack(inputPath);
        if (packed.FileCount == 0)
            throw new InvalidOperationException($"Input folder is empty: {inputPath}");

        var transferId = NewTransferId();
        var chunks = QrChunker.Chunk(packed.CompressedData, QrChunker.RawChunkBytes);

        var images = new byte[chunks.Count][];
        var payloads = new string[chunks.Count];
        using var generator = new QRCodeGenerator();
        for (var i = 0; i < chunks.Count; i++)
        {
            payloads[i] = QrChunker.FormatPayload(transferId, i + 1, chunks.Count, chunks[i]);
            using var data = generator.CreateQrCode(payloads[i], ErrorCorrection);
            images[i] = new PngByteQRCode(data).GetGraphic(PixelsPerModule);
        }

        return new QrTransfer(transferId, inputPath, packed.FileCount, packed.RawSize, packed.CompressedSize, images, payloads);
    }

    private static string NewTransferId()
    {
        Span<byte> buf = stackalloc byte[4];
        Random.Shared.NextBytes(buf);
        return Convert.ToHexString(buf);
    }
}
