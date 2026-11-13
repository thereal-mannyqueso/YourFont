using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FontBook.Core.FontMaker;

namespace FontBook.Infrastructure.FontMaker;

/// <summary>
/// Writes a minimal, valid TrueType font: the ten required tables
/// (OS/2, cmap, glyf, head, hhea, hmtx, loca, maxp, name, post), no hinting.
/// Spec: https://learn.microsoft.com/typography/opentype/spec/
/// </summary>
public sealed class TrueTypeFontBuilder : IFontFileBuilder
{
    private const int UnitsPerEm = 1000;
    private const int SpaceAdvance = 300;

    public byte[] Build(FontDefinition font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(font.FamilyName);

        // Glyph order: .notdef (required first), space, then the drawn characters.
        var glyphs = new List<GlyphOutline> { NotDef(), new(' ', SpaceAdvance, []) };
        glyphs.AddRange(font.Glyphs.Where(g => g.Character != ' ').DistinctBy(g => g.Character).OrderBy(g => g.Character));

        var metrics = glyphs.Select(Measure).ToList();
        var (glyf, loca) = BuildGlyf(glyphs);
        var inked = metrics.Where(m => m.HasContours).ToList();
        int xMin = inked.Min(m => m.XMin), yMin = inked.Min(m => m.YMin), xMax = inked.Max(m => m.XMax), yMax = inked.Max(m => m.YMax);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["OS/2"] = BuildOs2(font, glyphs, yMin, yMax),
            ["cmap"] = BuildCmap(glyphs),
            ["glyf"] = glyf,
            ["head"] = BuildHead(xMin, yMin, xMax, yMax),
            ["hhea"] = BuildHhea(font, glyphs, metrics),
            ["hmtx"] = BuildHmtx(glyphs, metrics),
            ["loca"] = loca,
            ["maxp"] = BuildMaxp(glyphs),
            ["name"] = BuildName(font.FamilyName),
            ["post"] = BuildPost(),
        };

        return Assemble(tables);
    }

    private sealed record GlyphMetrics(bool HasContours, int XMin, int YMin, int XMax, int YMax);

    private static GlyphMetrics Measure(GlyphOutline glyph)
    {
        var points = glyph.Contours.SelectMany(c => c).ToList();
        return points.Count == 0
            ? new GlyphMetrics(false, 0, 0, 0, 0)
            : new GlyphMetrics(true, points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    /// <summary>The required "missing character" glyph: a hollow box.</summary>
    private static GlyphOutline NotDef()
    {
        OutlinePoint P(int x, int y) => new(x, y, OnCurve: true);
        return new GlyphOutline('\0', 500,
        [
            [P(50, 0), P(50, 700), P(450, 700), P(450, 0)],       // outer, clockwise
            [P(100, 50), P(400, 50), P(400, 650), P(100, 650)],   // hole, counter-clockwise
        ]);
    }

    private static (byte[] Glyf, byte[] Loca) BuildGlyf(List<GlyphOutline> glyphs)
    {
        var glyf = new Writer();
        var loca = new Writer();
        foreach (var glyph in glyphs)
        {
            loca.U32((uint)glyf.Length);
            if (glyph.Contours.Count == 0)
            {
                continue; // empty glyph (e.g. space): zero-length entry
            }

            var points = glyph.Contours.SelectMany(c => c).ToList();
            glyf.I16((short)glyph.Contours.Count);
            glyf.I16((short)points.Min(p => p.X));
            glyf.I16((short)points.Min(p => p.Y));
            glyf.I16((short)points.Max(p => p.X));
            glyf.I16((short)points.Max(p => p.Y));
            var end = -1;
            foreach (var contour in glyph.Contours)
            {
                end += contour.Count;
                glyf.U16((ushort)end);
            }

            glyf.U16(0); // no hinting instructions
            foreach (var p in points)
            {
                glyf.U8((byte)(p.OnCurve ? 1 : 0)); // coordinates follow as 16-bit deltas
            }

            int previousX = 0, previousY = 0;
            foreach (var p in points)
            {
                glyf.I16((short)(p.X - previousX));
                previousX = p.X;
            }

            foreach (var p in points)
            {
                glyf.I16((short)(p.Y - previousY));
                previousY = p.Y;
            }

            glyf.Pad4();
        }

        loca.U32((uint)glyf.Length);
        return (glyf.ToArray(), loca.ToArray());
    }

    private static byte[] BuildHead(int xMin, int yMin, int xMax, int yMax)
    {
        var seconds = (long)(DateTime.UtcNow - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        var w = new Writer();
        w.U16(1); w.U16(0);           // version
        w.U32(0x00010000);             // fontRevision 1.0
        w.U32(0);                      // checksumAdjustment (filled in last)
        w.U32(0x5F0F3CF5);             // magic number
        w.U16(0x0009);                 // flags: baseline at y=0, integer scaling
        w.U16(UnitsPerEm);
        w.I64(seconds); w.I64(seconds); // created, modified
        w.I16((short)xMin); w.I16((short)yMin); w.I16((short)xMax); w.I16((short)yMax);
        w.U16(0);                      // macStyle: regular
        w.U16(8);                      // lowestRecPPEM
        w.I16(2);                      // fontDirectionHint
        w.I16(1);                      // indexToLocFormat: 32-bit loca
        w.I16(0);                      // glyphDataFormat
        return w.ToArray();
    }

    private static byte[] BuildHhea(FontDefinition font, List<GlyphOutline> glyphs, List<GlyphMetrics> metrics)
    {
        var inked = glyphs.Zip(metrics).Where(x => x.Second.HasContours).ToList();
        var w = new Writer();
        w.U16(1); w.U16(0);
        w.I16((short)font.Ascent);
        w.I16((short)-font.Descent);
        w.I16(0);                                                           // lineGap
        w.U16((ushort)glyphs.Max(g => g.AdvanceWidth));                     // advanceWidthMax
        w.I16((short)inked.Min(x => x.Second.XMin));                        // minLeftSideBearing
        w.I16((short)inked.Min(x => x.First.AdvanceWidth - x.Second.XMax)); // minRightSideBearing
        w.I16((short)inked.Max(x => x.Second.XMax));                        // xMaxExtent
        w.I16(1); w.I16(0); w.I16(0);                                       // caret slope rise/run, offset
        w.I16(0); w.I16(0); w.I16(0); w.I16(0);                             // reserved
        w.I16(0);                                                           // metricDataFormat
        w.U16((ushort)glyphs.Count);                                        // numberOfHMetrics
        return w.ToArray();
    }

    private static byte[] BuildHmtx(List<GlyphOutline> glyphs, List<GlyphMetrics> metrics)
    {
        var w = new Writer();
        for (var i = 0; i < glyphs.Count; i++)
        {
            w.U16((ushort)glyphs[i].AdvanceWidth);
            w.I16((short)metrics[i].XMin);
        }

        return w.ToArray();
    }

    private static byte[] BuildMaxp(List<GlyphOutline> glyphs)
    {
        var w = new Writer();
        w.U32(0x00010000);
        w.U16((ushort)glyphs.Count);
        w.U16((ushort)glyphs.Max(g => g.Contours.Sum(c => c.Count)));   // maxPoints
        w.U16((ushort)glyphs.Max(g => g.Contours.Count));               // maxContours
        w.U16(0); w.U16(0);                                             // composite points/contours
        w.U16(2);                                                       // maxZones
        for (var i = 0; i < 9; i++)
        {
            w.U16(0);                                                   // twilight, storage, functions, ... component depth
        }

        return w.ToArray();
    }

    private static byte[] BuildOs2(FontDefinition font, List<GlyphOutline> glyphs, int yMin, int yMax)
    {
        var drawn = glyphs.Skip(1).ToList();
        var w = new Writer();
        w.U16(4);                                                       // version
        w.I16((short)drawn.Average(g => g.AdvanceWidth));               // xAvgCharWidth
        w.U16(400);                                                     // usWeightClass: regular
        w.U16(5);                                                       // usWidthClass: normal
        w.U16(0);                                                       // fsType: installable, no restrictions
        foreach (var value in new short[] { 650, 600, 0, 75, 650, 600, 0, 350, 50, 300 })
        {
            w.I16(value);                                               // sub/superscript and strikeout metrics
        }

        w.I16(0x0A00);                                                  // sFamilyClass: script
        w.Bytes([3, 0, 0, 0, 0, 0, 0, 0, 0, 0]);                        // PANOSE: Latin hand written
        w.U32(1); w.U32(0); w.U32(0); w.U32(0);                         // Unicode ranges: Basic Latin
        w.Bytes("FBMK"u8.ToArray());                                    // vendor ID
        w.U16(0x00C0);                                                  // fsSelection: REGULAR | USE_TYPO_METRICS
        var codes = glyphs.Where(g => g.Character != '\0').Select(g => (int)g.Character).ToList();
        w.U16((ushort)codes.Min());
        w.U16((ushort)codes.Max());
        w.I16((short)font.Ascent);                                      // sTypoAscender
        w.I16((short)-font.Descent);                                    // sTypoDescender
        w.I16(0);                                                       // sTypoLineGap
        w.U16((ushort)Math.Max(font.Ascent, yMax));                     // usWinAscent
        w.U16((ushort)Math.Max(font.Descent, -yMin));                   // usWinDescent
        w.U32(1); w.U32(0);                                             // code pages: Latin 1
        w.I16((short)(font.Ascent * 0.6));                              // sxHeight (estimate)
        w.I16((short)(font.Ascent * 0.85));                             // sCapHeight (estimate)
        w.U16(0); w.U16(32); w.U16(0);                                  // default char, break char, max context
        return w.ToArray();
    }

    private static byte[] BuildCmap(List<GlyphOutline> glyphs)
    {
        // Format 4 subtable: one segment per run of consecutive characters with consecutive glyph IDs.
        var mapped = glyphs.Select((g, id) => (Code: (int)g.Character, Id: id)).Where(x => x.Code != 0).OrderBy(x => x.Code).ToList();
        var segments = new List<(int Start, int End, int Delta)>();
        foreach (var (code, id) in mapped)
        {
            var delta = id - code;
            if (segments.Count > 0 && segments[^1].End == code - 1 && segments[^1].Delta == delta)
            {
                segments[^1] = (segments[^1].Start, code, delta);
            }
            else
            {
                segments.Add((code, code, delta));
            }
        }

        segments.Add((0xFFFF, 0xFFFF, 1));
        var segCount = segments.Count;
        var power = (int)Math.Pow(2, Math.Floor(Math.Log2(segCount)));

        var sub = new Writer();
        sub.U16(4);
        sub.U16((ushort)(16 + (8 * segCount)));   // length
        sub.U16(0);                                // language
        sub.U16((ushort)(segCount * 2));
        sub.U16((ushort)(power * 2));              // searchRange
        sub.U16((ushort)Math.Log2(power));         // entrySelector
        sub.U16((ushort)((segCount * 2) - (power * 2)));
        segments.ForEach(s => sub.U16((ushort)s.End));
        sub.U16(0);                                // reservedPad
        segments.ForEach(s => sub.U16((ushort)s.Start));
        segments.ForEach(s => sub.U16((ushort)(s.Delta & 0xFFFF)));
        segments.ForEach(_ => sub.U16(0));         // idRangeOffset

        var w = new Writer();
        w.U16(0); w.U16(2);                        // version, two encoding records sharing the subtable
        w.U16(0); w.U16(3); w.U32(20);             // Unicode BMP
        w.U16(3); w.U16(1); w.U32(20);             // Windows Unicode BMP
        w.Bytes(sub.ToArray());
        return w.ToArray();
    }

    private static byte[] BuildName(string family)
    {
        var postScript = new string([.. family.Where(char.IsAsciiLetterOrDigit)]);
        if (postScript.Length == 0)
        {
            postScript = "Handwriting";
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        (int Id, string Value)[] names =
        [
            (1, family),
            (2, "Regular"),
            (3, $"FontBook:{postScript}:{stamp}"),
            (4, family),
            (5, "Version 1.000"),
            (6, postScript.Length > 50 ? postScript[..50] + "-Regular" : postScript + "-Regular"),
        ];

        var strings = new Writer();
        var w = new Writer();
        w.U16(0);                                  // format
        w.U16((ushort)names.Length);
        w.U16((ushort)(6 + (12 * names.Length)));  // string storage offset
        foreach (var (id, value) in names)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(value);
            w.U16(3); w.U16(1); w.U16(0x0409);     // Windows, Unicode BMP, English (US)
            w.U16((ushort)id);
            w.U16((ushort)bytes.Length);
            w.U16((ushort)strings.Length);
            strings.Bytes(bytes);
        }

        w.Bytes(strings.ToArray());
        return w.ToArray();
    }

    private static byte[] BuildPost()
    {
        var w = new Writer();
        w.U32(0x00030000);                         // version 3: no glyph names
        w.U32(0);                                  // italicAngle
        w.I16(-100);                               // underlinePosition
        w.I16(50);                                 // underlineThickness
        w.U32(0);                                  // isFixedPitch
        w.U32(0); w.U32(0); w.U32(0); w.U32(0);    // memory usage hints
        return w.ToArray();
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var power = (int)Math.Pow(2, Math.Floor(Math.Log2(count)));
        var w = new Writer();
        w.U32(0x00010000);                         // TrueType outlines
        w.U16((ushort)count);
        w.U16((ushort)(power * 16));
        w.U16((ushort)Math.Log2(power));
        w.U16((ushort)((count * 16) - (power * 16)));

        var offset = 12 + (16 * count);
        var headOffset = 0;
        foreach (var (tag, data) in tables)
        {
            w.Bytes(Encoding.ASCII.GetBytes(tag));
            w.U32(Checksum(data));
            w.U32((uint)offset);
            w.U32((uint)data.Length);
            if (tag == "head")
            {
                headOffset = offset;
            }

            offset += (data.Length + 3) & ~3;
        }

        foreach (var data in tables.Values)
        {
            w.Bytes(data);
            w.Pad4();
        }

        var file = w.ToArray();
        var adjustment = unchecked(0xB1B0AFBA - Checksum(file));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headOffset + 8), adjustment);
        return file;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i += 4)
        {
            uint word = 0;
            for (var j = 0; j < 4; j++)
            {
                word = (word << 8) | (i + j < data.Length ? data[i + j] : 0u);
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }

    /// <summary>Big-endian binary writer (fonts store numbers most-significant byte first).</summary>
    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public int Length => _bytes.Count;

        public void U8(byte value) => _bytes.Add(value);

        public void U16(int value)
        {
            var b = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value);
            _bytes.AddRange(b);
        }

        public void I16(int value)
        {
            var b = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(b, (short)value);
            _bytes.AddRange(b);
        }

        public void U32(uint value)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, value);
            _bytes.AddRange(b);
        }

        public void I64(long value)
        {
            var b = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(b, value);
            _bytes.AddRange(b);
        }

        public void Bytes(byte[] data) => _bytes.AddRange(data);

        public void Pad4()
        {
            while (_bytes.Count % 4 != 0)
            {
                _bytes.Add(0);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
