using System.Buffers.Binary;
using System.Text;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// What a player photo has to be before it is stored, and what is taken out of it first.
///
/// THE BYTES DECIDE, NOT THE UPLOAD. The content type a browser sends and the file's extension
/// are both whatever the sender says they are; the format is read from the file's own signature,
/// and only three are accepted: JPEG, PNG and WebP. Everything else -- GIF, HEIC, and above all
/// SVG, which is a document that can carry script -- is refused.
///
/// METADATA IS STRIPPED. A photo can carry where and when it was taken (EXIF, often with GPS),
/// the camera owner's name, captions and keywords (IPTC, XMP), and comments. None of it is needed
/// to show a face, and a photo of a child is not the place to keep a location. The segments that
/// carry it are removed; the ones a decoder needs -- the image data, the colour profile, JPEG's
/// Adobe marker -- are kept byte for byte. Nothing is re-encoded, so the picture itself is exactly
/// what the club published.
///
/// A file that does not parse as the format its signature claims is refused rather than stored
/// half-understood.
/// </summary>
public static class PlayerPhotoRules
{
    /// <summary>2 MB. A squad photo for the web is a fraction of that.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    /// <summary>For the file input's accept attribute. A hint to the browser, not a check.</summary>
    public const string Accept = "image/jpeg,image/png,image/webp";

    /// <summary>Checks a photo and returns it without its metadata, or says why it cannot be used.</summary>
    public static PhotoCheck Prepare(byte[] data)
    {
        if (data.Length == 0)
        {
            return PhotoCheck.Refused("The file is empty.");
        }

        if (data.Length > MaxBytes)
        {
            return PhotoCheck.Refused($"The photo is {data.Length / 1024} KB. The largest that can be used is {MaxBytes / 1024 / 1024} MB.");
        }

        try
        {
            if (IsJpeg(data))
            {
                return PhotoCheck.Accepted(StripJpeg(data), Jpeg);
            }

            if (IsPng(data))
            {
                return PhotoCheck.Accepted(StripPng(data), Png);
            }

            if (IsWebP(data))
            {
                return PhotoCheck.Accepted(StripWebP(data), WebP);
            }
        }
        catch (InvalidDataException)
        {
            return PhotoCheck.Refused("The photo could not be read. Save it again as a JPEG or PNG and try once more.");
        }

        return PhotoCheck.Refused("Use a JPEG, PNG or WebP photo.");
    }

    // -----------------------------------------------------------------------------------
    // JPEG
    // -----------------------------------------------------------------------------------

    private static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>
    /// The segments removed from a JPEG: APP1 (EXIF and XMP), APP12 (Ducky), APP13 (Photoshop and
    /// IPTC) and COM (comments). APP0 (JFIF), APP2 (the ICC colour profile) and APP14 (Adobe, which
    /// decoders need for CMYK) stay.
    /// </summary>
    private static readonly HashSet<byte> JpegSegmentsToRemove = new() { 0xE1, 0xEC, 0xED, 0xFE };

    private static byte[] StripJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2); // SOI

        var position = 2;

        while (position < data.Length)
        {
            if (data[position] != 0xFF)
            {
                throw new InvalidDataException("Expected a JPEG marker.");
            }

            // Any number of 0xFF fill bytes may come before a marker.
            var markerAt = position;
            while (markerAt < data.Length && data[markerAt] == 0xFF)
            {
                markerAt++;
            }

            if (markerAt >= data.Length)
            {
                throw new InvalidDataException("The JPEG ends in the middle of a marker.");
            }

            var marker = data[markerAt];
            var segmentStart = markerAt + 1;

            if (marker == 0xD9)
            {
                output.Write(new byte[] { 0xFF, 0xD9 });
                return output.ToArray();
            }

            // Markers without a length: TEM and the restart markers.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                output.Write(new byte[] { 0xFF, marker });
                position = segmentStart;
                continue;
            }

            if (segmentStart + 2 > data.Length)
            {
                throw new InvalidDataException("The JPEG ends in the middle of a segment.");
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(segmentStart, 2));
            if (length < 2 || segmentStart + length > data.Length)
            {
                throw new InvalidDataException("A JPEG segment runs past the end of the file.");
            }

            var next = segmentStart + length;

            if (marker == 0xDA)
            {
                // Start of scan: from here on it is image data (and, in a progressive JPEG, more
                // scans and tables). Metadata does not live here; copy the rest as it is.
                output.Write(new byte[] { 0xFF, marker });
                output.Write(data, segmentStart, data.Length - segmentStart);
                return output.ToArray();
            }

            if (!JpegSegmentsToRemove.Contains(marker))
            {
                output.Write(new byte[] { 0xFF, marker });
                output.Write(data, segmentStart, length);
            }

            position = next;
        }

        throw new InvalidDataException("The JPEG has no image data.");
    }

    // -----------------------------------------------------------------------------------
    // PNG
    // -----------------------------------------------------------------------------------

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static bool IsPng(byte[] data) => data.AsSpan().StartsWith(PngSignature);

    /// <summary>The PNG chunks removed: text in three encodings, EXIF, and the modification time.</summary>
    private static readonly HashSet<string> PngChunksToRemove = new(StringComparer.Ordinal)
    {
        "tEXt", "zTXt", "iTXt", "eXIf", "tIME"
    };

    private static byte[] StripPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);

        var position = PngSignature.Length;
        var first = true;

        while (position + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
            var type = Encoding.ASCII.GetString(data, position + 4, 4);
            var total = 12L + length; // length, type, data, CRC

            if (length > int.MaxValue || position + total > data.Length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            if (first && type != "IHDR")
            {
                throw new InvalidDataException("A PNG starts with its header.");
            }

            first = false;

            if (!PngChunksToRemove.Contains(type))
            {
                output.Write(data, position, (int)total);
            }

            position += (int)total;

            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        throw new InvalidDataException("The PNG has no end.");
    }

    // -----------------------------------------------------------------------------------
    // WebP
    // -----------------------------------------------------------------------------------

    private static bool IsWebP(byte[] data) =>
        data.Length >= 12
        && Encoding.ASCII.GetString(data, 0, 4) == "RIFF"
        && Encoding.ASCII.GetString(data, 8, 4) == "WEBP";

    private static byte[] StripWebP(byte[] data)
    {
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        var end = (int)Math.Min(data.Length, 8L + declared);

        var chunks = new List<(string Type, byte[] Bytes)>();
        var position = 12;
        var hasImage = false;

        while (position + 8 <= end)
        {
            var type = Encoding.ASCII.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4, 4));
            var padded = 8L + size + (size % 2);

            if (position + 8L + size > end)
            {
                throw new InvalidDataException("A WebP chunk runs past the end of the file.");
            }

            var length = (int)Math.Min(padded, end - position);

            if (type is "VP8 " or "VP8L" or "VP8X")
            {
                hasImage = true;
            }

            if (type is not ("EXIF" or "XMP "))
            {
                var chunk = data.AsSpan(position, length).ToArray();

                // The extended header says which optional chunks follow. Two of them are gone,
                // so the flags that announce them have to go too: XMP is bit 2, EXIF bit 3.
                if (type == "VP8X" && chunk.Length > 8)
                {
                    chunk[8] &= unchecked((byte)~0x0C);
                }

                chunks.Add((type, chunk));
            }

            position += length;
        }

        if (!hasImage)
        {
            throw new InvalidDataException("The WebP has no image data.");
        }

        var body = chunks.Sum(c => c.Bytes.Length);

        using var output = new MemoryStream(12 + body);
        output.Write(Encoding.ASCII.GetBytes("RIFF"));

        Span<byte> size32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size32, (uint)(4 + body));
        output.Write(size32);
        output.Write(Encoding.ASCII.GetBytes("WEBP"));

        foreach (var (_, bytes) in chunks)
        {
            output.Write(bytes);
        }

        return output.ToArray();
    }
}

/// <summary>The outcome of <see cref="PlayerPhotoRules.Prepare"/>.</summary>
public sealed record PhotoCheck(byte[]? Photo, string? ContentType, string? Error)
{
    public bool IsAccepted => Photo is not null;

    public static PhotoCheck Accepted(byte[] photo, string contentType) => new(photo, contentType, null);

    public static PhotoCheck Refused(string error) => new(null, null, error);
}
