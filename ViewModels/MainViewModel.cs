using System.Collections.ObjectModel;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using Microsoft.Win32;
using ImageConverter.Models;
using ImageConverter.Services;

namespace ImageConverter.ViewModels;

/// <summary>
/// 主窗口 ViewModel，管理图片转换的全部逻辑。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    /// <summary>持久化临时文件根目录，用于崩溃恢复</summary>
    private static readonly string TempRoot = Path.Combine(
        Path.GetTempPath(),
        "Image Converter");

    /// <summary>标记转换中是否发生了错误，用于 finally 判断是否保留临时文件</summary>
    private bool _hasError;

    // ==================== 可绑定属性 ====================

    /// <summary>已选择的文件列表</summary>
    [ObservableProperty]
    private ObservableCollection<ConvertItem> _selectedFiles = [];

    /// <summary>目标格式</summary>
    [ObservableProperty]
    private TargetFormatInfo _targetFormat;

    /// <summary>质量 (0.50 ~ 1.00)，仅对有损格式生效，1.0 = 最高画质</summary>
    [ObservableProperty]
    private double _quality = 1.0;

    /// <summary>ZIP 文件名</summary>
    [ObservableProperty]
    private string _zipName = "converted_images.zip";

    /// <summary>相同格式跳过重编码</summary>
    [ObservableProperty]
    private bool _avoidReencode = true;

    /// <summary>是否正在处理中</summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>进度百分比 (0-100)</summary>
    [ObservableProperty]
    private double _progressValue;

    /// <summary>状态文本</summary>
    [ObservableProperty]
    private string _statusText = "请选择文件或文件夹。";

    /// <summary>总文件数</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>识别为图片数</summary>
    [ObservableProperty]
    private int _imageCount;

    /// <summary>成功转换数</summary>
    [ObservableProperty]
    private int _doneCount;

    /// <summary>跳过文件数</summary>
    [ObservableProperty]
    private int _skipCount;

    /// <summary>失败文件数</summary>
    [ObservableProperty]
    private int _failCount;

    /// <summary>日志文本（追加式）</summary>
    [ObservableProperty]
    private string _logText = string.Empty;

    // ==================== 格式列表 ====================

    /// <summary>所有候选格式（静态定义）</summary>
    private static readonly List<TargetFormatInfo> AllCandidateFormats =
    [
        new TargetFormatInfo("JPG / JPEG（通用、体积小）", "jpg", "image/jpeg", true),
        new TargetFormatInfo("PNG（无损、保留透明）", "png", "image/png", false),
        new TargetFormatInfo("WebP（适合网页/安卓）", "webp", "image/webp", true),
        new TargetFormatInfo("AVIF（新格式，高压缩比）", "avif", "image/avif", true),
        new TargetFormatInfo("BMP（无压缩位图）", "bmp", "image/bmp", false),
        new TargetFormatInfo("TIFF（专业印刷/归档）", "tiff", "image/tiff", false),
        new TargetFormatInfo("HEIC（苹果高效格式）", "heic", "image/heic", true),
    ];

    /// <summary>运行时检测后实际可用的格式列表（过滤掉不支持编码的格式）</summary>
    public static List<TargetFormatInfo> AvailableFormats { get; } =
        AllCandidateFormats
            .Where(f => ImageConvertService.SupportsWritingExtension(f.Extension))
            .ToList();

    // ==================== 构造函数 ====================

    public MainViewModel()
    {
        TargetFormat = AvailableFormats.Count > 0 ? AvailableFormats[0] : AllCandidateFormats[0];

        // 将不可写入的格式打印到调试日志，方便排查
        var unsupported = AllCandidateFormats
            .Where(f => !ImageConvertService.SupportsWritingExtension(f.Extension))
            .ToList();
        if (unsupported.Count > 0)
        {
            var names = string.Join("、", unsupported.Select(f => f.Extension.ToUpperInvariant()));
            System.Diagnostics.Debug.WriteLine(
                $"[ImageConverter] 以下格式在当前环境不支持编码写入：{names}");
        }
    }

    // ==================== 命令 ====================

    /// <summary>选择文件</summary>
    [RelayCommand]
    private void SelectFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图片文件",
            Multiselect = true,
            Filter = "所有图片格式|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.heic;*.heif;*.avif;*.tif;*.tiff;*.cr2;*.dng;*.jxl|所有文件|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            SetFiles(dlg.FileNames, cameFromFolder: false);
        }
    }

    /// <summary>选择文件夹</summary>
    [RelayCommand]
    private void SelectFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择包含图片的文件夹"
        };

        if (dlg.ShowDialog() == true)
        {
            var files = Directory.GetFiles(dlg.FolderName, "*.*", SearchOption.AllDirectories);
            SetFiles(files, cameFromFolder: true, rootFolder: dlg.FolderName);
            Log($"已选择文件夹：{dlg.FolderName}，递归找到 {files.Length} 个文件。", "ok");
        }
    }

    /// <summary>处理拖放文件</summary>
    public void HandleDrop(string[] files, bool cameFromFolder = false)
    {
        SetFiles(files, cameFromFolder);
    }

    /// <summary>只识别格式（不转换）</summary>
    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task ScanOnlyAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ResetStats(resetTotal: false);
        IsProcessing = true;
        Log("开始读取文件头识别真实格式...", "ok");

        int imageCount = 0, skipped = 0, failed = 0;

        try
        {
            for (int i = 0; i < SelectedFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = SelectedFiles[i];

                try
                {
                    var detected = await FileSignatureDetector.DetectAsync(item.InputPath, ct);
                    item.DetectedKind = detected.Kind;
                    item.DetectedLabel = detected.Label;

                    if (FileSignatureDetector.DecodableKinds.Contains(detected.Kind))
                    {
                        imageCount++;
                        item.Status = ConvertStatus.Pending;
                        var note = FormatTrustNote(item.InputPath, detected);
                        Log($"✓ {item.RelativePath} => {detected.Label}{note}", "ok");
                    }
                    else if (FileSignatureDetector.RawKinds.Contains(detected.Kind) || detected.Kind == "jxl")
                    {
                        skipped++;
                        item.Status = ConvertStatus.Skipped;
                        item.Message = $"{detected.Label}：已识别，但可能无法可靠转换。";
                        Log($"! {item.RelativePath} => {detected.Label}；已识别但可能无法解码转换。", "warn");
                    }
                    else
                    {
                        skipped++;
                        item.Status = ConvertStatus.Skipped;
                        item.Message = "非支持图片或未知格式";
                        Log($"- {item.RelativePath} => 非支持图片或未知格式", "warn");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    item.Status = ConvertStatus.Failed;
                    item.Message = ex.Message;
                    Log($"✗ {item.RelativePath} => 识别失败：{ex.Message}", "err");
                }

                UpdateProgress(i + 1, SelectedFiles.Count, $"识别中：{i + 1} / {SelectedFiles.Count}");
                await Task.Delay(1, ct); // 让 UI 有机会刷新
            }
        }
        catch (OperationCanceledException)
        {
            Log("操作已取消。", "warn");
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            Log($"❌ 识别过程发生错误：{ex.Message}", "err");
            StatusText = $"识别失败：{ex.Message}";
            ProgressValue = 0;
        }
        finally
        {
            ImageCount = imageCount;
            SkipCount = skipped;
            FailCount = failed;
            // 只在正常完成时覆盖状态，保留 catch 中设置的错误消息
            if (StatusText == null || !StatusText.StartsWith("识别失败"))
                StatusText = $"识别完成：图片 {imageCount}，跳过 {skipped}，失败 {failed}";
            IsProcessing = false;
            _cts = null;
        }
    }

    /// <summary>开始转换</summary>
    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task StartConvertAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ResetStats(resetTotal: false);
        IsProcessing = true;

        var info = TargetFormat;
        var quality = (uint)Math.Clamp((int)(Quality * 100), 1, 100);
        var targetMagickFormat = ImageConvertService.GetMagickFormat(info.Extension);

        int done = 0, skipped = 0, failed = 0, imageCount = 0;
        var outputItems = new List<(string FilePath, string ZipPath)>();
        var usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var tempDir = Path.Combine(TempRoot, sessionId);
        Directory.CreateDirectory(tempDir);
        bool saved = false; // 追踪是否已成功保存，决定 finally 中是否清理临时文件

        StatusText = "正在转换，请不要关闭窗口。";
        Log($"开始处理 {SelectedFiles.Count} 个文件，目标格式：{info.Extension.ToUpperInvariant()}，质量：{quality}%", "ok");

        try
        {
            for (int i = 0; i < SelectedFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = SelectedFiles[i];

                try
                {
                    // 如果尚未识别，先识别
                    if (item.DetectedKind == "unknown" && item.Status != ConvertStatus.Skipped)
                    {
                        var detected = await FileSignatureDetector.DetectAsync(item.InputPath, ct);
                        item.DetectedKind = detected.Kind;
                        item.DetectedLabel = detected.Label;
                    }

                    // 跳过不可解码的格式
                    if (!FileSignatureDetector.DecodableKinds.Contains(item.DetectedKind))
                    {
                        skipped++;
                        SkipCount = skipped;
                        item.Status = ConvertStatus.Skipped;
                        item.Message = "不是可解码的图片格式";
                        Log($"- 跳过：{item.RelativePath}\n  {item.Message}", "warn");
                        continue;
                    }

                    imageCount++;
                    ImageCount = imageCount;

                    // 同格式跳过
                    if (AvoidReencode && ImageConvertService.IsSameFormat(item.DetectedKind, info.Extension))
                    {
                        skipped++;
                        SkipCount = skipped;
                        item.Status = ConvertStatus.Skipped;
                        item.Message = "已是目标格式，跳过重编码";
                        Log($"- 跳过：{item.RelativePath} => 已是 {info.Extension.ToUpperInvariant()} 格式", "warn");

                        // 仍然复制原文件到输出（用户可能期望它在 ZIP 里）
                        var outRelPath = MakeUniquePath(ReplaceExtension(item.RelativePath, info.Extension), usedOutputPaths);
                        var tempOutPath = Path.Combine(tempDir, outRelPath.Replace('/', Path.DirectorySeparatorChar));
                        var tempOutDir = Path.GetDirectoryName(tempOutPath);
                        if (!string.IsNullOrEmpty(tempOutDir)) Directory.CreateDirectory(tempOutDir);
                        await Task.Run(() => File.Copy(item.InputPath, tempOutPath, overwrite: true), ct);
                        outputItems.Add((tempOutPath, outRelPath));
                        item.OutputPath = tempOutPath;
                        continue;
                    }

                    // 执行转换
                    var outputRelPath = MakeUniquePath(ReplaceExtension(item.RelativePath, info.Extension), usedOutputPaths);
                    var outputFilePath = Path.Combine(tempDir, outputRelPath.Replace('/', Path.DirectorySeparatorChar));

                    await ImageConvertService.ConvertAsync(
                        item.InputPath, outputFilePath, targetMagickFormat, quality, ct);

                    outputItems.Add((outputFilePath, outputRelPath));
                    item.OutputPath = outputFilePath;
                    item.Status = ConvertStatus.Success;
                    done++;
                    DoneCount = done;

                    Log($"✓ {item.RelativePath} [{item.DetectedLabel}] -> {outputRelPath}", "ok");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    FailCount = failed;
                    item.Status = ConvertStatus.Failed;
                    item.Message = ex.Message;
                    Log($"✗ 转换失败：{item.RelativePath}\n  {ex.Message}", "err");
                }

                UpdateProgress(i + 1, SelectedFiles.Count,
                    $"处理中：{i + 1} / {SelectedFiles.Count}，成功 {done}，跳过 {skipped}，失败 {failed}");
                await Task.Delay(1, ct);
            }

            // 输出结果
            if (outputItems.Count == 0)
            {
                StatusText = "没有成功转换的图片，未生成输出文件。";
                Log("没有可下载的输出文件。", "err");
            }
            else if (outputItems.Count > 1)
            {
                // 多文件 → ZIP
                StatusText = "正在生成 ZIP 文件...";
                Log("正在压缩打包...", "ok");

                var saveDlg = new SaveFileDialog
                {
                    Title = "保存 ZIP 文件",
                    Filter = "ZIP 文件|*.zip",
                    FileName = ZipService.NormalizeZipName(ZipName)
                };

                if (saveDlg.ShowDialog() == true)
                {
                    await Task.Run(() => ZipService.CreateZip(outputItems, saveDlg.FileName), ct);
                    saved = true;
                    StatusText = $"完成：成功 {done}，跳过 {skipped}，失败 {failed}，已保存 {saveDlg.FileName}";
                    Log($"完成，已生成：{saveDlg.FileName}", "ok");
                }
                else
                {
                    StatusText = "用户取消了保存。";
                }
            }
            else
            {
                // 单文件 → 直接保存
                var single = outputItems[0];
                var defaultExt = $".{info.Extension}";
                var saveDlg = new SaveFileDialog
                {
                    Title = "保存转换后的图片",
                    Filter = $"{info.DisplayName}|*{defaultExt}",
                    FileName = Path.GetFileName(single.ZipPath)
                };

                if (saveDlg.ShowDialog() == true)
                {
                    File.Copy(single.FilePath, saveDlg.FileName, overwrite: true);
                    saved = true;
                    StatusText = $"完成：已保存 {Path.GetFileName(saveDlg.FileName)}";
                    Log($"完成，已生成：{saveDlg.FileName}", "ok");
                }
                else
                {
                    StatusText = "用户取消了保存。";
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("转换已取消。", "warn");
            StatusText = "操作已取消。";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            _hasError = true;
            Log($"❌ 发生未预期的错误：{ex.Message}", "err");
            StatusText = $"操作失败：{ex.Message}（已转换的文件已保留，重启应用可恢复）";
            ProgressValue = 0;
        }
        finally
        {
            IsProcessing = false;
            _cts = null;

            // 仅在成功保存或用户取消时清理临时目录；出错时保留以便恢复
            if (saved || !_hasError)
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* 忽略清理错误 */ }
            }
            _hasError = false;
        }
    }

    /// <summary>取消当前操作</summary>
    [RelayCommand(CanExecute = nameof(IsProcessing))]
    private void Cancel()
    {
        _cts?.Cancel();
        Log("正在取消...", "warn");
    }

    /// <summary>
    /// 启动时检测上次崩溃遗留的临时文件，提示用户恢复或清理。
    /// </summary>
    public async Task TryRecoverFromCrashAsync()
    {
        if (!Directory.Exists(TempRoot))
            return;

        var sessionDirs = Directory.GetDirectories(TempRoot);
        if (sessionDirs.Length == 0)
            return;

        // 按目录名排序，处理最早的残留会话
        foreach (var dir in sessionDirs.OrderBy(d => d))
        {
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                try { Directory.Delete(dir, true); } catch { }
                continue;
            }

            var totalSize = files.Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            });

            var result = await Task.Run(() =>
                System.Windows.MessageBox.Show(
                    $"检测到上次异常退出的转换结果：\n\n" +
                    $"文件数：{files.Length} 个\n" +
                    $"总大小：{FormatFileSize(totalSize)}\n" +
                    $"时间：{Path.GetFileName(dir)}",
                    "Image Converter — Recovery",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question));

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await RecoverFilesAsync(files, dir);
            }
            else
            {
                // 用户选择不恢复，清理
                try { Directory.Delete(dir, true); } catch { }
            }

            return; // 一次只处理一个会话
        }
    }

    /// <summary>将恢复文件导出到桌面</summary>
    private async Task RecoverFilesAsync(string[] files, string sessionDir)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var recoveryDir = Path.Combine(desktop,
            $"Image Converter_Recovery_{Path.GetFileName(sessionDir)}");
        Directory.CreateDirectory(recoveryDir);

        int copied = 0;
        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var dest = Path.Combine(recoveryDir, fileName);

                // 文件名冲突时追加序号
                if (File.Exists(dest))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    int counter = 2;
                    do
                    {
                        dest = Path.Combine(recoveryDir, $"{name}_{counter}{ext}");
                        counter++;
                    } while (File.Exists(dest));
                }

                File.Copy(file, dest, overwrite: false);
                copied++;
            }
        });

        Log($"已恢复 {copied} 个文件到：{recoveryDir}", "ok");
        StatusText = $"已恢复 {copied} 个文件到桌面。";

        // 恢复完成后清理
        try { Directory.Delete(sessionDir, true); } catch { }
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    /// <summary>清空记录</summary>
    [RelayCommand]
    private void Clear()
    {
        _cts?.Cancel();
        SelectedFiles.Clear();
        ResetStats(resetTotal: true);
        LogText = string.Empty;
        StatusText = "请选择文件或文件夹。";
        IsProcessing = false;

        StartConvertCommand.NotifyCanExecuteChanged();
        ScanOnlyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>打开临时文件目录</summary>
    [RelayCommand]
    private void OpenTempFolder()
    {
        if (!Directory.Exists(TempRoot))
            Directory.CreateDirectory(TempRoot);
        System.Diagnostics.Process.Start("explorer.exe", TempRoot);
    }

    // ==================== 辅助方法 ====================

    private bool CanStartOperation => SelectedFiles.Count > 0 && !IsProcessing;

    private void SetFiles(string[] filePaths, bool cameFromFolder, string? rootFolder = null)
    {
        SelectedFiles.Clear();
        ResetStats(resetTotal: true);
        LogText = string.Empty;

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            string relativePath;
            if (cameFromFolder && rootFolder != null)
            {
                relativePath = Path.GetRelativePath(rootFolder, path).Replace('\\', '/');
            }
            else
            {
                relativePath = Path.GetFileName(path);
            }

            SelectedFiles.Add(new ConvertItem
            {
                InputPath = path,
                RelativePath = relativePath
            });
        }

        TotalCount = SelectedFiles.Count;

        if (SelectedFiles.Count == 0)
        {
            StatusText = "没有选择文件。";
            return;
        }

        StatusText = $"已选择 {SelectedFiles.Count} 个文件。可以先「只识别格式」，也可以直接开始转换。";
        Log($"已选择 {SelectedFiles.Count} 个文件。程序会在处理时读取文件头，不按扩展名或 MIME 筛选。", "ok");

        // 通知命令重新评估 CanExecute（因为 ObservableCollection 的添加不会触发 PropertyChanged）
        StartConvertCommand.NotifyCanExecuteChanged();
        ScanOnlyCommand.NotifyCanExecuteChanged();
    }

    private void ResetStats(bool resetTotal)
    {
        if (resetTotal) TotalCount = SelectedFiles.Count;
        ImageCount = 0;
        DoneCount = 0;
        SkipCount = 0;
        FailCount = 0;
        ProgressValue = 0;
    }

    private void UpdateProgress(int done, int total, string message)
    {
        ProgressValue = total > 0 ? Math.Round((double)done / total * 100, 1) : 0;
        StatusText = message;
    }

    internal void Log(string message, string type = "")
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}";

        // 追加到日志
        LogText += line + Environment.NewLine;

        // 也可以根据类型发送不同的日志级别
        System.Diagnostics.Debug.WriteLine($"[{type}] {line}");
    }

    /// <summary>
    /// 生成信任提示（扩展名 vs 文件头）
    /// </summary>
    private static string FormatTrustNote(string filePath, DetectionResult detected)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var notes = new List<string>();

        if (string.IsNullOrEmpty(ext))
            notes.Add("无后缀");

        if (detected.Kind == "unknown")
            notes.Add("文件头未知");

        if (!string.IsNullOrEmpty(ext) && detected.Kind != "unknown" &&
            !FileSignatureDetector.ExtensionMatchesKind(ext, detected.Kind))
            notes.Add($"扩展名 .{ext} 与文件头不一致");

        return notes.Count > 0 ? $"（{string.Join("；", notes)}）" : "";
    }

    /// <summary>替换路径中的扩展名</summary>
    private static string ReplaceExtension(string path, string newExt)
    {
        var cleanPath = path.Replace('\\', '/');
        var lastSlash = cleanPath.LastIndexOf('/');
        var dir = lastSlash >= 0 ? cleanPath[..(lastSlash + 1)] : "";
        var baseName = lastSlash >= 0 ? cleanPath[(lastSlash + 1)..] : cleanPath;
        var dot = baseName.LastIndexOf('.');
        var nameOnly = dot > 0 ? baseName[..dot] : baseName;
        return dir + SanitizeFileName(string.IsNullOrWhiteSpace(nameOnly) ? "converted" : nameOnly) + "." + newExt;
    }

    /// <summary>清理文件名中的非法字符</summary>
    private static string SanitizeFileName(string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(safe) ? "converted" : safe.Trim();
    }

    /// <summary>生成唯一输出路径，避免 ZIP 内重名冲突</summary>
    private static string MakeUniquePath(string path, HashSet<string> used)
    {
        var candidate = path;
        var counter = 2;
        while (used.Contains(candidate.ToLowerInvariant()))
        {
            var slash = path.LastIndexOf('/');
            var dir = slash >= 0 ? path[..(slash + 1)] : "";
            var baseName = slash >= 0 ? path[(slash + 1)..] : path;
            var dot = baseName.LastIndexOf('.');
            var stem = dot > 0 ? baseName[..dot] : baseName;
            var ext = dot > 0 ? baseName[dot..] : "";
            candidate = $"{dir}{stem}_{counter}{ext}";
            counter++;
        }
        used.Add(candidate.ToLowerInvariant());
        return candidate;
    }
}
