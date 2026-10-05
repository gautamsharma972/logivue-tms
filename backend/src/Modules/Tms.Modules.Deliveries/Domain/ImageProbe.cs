namespace Tms.Modules.Deliveries.Domain;

/// <summary>Reads the size of a PNG or JPEG from its headers, without decoding it, so a corrupt or tiny image can be flagged on upload.</summary>
public static class ImageProbe
{
    /// <returns>Width and height, or null when the bytes are not a readable PNG / JPEG.</returns>
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            // IHDR follows the 8-byte signature and 8 bytes of chunk length + type.
            var w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            var h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return w > 0 && h > 0 ? (w, h) : null;
        }

        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                var marker = data[i + 1];
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
                {
                    i += 2;
                    continue;
                }

                var length = (data[i + 2] << 8) | data[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    var h = (data[i + 5] << 8) | data[i + 6];
                    var w = (data[i + 7] << 8) | data[i + 8];
                    return w > 0 && h > 0 ? (w, h) : null;
                }

                if (length < 2)
                {
                    return null;
                }

                i += 2 + length;
            }
        }

        return null;
    }
}
