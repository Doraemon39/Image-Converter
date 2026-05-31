using ImageMagick;

namespace ImageConverter.Services;

/// <summary>
/// 基于 Magick.NET 的图片转换服务，支持 100+ 格式互转。
/// </summary>
public static class ImageConvertService
{
    /// <summary>
    /// 将图片转换为指定格式
    /// </summary>
    /// <param name="inputPath">源文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="targetFormat">目标 Magick 格式</param>
    /// <param name="quality">有损格式质量 (1-100)</param>
    /// <param name="ct">取消令牌</param>
    public static async Task ConvertAsync(
        string inputPath,
        string outputPath,
        MagickFormat targetFormat,
        uint quality = 95,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var image = new MagickImage(inputPath);

            // 自动旋转（根据 EXIF 方向）
            image.AutoOrient();

            image.Format = targetFormat;

            // 设置有损格式质量
            if (targetFormat is MagickFormat.Jpeg or MagickFormat.WebP or MagickFormat.Avif or MagickFormat.Heic or MagickFormat.Heif)
            {
                image.Quality = quality;
            }

            // JPEG 不支持透明通道，用白底填充
            if (targetFormat == MagickFormat.Jpeg && image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaOption.Remove);
            }

            // 确保输出目录存在
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            image.Write(outputPath);
        }, ct);
    }

    /// <summary>
    /// 判断源格式与目标格式是否相同（可跳过重编码）
    /// </summary>
    public static bool IsSameFormat(string sourceKind, string targetExt)
    {
        return targetExt.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => sourceKind.Equals("jpeg", StringComparison.OrdinalIgnoreCase),
            "png" => sourceKind.Equals("png", StringComparison.OrdinalIgnoreCase),
            "webp" => sourceKind.Equals("webp", StringComparison.OrdinalIgnoreCase),
            "avif" => sourceKind.Equals("avif", StringComparison.OrdinalIgnoreCase),
            "heic" or "heif" => sourceKind is "heic" or "heif",
            "bmp" => sourceKind.Equals("bmp", StringComparison.OrdinalIgnoreCase),
            "tiff" or "tif" => sourceKind.Equals("tiff", StringComparison.OrdinalIgnoreCase),
            "gif" => sourceKind.Equals("gif", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// 将字符串格式映射到 MagickFormat 枚举
    /// </summary>
    public static MagickFormat GetMagickFormat(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => MagickFormat.Jpeg,
            "png" => MagickFormat.Png,
            "webp" => MagickFormat.WebP,
            "avif" => MagickFormat.Avif,
            "bmp" => MagickFormat.Bmp,
            "gif" => MagickFormat.Gif,
            "tiff" or "tif" => MagickFormat.Tiff,
            "heic" => MagickFormat.Heic,
            "heif" => MagickFormat.Heif,
            _ => MagickFormat.Jpeg
        };
    }

    /// <summary>
    /// 获取文件的像素尺寸
    /// </summary>
    public static (int Width, int Height) GetImageSize(string filePath)
    {
        using var image = new MagickImage();
        image.Ping(filePath);
        return ((int)image.Width, (int)image.Height);
    }
}
