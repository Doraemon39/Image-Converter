using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageConverter.ViewModels;

namespace ImageConverter
{
    /// <summary>
    /// 主窗口 — 自适应屏幕分辨率、自定义标题栏、居中显示
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();

            // 监听日志文本变化，自动滚动到底部
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        /// <summary>
        /// 窗口加载时自适应屏幕尺寸 — 取屏幕工作区的 80%，居中显示。
        /// 使用 WorkArea 而非 PrimaryScreenWidth/Height，以排除任务栏遮挡。
        /// WPF 坐标系统本身就是 DIP（设备无关像素），因此这里的值已自动适配系统缩放。
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 必须使用工作区，排除任务栏干扰（尤其任务栏在顶部/侧边时）
            var workArea = SystemParameters.WorkArea;

            // 80% 工作区大小，不低于最小限制
            double targetWidth = Math.Max(workArea.Width * 0.80, MinWidth);
            double targetHeight = Math.Max(workArea.Height * 0.80, MinHeight);

            Width = targetWidth;
            Height = targetHeight;

            // 居中于工作区
            Left = workArea.Left + (workArea.Width - targetWidth) / 2;
            Top = workArea.Top + (workArea.Height - targetHeight) / 2;

            // 检测上次崩溃遗留的临时文件，提示恢复
            await ViewModel.TryRecoverFromCrashAsync();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LogText))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogTextBox.ScrollToEnd();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        #region 标题栏事件

        /// <summary>拖拽标题栏移动窗口</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击切换最大化/还原
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        /// <summary>最小化</summary>
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>关闭</summary>
        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>窗口关闭前确保取消所有后台操作，不留残留进程</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            // 取消正在进行的转换/识别操作
            ViewModel.CancelCommand.Execute(null);

            // 确保应用彻底退出
            Application.Current.Shutdown();

            base.OnClosing(e);
        }

        #endregion

        #region 拖放事件处理

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3));
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3));
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;

                bool hasFolder = paths.Any(p => Directory.Exists(p));

                if (hasFolder)
                {
                    var allFiles = new List<string>();
                    foreach (var path in paths)
                    {
                        if (Directory.Exists(path))
                        {
                            allFiles.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories));
                        }
                        else if (File.Exists(path))
                        {
                            allFiles.Add(path);
                        }
                    }
                    ViewModel.HandleDrop([.. allFiles], cameFromFolder: true);
                }
                else
                {
                    ViewModel.HandleDrop(paths, cameFromFolder: false);
                }

                ViewModel.Log($"拖入 {paths.Length} 个条目（含文件夹递归展开）。", "ok");
            }
            e.Handled = true;
        }

        #endregion
    }

    /// <summary>
    /// 布尔值取反转换器
    /// </summary>
    public class InvertBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool b && !b;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool b && !b;
        }
    }
}