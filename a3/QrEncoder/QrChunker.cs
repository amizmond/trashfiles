namespace QrEncoder;

internal static class QrChunker
{
    public const string PayloadVersion = "QR1";

    /// <summary>
    /// Bytes of compressed payload carried by one QR code.
    /// <para>
    /// Picked for the QR version it lands on rather than for being a round number. 1024 bytes
    /// base64-encode to 1368 characters which, with the longest header this format can produce,
    /// fits version 37 - 165x165 modules - at error correction Q. Version 37 takes 1050 such bytes
    /// before it tips into version 38, so the margin covers a header grown by a five-figure chunk
    /// count without quietly making every code in the transfer a version denser.
    /// </para>
    /// <para>
    /// It was 336 bytes - version 20, 97x97 - for as long as QrDecoder read with ZXing.Net, which
    /// fits one perspective transform over the whole symbol and loses the grid on a photographed
    /// code this dense. QrDecoder now reads with zxing-cpp, which re-anchors on the timing and
    /// alignment patterns as it samples; it takes a version 37 code off a photographed screen in
    /// about a tenth of a second. Three times the payload per code is three times fewer
    /// photographs to take, which is the part of a transfer that costs real time.
    /// </para>
    /// </summary>
    public const int RawChunkBytes = 1024;

    public static List<byte[]> Chunk(byte[] data, int chunkSize)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var list = new List<byte[]>(Math.Max(1, (data.Length + chunkSize - 1) / chunkSize));
        for (var off = 0; off < data.Length; off += chunkSize)
        {
            var len = Math.Min(chunkSize, data.Length - off);
            var slice = new byte[len];
            Buffer.BlockCopy(data, off, slice, 0, len);
            list.Add(slice);
        }
        if (list.Count == 0) list.Add(Array.Empty<byte>());
        return list;
    }

    public static string FormatPayload(string transferId, int index, int total, byte[] chunk)
        => $"{PayloadVersion}|{transferId}|{index}|{total}|{Convert.ToBase64String(chunk)}";
}
