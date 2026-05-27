using System.Windows;
using System.Windows.Threading;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 捕获 UI 线程未处理异常，防止闪退
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"发生未预期的错误：\n{args.Exception.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true; // 阻止应用崩溃
            };

            // 捕获非 UI 线程未处理异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                var message = ex?.Message ?? args.ExceptionObject?.ToString() ?? "未知错误";
                // 在 UI 线程上显示消息框
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"发生严重错误：\n{message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            };

            // 捕获未观察到的 Task 异常
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"后台任务发生错误：\n{args.Exception?.InnerException?.Message ?? args.Exception?.Message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                args.SetObserved(); // 阻止进程崩溃
            };
        }
    }

}
