using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Auth.Infrastructure.Fonts;

/// <summary>
/// Wraps a TrueType or OpenType file (<c>.ttf</c>, <c>.otf</c>) as a WOFF2 file, so the platform
/// stores and serves one format whatever the administrator had at hand (owner, 2026-11-02: fonts
/// often come as <c>VariableFont_wght.ttf</c>).
/// </summary>
/// <remarks>
/// Every table goes in untransformed: <c>glyf</c> and <c>loca</c> with the null transform
/// (version 3, WOFF2 §5.1), the others as they are, and the whole stream compressed with Brotli.
/// The result is then read by <see cref="Woff2Font.Read"/> like any uploaded WOFF2, so nothing
/// this produces is stored without the same check.
/// </remarks>
internal static class SfntFont
{
    private const int HeaderSize = 12;
    private const int RecordSize = 16;

    private sealed record Table(string Tag, int Offset, int Length);

    /// <summary>The file as WOFF2, or null when it is not a whole TrueType or OpenType font.</summary>
    internal static byte[]? ToWoff2(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize)
        {
            return null;
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(file);
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(file[4..]);
        if (!Woff2Font.Flavors.Contains(flavor)
            || numTables is 0 or > Woff2Font.MaxTables
            || HeaderSize + (numTables * RecordSize) > file.Length)
        {
            return null;
        }

        var tables = new List<Table>(numTables);
        long dataSize = 0;
        for (var i = 0; i < numTables; i++)
        {
            var record = file.Slice(HeaderSize + (i * RecordSize), RecordSize);
            var tag = Encoding.ASCII.GetString(record[..4]);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if ((long)offset + length > file.Length || tables.Any(table => table.Tag == tag))
            {
                return null;
            }

            // Tables may point at the same bytes: a crafted directory of a thousand tables, each
            // the whole file, would otherwise be copied a thousand times. Bounded as the reader is.
            dataSize += length;
            if (dataSize > Woff2Font.MaxDecodedBytes)
            {
                return null;
            }

            tables.Add(new Table(tag, (int)offset, (int)length));
        }

        // In tag order, as the sfnt directory itself must be: glyf before loca, as WOFF2 wants.
        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        using var directory = new MemoryStream();
        using var data = new MemoryStream();
        long sfntSize = HeaderSize + ((long)numTables * RecordSize);
        foreach (var table in tables)
        {
            var index = Array.IndexOf(Woff2Font.KnownTags, table.Tag);
            var flags = index >= 0 ? index : 63;
            if (table.Tag is "glyf" or "loca")
            {
                flags |= 3 << 6;
            }

            directory.WriteByte((byte)flags);
            if (index < 0)
            {
                directory.Write(Encoding.ASCII.GetBytes(table.Tag));
            }

            WriteBase128(directory, (uint)table.Length);
            data.Write(file.Slice(table.Offset, table.Length));
            sfntSize += (table.Length + 3) & ~3;
        }

        var stream = data.ToArray();
        var compressed = new byte[BrotliEncoder.GetMaxCompressedLength(stream.Length)];
        if (!BrotliEncoder.TryCompress(stream, compressed, out var compressedLength, quality: 11, window: 22))
        {
            return null;
        }

        // The decoders expect the font data to end on a four-byte boundary (WOFF2 §5.3).
        var dataOffset = 48 + (int)directory.Length;
        var total = (dataOffset + compressedLength + 3) & ~3;
        var woff2 = new byte[total];
        var header = woff2.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x774F4632); // "wOF2"
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], flavor);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], (uint)total);
        BinaryPrimitives.WriteUInt16BigEndian(header[12..], numTables);
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], (uint)sfntSize);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], (uint)compressedLength);
        directory.ToArray().CopyTo(woff2, 48);
        compressed.AsSpan(0, compressedLength).CopyTo(woff2.AsSpan(dataOffset));
        return woff2;
    }

    /// <summary>A UIntBase128 (§4.2): seven bits a byte, most significant first, no leading zero.</summary>
    private static void WriteBase128(Stream stream, uint value)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & 0x7F));
        for (value >>= 7; value != 0; value >>= 7)
        {
            bytes.Push((byte)(0x80 | (value & 0x7F)));
        }

        foreach (var b in bytes)
        {
            stream.WriteByte(b);
        }
    }
}
