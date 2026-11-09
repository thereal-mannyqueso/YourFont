using System.Buffers.Binary;
using System.Text;

namespace FontBook.Infrastructure.Fonts.Files;

/// <summary>Names and style attributes of one face inside a font file.</summary>
/// <param name="FilePath">The font file the face was read from.</param>
/// <param name="FamilyName">Typographic family (name ID 16), falling back to the legacy family (ID 1). Used for grouping.</param>
/// <param name="StyleName">Typographic subfamily (ID 17), falling back to the legacy subfamily (ID 2).</param>
/// <param name="RenderFamilyName">
/// The family name text renderers match on: WWS family (ID 21), else typographic (ID 16), else legacy (ID 1).
/// For "Segoe UI Semibold" this is "Segoe UI"; the weight then selects the face.
/// </param>
/// <param name="Weight">OS/2 usWeightClass, 1–1000.</param>
/// <param name="Width">OS/2 usWidthClass, 1–9.</param>
/// <param name="IsItalic">Italic or oblique.</param>
/// <param name="IbmFamilyClass">OS/2 sFamilyClass high byte (0 = unclassified).</param>
/// <param name="Panose">OS/2 PANOSE bytes (all zeros when not set).</param>
/// <param name="IsFixedPitch">post.isFixedPitch (monospaced).</param>
public sealed record FontFaceData(
    string FilePath,
    string FamilyName,
    string StyleName,
    string RenderFamilyName,
    int Weight,
    int Width,
    bool IsItalic,
    int IbmFamilyClass = 0,
    IReadOnlyList<byte>? Panose = null,
    bool IsFixedPitch = false);

/// <summary>
/// Minimal, read-only OpenType/TrueType reader (TTF, OTF, TTC/OTC collections).
/// </summary>
/// <remarks>
/// It reads only three small tables — <c>name</c> (names), <c>OS/2</c> (weight, width, italic)
/// and <c>head</c> (fallback style bits) — by seeking into the file, so even very large CJK
/// fonts cost a few kilobytes of I/O. Files are opened with <see cref="FileAccess.Read"/> and a
/// permissive share mode: FontBook never writes to, locks, or modifies a font file.
/// Spec: https://learn.microsoft.com/typography/opentype/spec/
/// </remarks>
public static class FontFileReader
{
    private const uint TagTtcf = 0x74746366; // 'ttcf'
    private const uint TagName = 0x6E616D65; // 'name'
    private const uint TagOs2 = 0x4F532F32;  // 'OS/2'
    private const uint TagHead = 0x68656164; // 'head'
    private const uint TagPost = 0x706F7374; // 'post'
    private const uint SfntTrueType = 0x00010000;
    private const uint SfntOpenType = 0x4F54544F; // 'OTTO'
    private const uint SfntApple = 0x74727565;    // 'true'

    /// <summary>File extensions FontBook can read and render.</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>([".ttf", ".otf", ".ttc", ".otc"], StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>Reads every face in the file. Throws <see cref="InvalidDataException"/> for files that aren't valid fonts.</summary>
    public static IReadOnlyList<FontFaceData> ReadFaces(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.RandomAccess);

        var tag = ReadUInt32(stream, 0);
        if (tag == TagTtcf)
        {
            var count = ReadUInt32(stream, 8);
            if (count is 0 or > 1024)
            {
                throw new InvalidDataException("Font collection has an invalid face count.");
            }

            var faces = new List<FontFaceData>((int)count);
            for (var i = 0; i < count; i++)
            {
                faces.Add(ReadFace(stream, path, ReadUInt32(stream, 12 + (i * 4))));
            }

            return faces;
        }

        return [ReadFace(stream, path, 0)];
    }

    private static FontFaceData ReadFace(FileStream stream, string path, long offset)
    {
        var version = ReadUInt32(stream, offset);
        if (version is not (SfntTrueType or SfntOpenType or SfntApple))
        {
            throw new InvalidDataException("Not an OpenType or TrueType font.");
        }

        var numTables = ReadUInt16(stream, offset + 4);
        var directory = ReadBytes(stream, offset + 12, numTables * 16);
        long nameOffset = -1, os2Offset = -1, headOffset = -1, postOffset = -1;
        var postLength = 0;
        int nameLength = 0, os2Length = 0;
        for (var i = 0; i < numTables; i++)
        {
            var record = directory.AsSpan(i * 16, 16);
            var tableTag = BinaryPrimitives.ReadUInt32BigEndian(record);
            var tableOffset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var tableLength = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            switch (tableTag)
            {
                case TagName: nameOffset = tableOffset; nameLength = tableLength; break;
                case TagOs2: os2Offset = tableOffset; os2Length = tableLength; break;
                case TagHead: headOffset = tableOffset; break;
                case TagPost: postOffset = tableOffset; postLength = tableLength; break;
            }
        }

        if (nameOffset < 0)
        {
            throw new InvalidDataException("Font has no name table.");
        }

        var names = ReadNames(ReadBytes(stream, nameOffset, nameLength));
        var legacyFamily = names.GetValueOrDefault(1) ?? Path.GetFileNameWithoutExtension(path);
        var legacyStyle = names.GetValueOrDefault(2) ?? "Regular";
        var family = names.GetValueOrDefault(16) ?? legacyFamily;
        var style = names.GetValueOrDefault(17) ?? legacyStyle;
        var renderFamily = names.GetValueOrDefault(21) ?? family;

        int weight = 400, width = 5, familyClass = 0;
        var italic = false;
        byte[] panose = new byte[10];
        if (os2Offset >= 0 && os2Length >= 64)
        {
            var os2 = ReadBytes(stream, os2Offset, 64);
            weight = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(4));
            width = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(6));
            familyClass = os2[30]; // sFamilyClass: high byte = class, low byte = subclass
            panose = os2.AsSpan(32, 10).ToArray();
            var fsSelection = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(62));
            italic = (fsSelection & 0x0001) != 0 || (fsSelection & 0x0200) != 0; // ITALIC or OBLIQUE
        }
        else if (headOffset >= 0)
        {
            var macStyle = ReadUInt16(stream, headOffset + 44);
            weight = (macStyle & 0x1) != 0 ? 700 : 400;
            italic = (macStyle & 0x2) != 0;
        }

        // Some old fonts use a 1–9 weight scale; normalize to OpenType's 100–900.
        if (weight is > 0 and < 10)
        {
            weight *= 100;
        }

        var fixedPitch = postOffset >= 0 && postLength >= 16 && ReadUInt32(stream, postOffset + 12) != 0;

        return new FontFaceData(path, family.Trim(), style.Trim(), renderFamily.Trim(),
            Math.Clamp(weight == 0 ? 400 : weight, 1, 1000),
            Math.Clamp(width == 0 ? 5 : width, 1, 9),
            italic,
            familyClass,
            panose,
            fixedPitch);
    }

    /// <summary>Picks the best string for each name ID: Windows English first, then any Windows/Unicode, then Mac Roman.</summary>
    private static Dictionary<int, string> ReadNames(byte[] table)
    {
        var result = new Dictionary<int, (int Rank, string Value)>();
        if (table.Length < 6)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(2));
        var storage = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var at = 6 + (i * 12);
            if (at + 12 > table.Length)
            {
                break;
            }

            var record = table.AsSpan(at, 12);
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var offset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if (nameId is not (1 or 2 or 16 or 17 or 21))
            {
                continue;
            }

            var rank = (platform, language) switch
            {
                (3, 0x0409) => 0,
                (3, _) => 1,
                (0, _) => 2,
                (1, 0) when encoding == 0 => 3,
                _ => -1,
            };
            var start = storage + offset;
            if (rank < 0 || start + length > table.Length || length == 0)
            {
                continue;
            }

            var bytes = table.AsSpan(start, length);
            var value = platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
            value = value.TrimEnd('\0').Trim();
            if (value.Length > 0 && (!result.TryGetValue(nameId, out var existing) || rank < existing.Rank))
            {
                result[nameId] = (rank, value);
            }
        }

        return result.ToDictionary(pair => pair.Key, pair => pair.Value.Value);
    }

    private static byte[] ReadBytes(FileStream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > stream.Length)
        {
            throw new InvalidDataException("Font table points outside the file.");
        }

        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static uint ReadUInt32(FileStream stream, long offset) => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(stream, offset, 4));

    private static ushort ReadUInt16(FileStream stream, long offset) => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(stream, offset, 2));
}
