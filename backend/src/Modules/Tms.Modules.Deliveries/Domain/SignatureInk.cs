using System.IO.Compression;

namespace Tms.Modules.Deliveries.Domain;

/// <summary>
/// Tells a drawn signature from a blank canvas by looking at the pixels of the PNG. Only the common 8-bit, non-interlaced formats a browser canvas produces
/// are inspected; any other PNG is given the benefit of the doubt (it is not provably blank).
/// </summary>
public static class SignatureInk
{
    public static bool HasInk(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || png[0] != 0x89 || png[1] != 0x50)
        {
            return false;
        }

        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        var depth = png[24];
        var colorType = png[25];
        var interlace = png[28];
        if (width <= 0 || height <= 0 || width > 8000 || height > 8000)
        {
            return false;
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (depth != 8 || interlace != 0 || channels == 0)
        {
            return true;
        }

        using var compressed = new MemoryStream();
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            if (length < 0 || offset + 12 + length > png.Length)
            {
                return false;
            }

            if (png[offset + 4] == 'I' && png[offset + 5] == 'D' && png[offset + 6] == 'A' && png[offset + 7] == 'T')
            {
                compressed.Write(png.Slice(offset + 8, length));
            }

            offset += 12 + length;
        }

        compressed.Position = 0;
        var stride = (width * channels) + 1;
        var raw = new byte[stride * height];
        try
        {
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            var read = 0;
            while (read < raw.Length)
            {
                var n = inflate.Read(raw, read, raw.Length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (read < raw.Length)
            {
                return false;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var previous = new byte[stride - 1];
        var current = new byte[stride - 1];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * stride];
            for (var x = 0; x < stride - 1; x++)
            {
                var value = raw[(y * stride) + 1 + x];
                var left = x >= channels ? current[x - channels] : (byte)0;
                var up = previous[x];
                var upLeft = x >= channels ? previous[x - channels] : (byte)0;
                current[x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => value,
                };
            }

            for (var x = 0; x < width; x++)
            {
                var p = x * channels;
                var inked = colorType switch
                {
                    6 => current[p + 3] > 16 && !(current[p] > 240 && current[p + 1] > 240 && current[p + 2] > 240),
                    4 => current[p + 1] > 16 && current[p] < 240,
                    2 => !(current[p] > 240 && current[p + 1] > 240 && current[p + 2] > 240),
                    _ => current[p] < 240,
                };
                if (inked)
                {
                    return true;
                }
            }

            (previous, current) = (current, previous);
        }

        return false;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
