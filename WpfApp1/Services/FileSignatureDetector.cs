using WpfApp1.Models;

namespace WpfApp1.Services;

/// <summary>
/// 通过读取文件头魔数（magic bytes）识别真实图片格式，不依赖扩展名。
/// 逻辑移植自 muban.html 的 detectImageKind()。
/// </summary>
public static class FileSignatureDetector
{
    // JPEG: FF D8 FF
    // PNG:  89 50 4E 47 0D 0A 1A 0A
    // GIF:  GIF87a / GIF89a
    // WebP: RIFF....WEBP
    // BMP:  42 4D
    // TIFF: 49 49 2A 00 (LE) / 4D 4D 00 2A (BE)
    // HEIC/HEIF/AVIF: ....ftyp box (ISO BMFF)
    // JXL:  FF 0A / ....JXL ftyp

    private const int MaxHeaderBytes = 65536;

    /// <summary>已知可解码的图片格式集合</summary>
    public static readonly HashSet<string> DecodableKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpeg", "png", "gif", "webp", "bmp", "heic", "heif", "avif", "tiff"
    };

    /// <summary>CIFF/CR2/DNG 等 RAW 格式也可识别，但转换可能受限于系统编解码器</summary>
    public static readonly HashSet<string> RawKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cr2", "dng", "raw"
    };

    /// <summary>
    /// 检测文件的真实图片格式
    /// </summary>
    public static async Task<DetectionResult> DetectAsync(string filePath, CancellationToken ct = default)
    {
        byte[] buffer = new byte[Math.Min(new FileInfo(filePath).Length, MaxHeaderBytes)];

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        int read = await fs.ReadAsync(buffer, 0, buffer.Length, ct);
        if (read == 0)
            return new DetectionResult("unknown", "Unknown (空文件)");

        var span = buffer.AsSpan(0, read);
        return DetectFromBytes(span);
    }

    /// <summary>
    /// 同步版本，用于已加载到内存的数据
    /// </summary>
    public static DetectionResult DetectFromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return new DetectionResult("unknown", "Unknown");

        // JPEG
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            var ascii = BytesToAscii(bytes);
            var feature = DetectJpegFeature(ascii);
            return new DetectionResult("jpeg", string.IsNullOrEmpty(feature) ? "JPEG" : $"JPEG / {feature}");
        }

        // PNG
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return new DetectionResult("png", "PNG");
        }

        // GIF
        var asciiGif = BytesToAscii(bytes);
        if (asciiGif.StartsWith("GIF87a") || asciiGif.StartsWith("GIF89a"))
            return new DetectionResult("gif", "GIF");

        // WebP (RIFF....WEBP)
        if (asciiGif.StartsWith("RIFF") && asciiGif.Length >= 12 && asciiGif[8..12] == "WEBP")
            return new DetectionResult("webp", "WebP");

        // BMP
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return new DetectionResult("bmp", "BMP");

        // TIFF family
        if (IsTiffHeader(bytes))
        {
            if (IsCanonCr2Header(bytes)) return new DetectionResult("cr2", "Canon CR2 RAW");
            if (IsDngTiff(bytes)) return new DetectionResult("dng", "DNG RAW");
            return new DetectionResult("tiff", "TIFF / RAW-like");
        }

        // JPEG XL
        if (IsJxlHeader(bytes, asciiGif))
            return new DetectionResult("jxl", "JPEG XL");

        // ISO BMFF family (HEIC/HEIF/AVIF)
        if (asciiGif.Length >= 8 && asciiGif[4..8] == "ftyp")
        {
            var brandArea = asciiGif[8..Math.Min(80, asciiGif.Length)].ToLowerInvariant();
            if (brandArea.Contains("avif") || brandArea.Contains("avis"))
                return new DetectionResult("avif", "AVIF");
            if (brandArea.Contains("heic") || brandArea.Contains("heix") ||
                brandArea.Contains("hevc") || brandArea.Contains("hevx") ||
                brandArea.Contains("heif") || brandArea.Contains("heim") ||
                brandArea.Contains("heis") || brandArea.Contains("mif1") ||
                brandArea.Contains("msf1"))
                return new DetectionResult("heic", "HEIC / HEIF");
        }

        return new DetectionResult("unknown", "Unknown");
    }

    private static string BytesToAscii(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] is >= 32 and <= 126 ? (char)bytes[i] : '.';
        return new string(chars);
    }

    private static bool IsTiffHeader(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A));
    }

    private static bool IsCanonCr2Header(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 10 &&
            bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00 &&
            bytes[8] == 0x43 && bytes[9] == 0x52; // "CR"
    }

    private static bool IsDngTiff(ReadOnlySpan<byte> bytes)
    {
        if (!IsTiffHeader(bytes) || bytes.Length < 16)
            return false;

        bool little = bytes[0] == 0x49 && bytes[1] == 0x49;

        int ifdOffset = Read32(bytes, little, 4);
        if (ifdOffset <= 0 || ifdOffset + 2 > bytes.Length) return false;
        int entryCount = Math.Min(Read16(bytes, little, ifdOffset), 512);

        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = ifdOffset + 2 + i * 12;
            if (entryOffset + 12 > bytes.Length) break;
            int tag = Read16(bytes, little, entryOffset);
            // DNGVersion (50706) / DNGBackwardVersion (50707)
            if (tag == 50706 || tag == 50707) return true;
        }
        return false;
    }

    private static string DetectJpegFeature(string ascii)
    {
        var lower = ascii.ToLowerInvariant();
        if (lower.Contains("microvideo") || lower.Contains("motionphoto") || lower.Contains("motion photo"))
            return "Android Motion Photo-like";
        if (lower.Contains("hdrgm") || lower.Contains("gainmap") || lower.Contains("gain map"))
            return "Ultra HDR / gain map-like";
        return string.Empty;
    }

    private static bool IsJxlHeader(ReadOnlySpan<byte> bytes, string ascii)
    {
        return (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0x0A) ||
               (bytes.Length >= 12 && bytes[4] == 0x4A && bytes[5] == 0x58 && bytes[6] == 0x4C && bytes[7] == 0x20 && ascii.Length >= 12 && ascii[8..12] == "ftyp");
    }

    /// <summary>TIFF 小端/大端读取 UInt16</summary>
    private static int Read16(ReadOnlySpan<byte> bytes, bool littleEndian, int offset)
    {
        if (offset + 2 > bytes.Length) return 0;
        return littleEndian
            ? bytes[offset] | (bytes[offset + 1] << 8)
            : (bytes[offset] << 8) | bytes[offset + 1];
    }

    /// <summary>TIFF 小端/大端读取 UInt32</summary>
    private static int Read32(ReadOnlySpan<byte> bytes, bool littleEndian, int offset)
    {
        if (offset + 4 > bytes.Length) return 0;
        return littleEndian
            ? bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)
            : (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    /// <summary>
    /// 检查扩展名是否与检测到的格式匹配
    /// </summary>
    public static bool ExtensionMatchesKind(string ext, string kind)
    {
        var groups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpeg"] = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "jfif", "pjpeg", "pjp" },
            ["png"] = new(StringComparer.OrdinalIgnoreCase) { "png" },
            ["gif"] = new(StringComparer.OrdinalIgnoreCase) { "gif" },
            ["webp"] = new(StringComparer.OrdinalIgnoreCase) { "webp" },
            ["bmp"] = new(StringComparer.OrdinalIgnoreCase) { "bmp", "dib" },
            ["heic"] = new(StringComparer.OrdinalIgnoreCase) { "heic", "heif", "hif" },
            ["heif"] = new(StringComparer.OrdinalIgnoreCase) { "heic", "heif", "hif" },
            ["avif"] = new(StringComparer.OrdinalIgnoreCase) { "avif" },
            ["tiff"] = new(StringComparer.OrdinalIgnoreCase) { "tif", "tiff" },
            ["dng"] = new(StringComparer.OrdinalIgnoreCase) { "dng" },
            ["cr2"] = new(StringComparer.OrdinalIgnoreCase) { "cr2" },
            ["jxl"] = new(StringComparer.OrdinalIgnoreCase) { "jxl" },
        };
        return groups.TryGetValue(kind, out var set) && set.Contains(ext);
    }
}
