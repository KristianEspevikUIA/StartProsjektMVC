using System.Buffers.Binary;
using System.Text;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// What a player photo has to be, and what is taken out of it before it is stored.
///
/// The images here are built by hand, segment by segment, so each test says exactly what the
/// file contains and which part of it has to survive. They are not pictures of anybody.
/// </summary>
public class PlayerPhotoRulesTests
{
    // -----------------------------------------------------------------------------------
    // Which files
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Jpeg_png_and_webp_are_accepted_by_their_bytes()
    {
        Assert.Equal(PlayerPhotoRules.Jpeg, PlayerPhotoRules.Prepare(Jpeg()).ContentType);
        Assert.Equal(PlayerPhotoRules.Png, PlayerPhotoRules.Prepare(Png()).ContentType);
        Assert.Equal(PlayerPhotoRules.WebP, PlayerPhotoRules.Prepare(WebP()).ContentType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("GIF89a....")]
    [InlineData("just some text pretending to be a .jpg")]
    public void Anything_else_is_refused_whatever_it_is_called(string content)
    {
        var check = PlayerPhotoRules.Prepare(Encoding.UTF8.GetBytes(content));

        Assert.False(check.IsAccepted);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", check.Error);
    }

    [Fact]
    public void An_empty_file_and_a_file_over_the_limit_are_refused()
    {
        Assert.False(PlayerPhotoRules.Prepare(Array.Empty<byte>()).IsAccepted);

        var huge = new byte[PlayerPhotoRules.MaxBytes + 1];
        Jpeg().CopyTo(huge, 0);

        var check = PlayerPhotoRules.Prepare(huge);
        Assert.False(check.IsAccepted);
        Assert.Contains("2 MB", check.Error);
    }

    [Fact]
    public void A_file_that_claims_to_be_a_jpeg_and_is_not_one_is_refused()
    {
        // The JPEG signature, then a segment length that runs off the end of the file.
        var broken = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x40, 0x00, 0x01 };

        var check = PlayerPhotoRules.Prepare(broken);

        Assert.False(check.IsAccepted);
        Assert.Contains("could not be read", check.Error);
    }

    // -----------------------------------------------------------------------------------
    // What is taken out
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_jpeg_loses_its_exif_iptc_and_comments_and_keeps_its_picture()
    {
        var original = Jpeg(withMetadata: true);

        var photo = PlayerPhotoRules.Prepare(original).Photo!;

        // Gone: where it was taken (EXIF/GPS in APP1), the caption (IPTC in APP13), the comment.
        Assert.DoesNotContain("GPSLatitude", Ascii(photo));
        Assert.DoesNotContain("Caption: a player", Ascii(photo));
        Assert.DoesNotContain("Shot by somebody", Ascii(photo));

        // Kept: JFIF, the colour profile, and the image data after the start of scan.
        Assert.Contains("JFIF", Ascii(photo));
        Assert.Contains("ICC_PROFILE", Ascii(photo));
        Assert.Contains("SCANDATA", Ascii(photo));
        Assert.True(photo.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }));
        Assert.True(photo.AsSpan().EndsWith(new byte[] { 0xFF, 0xD9 }));
        Assert.True(photo.Length < original.Length);
    }

    [Fact]
    public void A_png_loses_its_text_exif_and_time_chunks()
    {
        var photo = PlayerPhotoRules.Prepare(Png(withMetadata: true)).Photo!;

        Assert.DoesNotContain("tEXt", Ascii(photo));
        Assert.DoesNotContain("eXIf", Ascii(photo));
        Assert.DoesNotContain("tIME", Ascii(photo));
        Assert.Contains("IHDR", Ascii(photo));
        Assert.Contains("IDAT", Ascii(photo));
        Assert.True(photo.AsSpan().EndsWith(Chunk("IEND", Array.Empty<byte>())));
    }

    [Fact]
    public void A_webp_loses_its_exif_and_xmp_and_says_so_in_its_header()
    {
        var photo = PlayerPhotoRules.Prepare(WebP(withMetadata: true)).Photo!;

        Assert.DoesNotContain("EXIF", Ascii(photo));
        Assert.DoesNotContain("XMP ", Ascii(photo));
        Assert.Contains("VP8 ", Ascii(photo));

        // The extended header no longer announces the chunks that were removed...
        var flags = photo[12 + 8];
        Assert.Equal(0, flags & 0x0C);

        // ...and the container's size is the size of what is left.
        Assert.Equal((uint)(photo.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(photo.AsSpan(4, 4)));
    }

    [Fact]
    public void A_photo_without_metadata_comes_back_byte_for_byte()
    {
        var clean = Png();

        Assert.Equal(clean, PlayerPhotoRules.Prepare(clean).Photo);
    }

    // -----------------------------------------------------------------------------------
    // Files, built by hand
    // -----------------------------------------------------------------------------------

    internal static byte[] Jpeg(bool withMetadata = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        bytes.AddRange(JpegSegment(0xE0, Encoding.ASCII.GetBytes("JFIF\0\u0001\u0001\0\0\u0001\0\u0001\0\0")));

        if (withMetadata)
        {
            bytes.AddRange(JpegSegment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0GPSLatitude 58.14 GPSLongitude 7.99")));
            bytes.AddRange(JpegSegment(0xED, Encoding.ASCII.GetBytes("Photoshop 3.0\08BIM Caption: a player")));
            bytes.AddRange(JpegSegment(0xFE, Encoding.ASCII.GetBytes("Shot by somebody")));
        }

        bytes.AddRange(JpegSegment(0xE2, Encoding.ASCII.GetBytes("ICC_PROFILE\0\u0001\u0001")));
        bytes.AddRange(JpegSegment(0xDB, new byte[65]));
        bytes.AddRange(JpegSegment(0xC0, new byte[] { 8, 0, 1, 0, 1, 1, 1, 0x11, 0 }));
        bytes.AddRange(JpegSegment(0xDA, new byte[] { 1, 1, 0, 0, 0x3F, 0 }));
        bytes.AddRange(Encoding.ASCII.GetBytes("SCANDATA"));
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });

        return bytes.ToArray();
    }

    private static IEnumerable<byte> JpegSegment(byte marker, byte[] data)
    {
        var length = (ushort)(data.Length + 2);

        return new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length }.Concat(data);
    }

    internal static byte[] Png(bool withMetadata = false)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        bytes.AddRange(Chunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 }));

        if (withMetadata)
        {
            bytes.AddRange(Chunk("tEXt", Encoding.ASCII.GetBytes("Author\0Somebody")));
            bytes.AddRange(Chunk("eXIf", Encoding.ASCII.GetBytes("MM\0*GPS")));
            bytes.AddRange(Chunk("tIME", new byte[7]));
        }

        bytes.AddRange(Chunk("IDAT", new byte[] { 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01 }));
        bytes.AddRange(Chunk("IEND", Array.Empty<byte>()));

        return bytes.ToArray();
    }

    /// <summary>A PNG chunk. The CRC is left at zero: the rules copy chunks, they do not check CRCs.</summary>
    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    internal static byte[] WebP(bool withMetadata = false)
    {
        var chunks = new List<byte>();

        // Extended header, announcing EXIF (0x08) and XMP (0x04) when they are there.
        var vp8x = new byte[10];
        vp8x[0] = withMetadata ? (byte)0x0C : (byte)0;
        chunks.AddRange(RiffChunk("VP8X", vp8x));
        chunks.AddRange(RiffChunk("VP8 ", new byte[] { 1, 2, 3, 4, 5 })); // odd: padded

        if (withMetadata)
        {
            chunks.AddRange(RiffChunk("EXIF", Encoding.ASCII.GetBytes("GPSLatitude")));
            chunks.AddRange(RiffChunk("XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta/>")));
        }

        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + chunks.Count));
        file.AddRange(size);
        file.AddRange(Encoding.ASCII.GetBytes("WEBP"));
        file.AddRange(chunks);

        return file.ToArray();
    }

    private static byte[] RiffChunk(string type, byte[] data)
    {
        var padded = data.Length + (data.Length % 2);
        var chunk = new byte[8 + padded];
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);
}
