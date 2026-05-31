using System.IO.Compression;

namespace ImageConverter.Services;

/// <summary>
/// 基于 System.IO.Compression 的 ZIP 打包服务
/// </summary>
public static class ZipService
{
    /// <summary>
    /// 将多个文件打包成 ZIP
    /// </summary>
    /// <param name="files">(文件系统路径, ZIP 内相对路径) 集合</param>
    /// <param name="outputZipPath">输出 ZIP 文件路径</param>
    /// <param name="compressionLevel">压缩级别，图片文件建议 NoCompression（本身已压缩）</param>
    public static void CreateZip(
        IEnumerable<(string FilePath, string EntryPath)> files,
        string outputZipPath,
        CompressionLevel compressionLevel = CompressionLevel.NoCompression)
    {
        var outputDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // 使用 FileMode.Create 确保覆盖已存在的文件
        // （ZipFile.Open + ZipArchiveMode.Create 在某些 .NET 版本中不会自动覆盖）
        using var stream = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (filePath, entryPath) in files)
        {
            if (!File.Exists(filePath)) continue;

            // 避免重复条目
            var normalizedEntry = entryPath.Replace('\\', '/');
            zip.CreateEntryFromFile(filePath, normalizedEntry, compressionLevel);
        }
    }

    /// <summary>
    /// 生成安全的 ZIP 文件名
    /// </summary>
    public static string NormalizeZipName(string name)
    {
        var trimmed = (name ?? "converted_images").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "converted_images";

        // 移除非法字符
        var safe = string.Join("_", trimmed.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "converted_images";

        return safe.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? safe : safe + ".zip";
    }
}
