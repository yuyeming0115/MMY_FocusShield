using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusShield
{
    public partial class ProcessPickerWindow : Window
    {
        public string? SelectedProcessName { get; private set; }

        private List<ProcessItem> _allProcesses = new();

        public ProcessPickerWindow()
        {
            InitializeComponent();
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            _allProcesses.Clear();

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        // 只保留有主窗口的进程（排除服务进程）
                        if (proc.MainWindowHandle != IntPtr.Zero
                            && !string.IsNullOrWhiteSpace(proc.ProcessName))
                        {
                            var icon = ExtractIcon(proc);
                            _allProcesses.Add(new ProcessItem
                            {
                                Icon = icon,
                                ProcessName = proc.ProcessName,
                                WindowTitle = proc.MainWindowTitle ?? ""
                            });
                        }
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }

                // 按进程名排序
                _allProcesses = _allProcesses
                    .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                LstProcesses.ItemsSource = _allProcesses;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadProcesses failed: {ex.Message}");
            }
        }

        // 提取进程图标
        private ImageSource? ExtractIcon(Process proc)
        {
            try
            {
                var mainModule = proc.MainModule;
                if (mainModule == null) return null;

                // 使用完整类型名避免歧义
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(mainModule.FileName);
                if (icon == null) return null;

                // 转换为 WPF ImageSource
                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch { return null; }
        }

        // 搜索过滤
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtSearch.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(filter))
            {
                LstProcesses.ItemsSource = _allProcesses;
            }
            else
            {
                var filtered = _allProcesses
                    .Where(p =>
                        p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || p.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                LstProcesses.ItemsSource = filtered;
            }
        }

        // 双击添加
        private void LstProcesses_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstProcesses.SelectedItem is ProcessItem item)
            {
                SelectedProcessName = item.ProcessName;
                DialogResult = true;
            }
        }

        // 列点击排序
        private void LstProcesses_ColumnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header)
            {
                string? column = header.Content as string;
                if (column == "进程名")
                {
                    _allProcesses = _allProcesses
                        .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    LstProcesses.ItemsSource = _allProcesses;
                }
                else if (column == "窗口标题")
                {
                    _allProcesses = _allProcesses
                        .OrderBy(p => p.WindowTitle, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    LstProcesses.ItemsSource = _allProcesses;
                }
            }
        }

        // 添加按钮
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (LstProcesses.SelectedItem is ProcessItem item)
            {
                SelectedProcessName = item.ProcessName;
                DialogResult = true;
            }
        }

        // 取消按钮
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    // 进程信息数据模型
    public class ProcessItem
    {
        public ImageSource? Icon { get; set; }
        public string ProcessName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
    }
}
