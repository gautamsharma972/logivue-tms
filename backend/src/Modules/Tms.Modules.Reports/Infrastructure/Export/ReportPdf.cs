using System.Globalization;
using System.Text;

namespace Tms.Modules.Reports.Infrastructure.Export;

/// <summary>
/// A small dependency-free PDF writer for report tables: landscape A4, Helvetica, a title block, key figures, a table that continues across pages with its header repeated,
/// and page numbers. Text is Latin only: anything else is transliterated, never written raw.
/// </summary>
internal sealed class ReportPdf
{
    private const double PageWidth = 842;
    private const double PageHeight = 595;
    private const double Margin = 36;

    private readonly List<StringBuilder> _pages = [];
    private StringBuilder _page = new();
    private double _y;
    private readonly string _footer;

    public ReportPdf(string footer)
    {
        _footer = footer;
        NewPage();
    }

    public static string Latin(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '₹' => "Rs ", '→' => "->", '←' => "<-", '–' or '—' => "-", '×' => "x", '÷' => "/", '≥' => ">=", '≤' => "<=", '’' or '‘' => "'", '“' or '”' => "\"", '·' or '•' => "-", '…' => "...", ' ' => " ",
                _ when c is >= ' ' and <= '~' => c.ToString(),
                _ => "?",
            });
        }

        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private void NewPage()
    {
        _page = new StringBuilder();
        _pages.Add(_page);
        _y = PageHeight - Margin;
    }

    private void Ensure(double needed)
    {
        if (_y - needed < Margin + 14)
        {
            NewPage();
        }
    }

    private void Text(double x, double y, string text, double size, bool bold = false, double grey = 0)
    {
        _page.Append(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {grey:0.##} g {x:0.##} {y:0.##} Td ({Escape(Latin(text))}) Tj ET\n");
    }

    public void Title(string text, string? subtitle)
    {
        Text(Margin, _y - 14, text, 16, bold: true);
        _y -= 22;
        if (subtitle is not null)
        {
            Text(Margin, _y - 8, subtitle, 8.5, grey: 0.35);
            _y -= 14;
        }

        _y -= 4;
    }

    public void Heading(string text)
    {
        Ensure(24);
        _y -= 6;
        Text(Margin, _y - 10, text, 11, bold: true);
        _y -= 16;
    }

    public void Line(string text, double size = 8.5, bool bold = false)
    {
        foreach (var part in Wrap(text, (int)((PageWidth - (2 * Margin)) / (size * 0.5))))
        {
            Ensure(size + 4);
            Text(Margin, _y - size, part, size, bold);
            _y -= size + 3.5;
        }
    }

    public void Pairs(IReadOnlyList<(string Label, string Value)> items, int columns = 3)
    {
        var width = (PageWidth - (2 * Margin)) / columns;
        for (var i = 0; i < items.Count; i += columns)
        {
            Ensure(26);
            for (var c = 0; c < columns && i + c < items.Count; c++)
            {
                var (label, value) = items[i + c];
                Text(Margin + (c * width), _y - 8, Clip(label, (int)(width / 4.2)), 7, grey: 0.4);
                Text(Margin + (c * width), _y - 20, Clip(value, (int)(width / 5.2)), 10, bold: true);
            }

            _y -= 28;
        }
    }

    public void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<bool> rightAligned)
    {
        if (headers.Count == 0)
        {
            return;
        }

        var total = PageWidth - (2 * Margin);
        var size = headers.Count > 12 ? 6 : headers.Count > 9 ? 6.5 : 7.5;
        var charWidth = size * 0.52;
        var natural = headers.Select((h, c) => Math.Min(40, Math.Max(h.Length, rows.Take(200).Select(r => r[c].Length).DefaultIfEmpty(0).Max())) * charWidth + 8).ToList();
        var scale = natural.Sum() > total ? total / natural.Sum() : 1;
        var widths = natural.Select(w => w * scale).ToList();
        var rowHeight = size + 5;

        void Header()
        {
            Ensure(rowHeight * 3);
            _page.Append(CultureInfo.InvariantCulture, $"0.93 g {Margin:0.##} {_y - rowHeight:0.##} {widths.Sum():0.##} {rowHeight:0.##} re f\n");
            var x = Margin;
            for (var c = 0; c < headers.Count; c++)
            {
                Text(x + 3, _y - size - 1.5, Clip(headers[c], (int)(widths[c] / charWidth)), size, bold: true);
                x += widths[c];
            }

            _y -= rowHeight;
        }

        Header();
        for (var r = 0; r < rows.Count; r++)
        {
            if (_y - rowHeight < Margin + 14)
            {
                NewPage();
                Header();
            }

            if (r % 2 == 1)
            {
                _page.Append(CultureInfo.InvariantCulture, $"0.975 g {Margin:0.##} {_y - rowHeight:0.##} {widths.Sum():0.##} {rowHeight:0.##} re f\n");
            }

            var x = Margin;
            for (var c = 0; c < headers.Count; c++)
            {
                var text = Clip(rows[r][c], Math.Max(1, (int)(widths[c] / charWidth) - 1));
                var tx = rightAligned[c] ? x + widths[c] - 3 - (text.Length * charWidth) : x + 3;
                Text(Math.Max(x + 1, tx), _y - size - 1.5, text, size);
                x += widths[c];
            }

            _y -= rowHeight;
        }

        _y -= 6;
    }

    private static string Clip(string text, int max) => max <= 1 ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "~";

    private static IEnumerable<string> Wrap(string text, int width)
    {
        text = Latin(text);
        if (text.Length <= width)
        {
            yield return text;
            yield break;
        }

        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }

            line.Append(line.Length == 0 ? string.Empty : " ").Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    public byte[] ToBytes()
    {
        var objects = new List<string>();
        var count = _pages.Count;
        // 1 catalog, 2 pages, 3 helvetica, 4 helvetica-bold, then page + content per page
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add("<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, count).Select(i => $"{5 + (i * 2)} 0 R")) + $"] /Count {count} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < count; i++)
        {
            var content = new StringBuilder(_pages[i].ToString());
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 7 Tf 0.4 g {Margin} 20 Td ({Escape(Latin(_footer))}) Tj ET\n");
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 7 Tf 0.4 g {PageWidth - Margin - 50} 20 Td (Page {i + 1} of {count}) Tj ET\n");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + (i * 2)} 0 R >>");
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content.ToString())} >>\nstream\n{content}endstream");
        }

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
        {
            Write($"{o:0000000000} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
