using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FocusShield
{
    public partial class App : Application
    {
        private static readonly string LogPath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? "",
                "FocusShield_debug.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            // 全局异常捕获
            this.DispatcherUnhandledException += (s, args) =>
            {
                Log($"【全局异常】{args.Exception}");
                MessageBox.Show(
                    $"FocusShield 启动失败：\n\n{args.Exception.Message}\n\n详情已保存到程序目录 FocusShield_debug.log",
                    "FocusShield 错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
                this.Shutdown();
            };

            Log("=== FocusShield 启动 ===");

            // 手动创建 MainWindow（不调用 Show()，保持隐藏）
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            // 窗口保持隐藏，仅通过光棒双击/托盘打开

            base.OnStartup(e);
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        public void SwitchLanguage(string cultureCode)
        {
            try
            {
                ResourceDictionary dict = new ResourceDictionary();
                string source = $"Resources/Lang.{cultureCode}.xaml";
                Log($"切换语言：{source}");
                dict.Source = new Uri(source, UriKind.Relative);
                this.Resources.MergedDictionaries.Clear();
                this.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                Log($"语言切换失败：{ex.Message}");
            }
        }
    }
}
