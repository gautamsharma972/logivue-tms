using System.Globalization;
using System.Text;

namespace Tms.Modules.Shipments.Infrastructure.Export;

/// <summary>
/// A small, dependency-free PDF writer for tabular reports: A4 landscape, the built-in Helvetica fonts (so nothing is embedded and no
/// licence applies), wrapped table cells, repeated headers and page numbers. Text outside basic Latin is transliterated.
/// </summary>
public sealed class SimplePdf
{
    private const double PageWidth = 842, PageHeight = 595, Margin = 36, LineGap = 1.25;

    // Helvetica advance widths for ASCII 32..126, in 1/1000 em.
    private static readonly int[] Widths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private sealed record Page(StringBuilder Content);

    private readonly List<Page> _pages = [];
    private double _y;
    private readonly string _title;
    private readonly string _subtitle;

    public SimplePdf(string title, string subtitle)
    {
        _title = Clean(title);
        _subtitle = Clean(subtitle);
        NewPage();
        Text(Margin, _y - 16, _title, 16, bold: true);
        _y -= 24;
        Text(Margin, _y - 9, _subtitle, 9, grey: true);
        _y -= 22;
    }

    public static string Clean(string? value)
    {
        var sb = new StringBuilder();
        foreach (var c in value ?? string.Empty)
        {
            sb.Append(c switch
            {
                >= ' ' and <= '~' => c,
                '₹' => "Rs ",
                '–' or '—' => '-',
                '·' or '•' => '|',
                '→' => "->",
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '\t' or '\n' or '\r' => ' ',
                _ => '?',
            });
        }

        return sb.ToString();
    }

    public static double Measure(string text, double size, bool bold = false)
    {
        double total = 0;
        foreach (var c in text)
        {
            total += c is >= ' ' and <= '~' ? Widths[c - ' '] : 556;
        }

        return total * size / 1000 * (bold ? 1.06 : 1); // bold Helvetica is slightly wider; this keeps wrapping safe
    }

    public void Heading(string text)
    {
        Ensure(34);
        _y -= 8;
        Text(Margin, _y - 12, Clean(text), 12, bold: true);
        _y -= 20;
    }

    public void Paragraph(string text, double size = 9)
    {
        foreach (var line in Wrap(Clean(text), PageWidth - 2 * Margin, size, false))
        {
            Ensure(size * LineGap + 2);
            Text(Margin, _y - size, line, size);
            _y -= size * LineGap;
        }

        _y -= 4;
    }

    public void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<bool>? rightAligned = null)
    {
        const double size = 8, pad = 3;
        var usable = PageWidth - 2 * Margin;
        var cleanHeaders = headers.Select(Clean).ToList();
        var cleanRows = rows.Select(r => r.Select(Clean).ToList()).ToList();

        // Natural width of each column, capped so one long cell cannot starve the others, then scaled to the page.
        var natural = new double[headers.Count];
        for (var c = 0; c < headers.Count; c++)
        {
            var longest = Math.Max(Measure(cleanHeaders[c], size, true), cleanRows.Count == 0 ? 0 : cleanRows.Max(r => Measure(r[c], size)));
            natural[c] = Math.Min(longest, 220) + 2 * pad;
        }

        var scale = Math.Min(1, usable / natural.Sum());
        var widths = natural.Select(w => w * scale).ToArray();
        if (scale >= 1)
        {
            var extra = (usable - natural.Sum()) / widths.Length;
            widths = widths.Select(w => w + extra).ToArray();
        }

        void Header()
        {
            Ensure(size * LineGap + 2 * pad + 6);
            var h = size * LineGap + 2 * pad;
            Rect(Margin, _y - h, usable, h, 0.92);
            var x = Margin;
            for (var c = 0; c < headers.Count; c++)
            {
                var title = Fit(cleanHeaders[c], widths[c] - 2 * pad, size, true);
                var shift = rightAligned is { } r && c < r.Count && r[c] ? Math.Max(0, widths[c] - 2 * pad - Measure(title, size, true)) : 0;
                Text(x + pad + shift, _y - pad - size, title, size, bold: true);
                x += widths[c];
            }

            _y -= h;
        }

        Header();
        foreach (var row in cleanRows)
        {
            var cells = row.Select((t, c) => Wrap(t, widths[c] - 2 * pad, size, false)).ToList();
            var lines = Math.Max(1, cells.Max(c => c.Count));
            var h = lines * size * LineGap + 2 * pad;
            if (_y - h < Margin + 20)
            {
                NewPage();
                Header();
            }

            var x = Margin;
            for (var c = 0; c < cells.Count; c++)
            {
                for (var l = 0; l < cells[c].Count; l++)
                {
                    var line = cells[c][l];
                    var offset = rightAligned is { } r && c < r.Count && r[c] ? Math.Max(0, widths[c] - 2 * pad - Measure(line, size)) : 0;
                    Text(x + pad + offset, _y - pad - size - l * size * LineGap, line, size);
                }

                x += widths[c];
            }

            _y -= h;
            Line(Margin, _y, Margin + usable, _y);
        }

        _y -= 6;
    }

    public byte[] ToBytes(DateTimeOffset generatedAt)
    {
        var footer = Clean($"Generated {generatedAt:dd MMM yyyy HH:mm} IST");
        for (var i = 0; i < _pages.Count; i++)
        {
            Text(_pages[i], Margin, 20, footer, 8, grey: true);
            var label = $"Page {i + 1} of {_pages.Count}";
            Text(_pages[i], PageWidth - Margin - Measure(label, 8), 20, label, 8, grey: true);
        }

        var objects = new List<string>();
        string Add(string body)
        {
            objects.Add(body);
            return $"{objects.Count} 0 R";
        }

        // 1 catalog, 2 pages, 3 regular font, 4 bold font, then (content, page) pairs.
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add(string.Empty); // pages: filled below
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        var pageRefs = new List<string>();
        foreach (var page in _pages)
        {
            var content = page.Content.ToString();
            var contentRef = Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
            var pageRef = Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentRef} >>");
            pageRefs.Add(pageRef);
        }

        objects[1] = $"<< /Type /Pages /Count {pageRefs.Count} /Kids [{string.Join(' ', pageRefs)}] >>";

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private void NewPage()
    {
        _pages.Add(new Page(new StringBuilder()));
        _y = PageHeight - Margin;
    }

    private void Ensure(double height)
    {
        if (_y - height < Margin + 20)
        {
            NewPage();
        }
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private void Text(double x, double y, string text, double size, bool bold = false, bool grey = false) => Text(_pages[^1], x, y, text, size, bold, grey);

    private static void Text(Page page, double x, double y, string text, double size, bool bold = false, bool grey = false) =>
        page.Content.Append(CultureInfo.InvariantCulture, $"BT {(grey ? "0.4 g" : "0 g")} /{(bold ? "F2" : "F1")} {Num(size)} Tf {Num(x)} {Num(y)} Td ({Escape(text)}) Tj ET\n");

    private void Rect(double x, double y, double w, double h, double grey) =>
        _pages[^1].Content.Append(CultureInfo.InvariantCulture, $"{Num(grey)} g {Num(x)} {Num(y)} {Num(w)} {Num(h)} re f 0 g\n");

    private void Line(double x1, double y, double x2, double _) =>
        _pages[^1].Content.Append(CultureInfo.InvariantCulture, $"0.8 G 0.4 w {Num(x1)} {Num(y)} m {Num(x2)} {Num(y)} l S\n");

    private static string Fit(string text, double width, double size, bool bold)
    {
        if (Measure(text, size, bold) <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && Measure(cut + "..", size, bold) > width)
        {
            cut = cut[..^1];
        }

        return cut + "..";
    }

    private static List<string> Wrap(string text, double width, double size, bool bold)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = word;
            while (Measure(piece, size, bold) > width && piece.Length > 1)
            {
                // A single word wider than the cell is broken at the cell edge.
                var take = piece.Length - 1;
                while (take > 1 && Measure(piece[..take], size, bold) > width)
                {
                    take--;
                }

                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                lines.Add(piece[..take]);
                piece = piece[take..];
            }

            var candidate = current.Length == 0 ? piece : current + " " + piece;
            if (Measure(candidate, size, bold) <= width)
            {
                current.Clear().Append(candidate);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear().Append(piece);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? [string.Empty] : lines;
    }
}
