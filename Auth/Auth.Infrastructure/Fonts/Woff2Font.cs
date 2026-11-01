using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Auth.Infrastructure.Fonts;

/// <summary>
/// A structural reading of a WOFF2 file (W3C WOFF File Format 2.0): the header, the table
/// directory, and the font data, which must decompress to exactly what the directory declares.
/// It answers "is this a WOFF2 font that is whole", and the family name it declares.
/// </summary>
/// <remarks>
/// <para>
/// Not a font sanitizer: the browsers run their own (OpenType Sanitizer) on every web font, and
/// a font they refuse falls back to the next family. This check exists so that what the server
/// stores and serves as <c>font/woff2</c> is a WOFF2 file, not any bytes that begin with
/// <c>wOF2</c>: a renamed PNG, a WOFF 1.0, a TrueType file or an HTML page are refused, and so
/// is a file whose compressed data is truncated or garbage.
/// </para>
/// <para>
/// Bounded: the decompressed size is the sum of the declared table lengths, refused above
/// <see cref="MaxDecodedBytes"/> before anything is allocated, and the decoder writes into a
/// buffer of exactly that size, so a file that expands further is refused, not followed.
/// </para>
/// </remarks>
internal static class Woff2Font
{
    /// <summary>The largest decompressed font accepted: a 2 MiB WOFF2 is a few times smaller than its font.</summary>
    internal const int MaxDecodedBytes = 32 * 1024 * 1024;

    private const uint Signature = 0x774F4632; // "wOF2"
    private const int HeaderSize = 48;
    private const int MaxTables = 1024;

    // TrueType outlines, CFF outlines, Apple TrueType. A collection ("ttcf") is not a font file
    // a page can name in one @font-face, so it is refused with everything else.
    private static readonly uint[] Flavors = [0x00010000, 0x4F54544F, 0x74727565];

    // The table tags a directory entry names by index (WOFF2 §5.1, "Known Table Tags"); index 63
    // means the tag follows as four bytes.
    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca",
        "prep", "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea",
        "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL",
        "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat",
        "Gloc", "Feat", "Sill",
    ];

    /// <summary>What a whole WOFF2 file declares.</summary>
    /// <param name="FamilyName">The typographic family (name ID 16), else the family (name ID 1); null when it has neither.</param>
    internal sealed record Info(string? FamilyName);

    private sealed record Table(string Tag, int Offset, int Length, bool Transformed);

    /// <summary>The file's declarations, or null when it is not a whole WOFF2 font.</summary>
    internal static Info? Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32BigEndian(file) != Signature
            || !Flavors.Contains(BinaryPrimitives.ReadUInt32BigEndian(file[4..]))
            || BinaryPrimitives.ReadUInt32BigEndian(file[8..]) != file.Length)
        {
            return null;
        }

        var numTables = BinaryPrimitives.ReadUInt16BigEndian(file[12..]);
        var reserved = BinaryPrimitives.ReadUInt16BigEndian(file[14..]);
        var totalCompressedSize = BinaryPrimitives.ReadUInt32BigEndian(file[20..]);
        if (numTables is 0 or > MaxTables || reserved != 0 || totalCompressedSize == 0)
        {
            return null;
        }

        var position = HeaderSize;
        var tables = new List<Table>(numTables);
        long decodedSize = 0;
        for (var i = 0; i < numTables; i++)
        {
            if (position >= file.Length)
            {
                return null;
            }

            var flags = file[position++];
            var tagIndex = flags & 0x3F;
            string tag;
            if (tagIndex == 63)
            {
                if (position + 4 > file.Length)
                {
                    return null;
                }

                tag = Encoding.ASCII.GetString(file.Slice(position, 4));
                position += 4;
            }
            else if (tagIndex < KnownTags.Length)
            {
                tag = KnownTags[tagIndex];
            }
            else
            {
                return null;
            }

            var version = (flags >> 6) & 0x3;
            if (!TryReadBase128(file, ref position, out var originalLength))
            {
                return null;
            }

            // glyf and loca are transformed unless version 3 says otherwise; every other table is
            // transformed only by a non-zero version (§5.1). A transformed table carries its length.
            var transformed = tag is "glyf" or "loca" ? version != 3 : version != 0;
            var length = originalLength;
            if (transformed && !TryReadBase128(file, ref position, out length))
            {
                return null;
            }

            if (tables.Any(table => table.Tag == tag))
            {
                return null;
            }

            tables.Add(new Table(tag, (int)decodedSize, (int)length, transformed));
            decodedSize += length;
            if (decodedSize > MaxDecodedBytes)
            {
                return null;
            }
        }

        if (!tables.Any(table => table.Tag == "head") || !tables.Any(table => table.Tag == "cmap"))
        {
            return null;
        }

        if (position + (long)totalCompressedSize > file.Length)
        {
            return null;
        }

        var decoded = new byte[decodedSize];
        if (!BrotliDecoder.TryDecompress(file.Slice(position, (int)totalCompressedSize), decoded, out var written)
            || written != decodedSize)
        {
            return null;
        }

        var name = tables.FirstOrDefault(table => table.Tag == "name" && !table.Transformed);
        return new Info(name is null ? null : FamilyName(decoded.AsSpan(name.Offset, name.Length)));
    }

    /// <summary>A UIntBase128 (§4.2): at most five bytes, no leading zero, no overflow.</summary>
    private static bool TryReadBase128(ReadOnlySpan<byte> data, ref int position, out uint value)
    {
        value = 0;
        for (var i = 0; i < 5; i++)
        {
            if (position >= data.Length)
            {
                return false;
            }

            var b = data[position++];
            if ((i == 0 && b == 0x80) || (value & 0xFE000000) != 0)
            {
                return false;
            }

            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The family name in an OpenType <c>name</c> table: the typographic family (16) over the
    /// family (1); a Windows Unicode English record over any Windows Unicode one, over a Unicode
    /// one, over a Macintosh Roman one. Null when the table holds none or does not parse.
    /// </summary>
    private static string? FamilyName(ReadOnlySpan<byte> table)
    {
        if (table.Length < 6)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        var storage = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        string? best = null;
        var bestRank = int.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var record = 6 + (i * 12);
            if (record + 12 > table.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(table[record..]);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 2)..]);
            var language = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 4)..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 6)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 8)..]);
            var offset = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 10)..]);
            if (nameId is not (16 or 1) || storage + offset + length > table.Length)
            {
                continue;
            }

            var source = Rank(platform, encoding, language);
            if (source is null)
            {
                continue;
            }

            // Every typographic family outranks every family name.
            var rank = (nameId == 16 ? 0 : 10) + source.Value.Rank;
            if (rank >= bestRank)
            {
                continue;
            }

            var bytes = table.Slice(storage + offset, length);
            var text = source.Value.Utf16 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            if (!string.IsNullOrWhiteSpace(text))
            {
                best = text;
                bestRank = rank;
            }
        }

        return best;
    }

    private static (int Rank, bool Utf16)? Rank(ushort platform, ushort encoding, ushort language) =>
        (platform, encoding) switch
        {
            (3, 1 or 10) when language == 0x409 => (0, true),
            (3, 1 or 10) => (1, true),
            (0, _) => (2, true),
            (1, 0) => (3, false),
            _ => null,
        };
}
