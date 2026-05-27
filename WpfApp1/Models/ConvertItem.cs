namespace WpfApp1.Models;

/// <summary>
/// 表示一个待转换或已转换的文件项
/// </summary>
public class ConvertItem
{
    /// <summary>源文件完整路径</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>文件名（含扩展名）</summary>
    public string FileName => Path.GetFileName(InputPath);

    /// <summary>在 ZIP 中的相对路径</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>检测到的真实图片格式</summary>
    public string DetectedKind { get; set; } = "unknown";

    /// <summary>检测到的格式标签（如 "JPEG / Ultra HDR"）</summary>
    public string DetectedLabel { get; set; } = "Unknown";

    /// <summary>转换后的输出路径</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>转换状态</summary>
    public ConvertStatus Status { get; set; } = ConvertStatus.Pending;

    /// <summary>状态消息或错误信息</summary>
    public string Message { get; set; } = string.Empty;
}

public enum ConvertStatus
{
    Pending,
    Success,
    Skipped,
    Failed
}

/// <summary>
/// 支持的输出格式信息
/// </summary>
public record TargetFormatInfo(string DisplayName, string Extension, string MimeType, bool Lossy);

/// <summary>
/// 文件头检测结果
/// </summary>
public record DetectionResult(string Kind, string Label);
