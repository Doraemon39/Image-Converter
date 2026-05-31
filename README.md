# Image Converter 🖼️

一个现代化的 WPF 桌面图片格式转换工具，支持 100+ 图片格式互转，基于文件头魔数（Magic Bytes）识别真实图片格式，而非依赖不可靠的文件扩展名。

![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![WPF](https://img.shields.io/badge/UI-WPF-blue)
![License](https://img.shields.io/badge/License-MIT-green)
![Platform](https://img.shields.io/badge/Platform-Windows-brightgreen)

---

## ✨ 功能特性

### 核心功能

- 📁 **多格式输入** — 支持 JPG、PNG、WebP、AVIF、BMP、TIFF、GIF、HEIC、HEIF、CR2、DNG、JXL 等 100+ 格式
- 🎯 **多格式输出** — JPG、PNG、WebP、AVIF、BMP、TIFF、HEIC
- 🔍 **智能识别** — 通过文件头魔数识别真实格式，不依赖扩展名，准确检测损坏/伪装文件
- 📦 **智能打包** — 单张图片直接保存，多张图片自动打包 ZIP 并保留原始目录结构
- ⏭️ **跳过重编码** — 相同格式可直接复制，避免有损转换

### 输入方式

| 方式 | 说明 |
|------|------|
| 📂 选择文件 | 支持多选，不使用扩展名过滤 |
| 📁 选择文件夹 | 递归扫描所有子文件 |
| 🖱️ 拖放 | 直接拖拽文件/文件夹到窗口 |

### 输出格式一览

| 格式 | 类型 | 适用场景 |
|------|------|----------|
| JPG / JPEG | 有损 | 通用照片，体积小 |
| PNG | 无损 | 截图、设计稿、需保留透明通道 |
| WebP | 有损 | 网页、安卓生态，高压缩率 |
| AVIF | 有损 | 新一代格式，压缩率最高 |
| BMP | 无损 | 无压缩位图 |
| TIFF | 无损 | 专业印刷、归档 |
| HEIC | 有损 | 苹果设备高效格式 |

### 用户体验

- 🎨 **现代化 UI** — 无边框圆角窗口 + 自定义标题栏 + 阴影效果
- 🖥️ **高 DPI 适配** — PerMonitorV2 多显示器 DPI 感知
- 📊 **实时统计** — 已选择、已识别、成功、跳过、失败数量
- 📝 **彩色日志** — 终端风格日志面板，清晰记录每个文件的处理结果
- 💥 **崩溃恢复** — 检测异常退出残留的临时文件并提示恢复
- ⚡ **异步处理** — 后台线程转换，UI 始终保持响应
- 🚫 **取消支持** — 转换过程中可随时取消

---

## 🛠️ 技术架构

### 技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 运行框架 | .NET | 10.0 |
| UI 框架 | WPF (Windows Presentation Foundation) | — |
| 架构模式 | MVVM | CommunityToolkit.Mvvm 8.4.0 |
| 图片处理 | ImageMagick | Magick.NET-Q8-AnyCPU 14.13.1 |
| 压缩打包 | System.IO.Compression | .NET 内置 |

### 项目结构

```
Image-Converter/
├── App.xaml / App.xaml.cs              # 应用入口，全局异常处理（UI 线程 / 非 UI 线程 / Task）
├── MainWindow.xaml / .cs               # 主窗口 UI + 自定义标题栏 + 屏幕自适应
├── AssemblyInfo.cs                      # 程序集信息
├── Models/
│   └── ConvertItem.cs                  # 数据模型：ConvertItem、ConvertStatus、TargetFormatInfo、DetectionResult
├── ViewModels/
│   └── MainViewModel.cs                # 核心 ViewModel：命令、属性、转换逻辑、崩溃恢复
├── Services/
│   ├── ImageConvertService.cs          # 图片转换服务（Magick.NET）
│   ├── FileSignatureDetector.cs        # 文件头魔数识别（Magic Bytes）
│   └── ZipService.cs                   # ZIP 打包服务
├── Properties/
│   └── PublishProfiles/                # 发布配置
├── ImageConverter.slnx                 # 解决方案文件
└── ImageConverter.csproj               # 项目文件
```

### 架构设计

```
┌──────────────────────────────────────────────────────────┐
│                       UI Layer                            │
│   MainWindow.xaml (WPF + WindowChrome 自定义标题栏)        │
│   • 拖放支持、按钮事件、标题栏拖拽/双击                    │
│   • 资源字典：颜色、按钮样式、卡片样式、Badge 样式          │
└──────────────────────┬───────────────────────────────────┘
                       │ Data Binding
┌──────────────────────▼───────────────────────────────────┐
│                    ViewModel Layer                         │
│   MainViewModel (CommunityToolkit.Mvvm)                   │
│   • [ObservableProperty] 属性自动通知                      │
│   • [RelayCommand] 异步命令 + CanExecute 控制              │
│   • 格式列表、质量滑块、进度、统计、日志                    │
└──────────────────────┬───────────────────────────────────┘
                       │ 调用
┌──────────────────────▼───────────────────────────────────┐
│                     Service Layer                          │
│   ├─ FileSignatureDetector   # 文件头魔数识别              │
│   │   JPEG (FF D8 FF) / PNG (89 50 4E 47) / GIF87a/89a  │
│   │   WebP (RIFF....WEBP) / BMP (42 4D) / TIFF /        │
│   │   HEIC/HEIF/AVIF (ftyp box) / JXL / CR2 / DNG       │
│   ├─ ImageConvertService     # 格式转换 + EXIF 自动旋转   │
│   │   JPEG 透明通道白底填充 / 有损格式质量控制             │
│   └─ ZipService              # ZIP 打包 + 文件名安全化     │
└──────────────────────────────────────────────────────────┘
```

---

## 🚀 环境要求

### 开发环境

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10/11 (64-bit) |
| IDE | Visual Studio 2022 17.10+ / VS Code + C# Dev Kit |
| SDK | [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) |

### 运行环境

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10/11 (x64/arm64) |
| 运行时 | .NET Desktop Runtime 10.0（自包含发布时不需要） |

---

## 📦 安装与运行

### 方式一：源码编译

```bash
# 1. 克隆项目
git clone https://github.com/your-username/ImageConverter.git
cd ImageConverter

# 2. 还原依赖
dotnet restore

# 3. 编译并运行（Debug 模式）
dotnet run

# 或 Release 模式
dotnet run -c Release
```

### 方式二：发布为独立应用

```bash
# 生成自包含的独立可执行文件（无需安装运行时）
dotnet publish -c Release -r win-x64 --self-contained

# 生成依赖框架的版本（体积更小，需安装 .NET Desktop Runtime）
dotnet publish -c Release -r win-x64 --no-self-contained
```

发布产物位于：
```
bin\Release\net10.0-windows\win-x64\publish\
```

### 方式三：Visual Studio

1. 打开 `ImageConverter.slnx`
2. 选择 `Release` 配置
3. 按 `F5` 运行，或 `Ctrl+Shift+B` 编译

---

## 📖 使用指南

### 基本操作流程

```
选择输入源 → 选择目标格式 → 调整参数 → 开始转换/识别 → 保存结果
```

### 详细步骤

#### 1. 选择输入

| 操作 | 说明 |
|------|------|
| 点击「选择文件」 | 弹出文件选择框，支持多选 |
| 点击「选择文件夹」 | 弹出文件夹选择框，递归扫描所有子文件 |
| 拖拽文件到窗口 | 支持文件和文件夹，文件夹会递归读取 |

> 💡 **提示**：程序不依赖扩展名过滤，即使文件扩展名错误或缺失也能正确识别。

#### 2. 可选：只识别格式

点击「🔍 只识别格式」按钮，程序会读取每个文件的头部魔数，显示真实格式，但不会执行转换。适用于：
- 批量检查文件格式是否正确
- 确认文件是否为有效的图片

#### 3. 选择输出格式

从下拉框中选择目标格式，勾选「已经是目标格式时不重新压缩」可避免冗余转码。

#### 4. 调整质量（仅对有损格式生效）

- 拖动滑块在 **0.50 ~ 1.00** 之间调节
- `1.00` = 最高画质
- PNG / BMP 为无损格式，此参数不影响

#### 5. 开始转换

点击「🚀 开始转换」：
- **单张图片** → 弹出保存对话框，选择保存位置
- **多张图片** → 弹出 ZIP 保存对话框，默认文件名可在设置中修改
- 转换过程中可点击「⏹ 取消」随时中断

---

## ⚙️ 高级功能

### 文件头魔数识别

程序不依赖文件扩展名，而是读取文件头部的 Magic Bytes 来判断真实格式：

| 格式 | 魔数（十六进制） |
|------|------------------|
| JPEG | `FF D8 FF` |
| PNG | `89 50 4E 47 0D 0A 1A 0A` |
| GIF | `GIF87a` / `GIF89a` ASCII |
| WebP | `RIFF....WEBP` |
| BMP | `42 4D` |
| TIFF | `49 49 2A 00` (LE) / `4D 4D 00 2A` (BE) |
| HEIC/HEIF/AVIF | `ftyp` box (ISO BMFF) |
| JPEG XL | `FF 0A` / `....JXL ftyp` |
| CR2 | TIFF header + Canon signature |
| DNG | TIFF header + DNG signature |

### 崩溃恢复

应用在 `System.IO.Image Converter` 临时目录中存储转换过程中的临时文件。如果程序异常退出：
1. 下次启动时自动检测临时文件
2. 提示用户是否恢复上次未完成的工作

### 全局异常处理

`App.xaml.cs` 中注册了三层异常捕获：
- **UI 线程**：`DispatcherUnhandledException`
- **非 UI 线程**：`AppDomain.CurrentDomain.UnhandledException`
- **后台任务**：`TaskScheduler.UnobservedTaskException`

确保任何异常都不会导致应用无声崩溃。

---

## 🔧 开发指南

### 核心类说明

#### `MainViewModel` — 主视图模型

| 属性/命令 | 类型 | 说明 |
|-----------|------|------|
| `SelectedFiles` | `ObservableCollection<ConvertItem>` | 已选择的文件列表 |
| `TargetFormat` | `TargetFormatInfo` | 目标输出格式 |
| `Quality` | `double` | 质量参数 (0.50-1.00) |
| `AvoidReencode` | `bool` | 相同格式跳过重编码 |
| `IsProcessing` | `bool` | 是否正在处理中 |
| `ProgressValue` | `double` | 进度百分比 (0-100) |
| `LogText` | `string` | 日志文本 |
| `SelectFilesCommand` | `ICommand` | 选择文件 |
| `SelectFolderCommand` | `ICommand` | 选择文件夹 |
| `StartConvertCommand` | `ICommand` | 开始转换 |
| `ScanOnlyCommand` | `ICommand` | 仅识别格式 |
| `CancelCommand` | `ICommand` | 取消操作 |

#### `ImageConvertService` — 图片转换服务

```csharp
// 转换图片
await ImageConvertService.ConvertAsync(
    inputPath, outputPath,
    MagickFormat.Jpeg,
    quality: 95,
    cancellationToken);

// 检查是否同格式
bool same = ImageConvertService.IsSameFormat("jpeg", "jpg");

// 获取格式枚举
MagickFormat fmt = ImageConvertService.GetMagickFormat("webp");

// 获取图片尺寸
var (width, height) = ImageConvertService.GetImageSize(filePath);
```

#### `FileSignatureDetector` — 文件头识别

```csharp
var result = await FileSignatureDetector.DetectAsync(filePath);
// result.Kind:  "jpeg", "png", "webp", "avif", "heic", "tiff", etc.
// result.Label: "JPEG / Ultra HDR", "PNG", "WebP", "Canon CR2 RAW", etc.

// 支持的可解码格式
FileSignatureDetector.DecodableKinds  // jpeg, png, gif, webp, bmp, heic, heif, avif, tiff
FileSignatureDetector.RawKinds        // cr2, dng, raw
```

#### `ZipService` — ZIP 打包

```csharp
var files = new List<(string FilePath, string EntryPath)>
{
    ("C:\output\img1.jpg", "images/photo1.jpg"),
    ("C:\output\img2.png", "images/sub/photo2.png")
};

ZipService.CreateZip(files, "output.zip", CompressionLevel.NoCompression);

string safeName = ZipService.NormalizeZipName("my photos"); // => "my photos.zip"
```

#### `ConvertItem` — 文件项模型

| 属性 | 说明 |
|------|------|
| `InputPath` | 源文件完整路径 |
| `FileName` | 文件名（含扩展名） |
| `RelativePath` | 在 ZIP 中的相对路径 |
| `DetectedKind` | 检测到的真实格式 |
| `DetectedLabel` | 格式标签（如 "JPEG / Ultra HDR"） |
| `OutputPath` | 转换后的输出路径 |
| `Status` | 转换状态 (Pending/Success/Skipped/Failed) |
| `Message` | 状态消息或错误信息 |

### 添加新的输出格式

1. 在 `MainViewModel.AvailableFormats` 中添加格式定义：

```csharp
new TargetFormatInfo("新格式（说明）", "ext", "image/ext", isLossy: true)
```

2. 在 `ImageConvertService.GetMagickFormat` 中添加映射：

```csharp
"ext" or "alias" => MagickFormat.NewFormat,
```

3. 在 `ImageConvertService.IsSameFormat` 中添加同格式判断。

### 添加新的输入格式识别

在 `FileSignatureDetector.DetectFromBytes` 中添加魔数检测逻辑：

```csharp
// 示例：检测新格式
if (bytes.Length >= 4 && bytes[0] == 0xAA && bytes[1] == 0xBB)
    return new DetectionResult("newfmt", "New Format");
```

同时将格式名加入 `DecodableKinds`（可解码）或 `RawKinds`（仅识别）。

---

## 🐛 故障排除

| 问题 | 原因 | 解决方案 |
|------|------|----------|
| `dotnet run` 报错 `CS0234` | NuGet 包未还原 | 运行 `dotnet restore` |
| 应用启动闪退 | 缺少运行时 | 安装 [.NET Desktop Runtime 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) |
| HEIC/AVIF 转换失败 | 缺少编解码器 | 安装 HEIF/AV1 扩展（Microsoft Store） |
| 高 DPI 下界面模糊 | DPI 缩放问题 | 已使用 PerMonitorV2，如仍有问题请调整系统缩放设置 |
| 拖放无反应 | 权限问题 | 以管理员身份运行，或检查安全软件拦截 |

---

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE) 开源。

```
MIT License
Copyright (c) 2026 DoraemonX

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction...
```

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建功能分支：`git checkout -b feature/AmazingFeature`
3. 提交更改：`git commit -m 'Add some AmazingFeature'`
4. 推送分支：`git push origin feature/AmazingFeature`
5. 创建 Pull Request

---

## 🙏 致谢

- [Magick.NET](https://github.com/dlemstra/Magick.NET) — .NET 图片处理封装
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — 轻量 MVVM 框架
- [ImageMagick](https://imagemagick.org/) — 核心图片处理引擎

---

<div align="center">
  <sub>Made with ❤️ by Yuueki XY · Doraemon</sub>
</div>