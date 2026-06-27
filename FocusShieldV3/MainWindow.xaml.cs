using System;
using System.Runtime.InteropServices; // 必须引用！
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Media;
using Microsoft.Win32; // 用于注册表操作(开机自启)
using FocusShield.Core;
using Hardcodet.Wpf.TaskbarNotification; // 托盘库

namespace FocusShield
{
    public partial class MainWindow : Window
    {
        private InputGuard _guard;
        private OverlayWindow _overlay; // 光盾窗口
        private bool _isRealExit = false; // 防止点击X直接退出

        // 固定的快捷键 ID
        private const int HOTKEY_ID = 9000;
        private const int HOTKEY_SETTINGS_ID = 9001;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                App.Log("MainWindow constructor start");

                // 0. 设置图标（优先从内嵌资源读取，兼容单文件发布模式）
                try
                {
                    System.Drawing.Icon? sdIcon = null;

                    // 策略1：从程序集内嵌资源读取（单文件发布模式首选）
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("FocusShield.ico"))
                    {
                        if (stream != null)
                        {
                            sdIcon = new System.Drawing.Icon(stream);
                            App.Log("Icon loaded from embedded resource");
                        }
                    }

                    // 策略2：从文件路径回退（开发模式）
                    if (sdIcon == null)
                    {
                        string? exeDir = System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                        if (exeDir != null)
                        {
                            string icoPath = System.IO.Path.Combine(exeDir, "FocusShield.ico");
                            if (System.IO.File.Exists(icoPath))
                            {
                                sdIcon = new System.Drawing.Icon(icoPath);
                                App.Log("Icon loaded from file path");
                            }
                        }
                    }

                    if (sdIcon != null)
                    {
                        // 窗口标题栏图标（WPF ImageSource）
                        var wpfIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            sdIcon.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        this.Icon = wpfIcon;
                        // 托盘图标（Hardcodet 直接接受 System.Drawing.Icon）
                        TrayIcon.Icon = sdIcon; // 注意：托盘持有引用，此处不 Dispose
                        App.Log("Icon applied OK");
                    }
                }
                catch (Exception ex) { App.Log($"Icon load skipped: {ex.Message}"); }

                // 1. 加载配置
                ConfigManager.Load();
                App.Log("ConfigManager.Load() done");

                // 2. 初始化核心卫士
                _guard = new InputGuard();
                App.Log("InputGuard created");

                // 3. 初始化光盾窗口
                _overlay = new OverlayWindow();
                App.Log("OverlayWindow created");
                try { _overlay.SetColor(ConfigManager.Current.GlowColor); } catch (Exception ex) { App.Log($"SetColor failed: {ex.Message}"); }

                // 订阅光盾事件
                _overlay.OnToggleRequested += () =>
                {
                    App.Log("ToggleRequested");
                    _guard.ToggleLock();
                    UpdateStatusUI();
                    PlaySoundEffect(_guard.IsLocked);
                };
                _overlay.OnSettingsRequested += () =>
                {
                    App.Log("SettingsRequested - opening window");
                    try
                    {
                        this.Show();
                        this.WindowState = WindowState.Normal;
                        // 确保窗口在屏幕可见区域
                        if (this.Left < 0 || this.Top < 0 ||
                            this.Left > SystemParameters.WorkArea.Width ||
                            this.Top > SystemParameters.WorkArea.Height)
                        {
                            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        }
                        this.Activate();
                        App.Log("Settings window shown");
                    }
                    catch (Exception ex)
                    {
                        App.Log($"Settings window error: {ex}");
                    }
                };
                _overlay.OnExitRequested += () =>
                {
                    App.Log("ExitRequested");
                    BtnQuit_Click(this, new RoutedEventArgs());
                };

                // 4. 初始化界面
                InitUI();
                CheckAutoStartStatus();
                App.Log("InitUI done");

                // 5. 默认开启专注模式
                _guard.ToggleLock();
                App.Log($"ToggleLock done, IsLocked={_guard.IsLocked}");
                UpdateStatusUI();
                App.Log("UpdateStatusUI done");
            }
            catch (Exception ex)
            {
                App.Log($"CRASH in constructor: {ex}");
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FocusShield_error.log"),
                    $"[{DateTime.Now}] {ex}\n\n");
                throw;
            }
        }

        // Window_Loaded 已移除 - 窗口初始为 Hidden，不需要 Loaded 隐藏逻辑

        private void InitUI()
        {
            // 加载 DCC 列表
            LstBlacklist.Items.Clear();
            foreach (var app in ConfigManager.Current.Blacklist)
            {
                LstBlacklist.Items.Add(app);
            }

            // 加载快捷键显示
            TxtHotkeySwitch.Text = ConfigManager.Current.HotkeySwitch.DisplayText;
            TxtHotkeySettings.Text = ConfigManager.Current.HotkeySettings.DisplayText;

            // 加载颜色
            try
            {
                ColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(ConfigManager.Current.GlowColor);
            }
            catch { }

            // 加载荧光棒尺寸
            NumGlowWidth.Value = (int)ConfigManager.Current.GlowWidth;
            NumGlowHeight.Value = (int)ConfigManager.Current.GlowHeight;

            // === [核心] 初始化语言 ===
            // 如果配置里没有语言，默认设为英文
            string currentLang = string.IsNullOrEmpty(ConfigManager.Current.Language) ? "en-US" : ConfigManager.Current.Language;
            ApplyLanguage(currentLang);
        }

        // === 语言切换核心逻辑 ===
        private void ApplyLanguage(string langCode)
        {
            try
            {
                string dictPath = $"Resources/Lang.{langCode}.xaml";
                var dict = new ResourceDictionary();
                dict.Source = new Uri(dictPath, UriKind.Relative);

                // 移除旧字典
                var oldDict = Application.Current.Resources.MergedDictionaries
                                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Lang."));
                if (oldDict != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(oldDict);
                }

                // 添加新字典
                Application.Current.Resources.MergedDictionaries.Add(dict);

                // 更新配置
                ConfigManager.Current.Language = langCode;

                // 同步下拉框显示
                foreach (ComboBoxItem item in CboLanguage.Items)
                {
                    if (item.Tag != null && item.Tag.ToString() == langCode)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Language load failed: {ex.Message}");
            }
        }

        // 辅助方法：获取资源字符串
        private string TryGetResource(string key)
        {
            var val = Application.Current.TryFindResource(key);
            return val as string ?? key;
        }

        // === 窗口初始化挂钩 ===
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            if (!RegisterHotKeys(helper.Handle))
            {
                TrayIcon.ShowBalloonTip("FocusShield", "Hotkey registration failed. Please change shortcuts.", BalloonIcon.Warning);
            }

            HwndSource? source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);
        }

        // === 注册快捷键 ===
        private bool RegisterHotKeys(IntPtr hwnd)
        {
            UnregisterHotKey(hwnd, HOTKEY_ID);
            UnregisterHotKey(hwnd, HOTKEY_SETTINGS_ID);

            bool switchOk = RegisterHotKey(hwnd, HOTKEY_ID, ConfigManager.Current.HotkeySwitch.Modifier, ConfigManager.Current.HotkeySwitch.Key);
            bool settingsOk = RegisterHotKey(hwnd, HOTKEY_SETTINGS_ID, ConfigManager.Current.HotkeySettings.Modifier, ConfigManager.Current.HotkeySettings.Key);
            return switchOk && settingsOk;
        }

        // === 消息处理循环 ===
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID)
                {
                    // 切换专注模式
                    _guard.ToggleLock();
                    UpdateStatusUI();
                    PlaySoundEffect(_guard.IsLocked);
                    handled = true;
                }
                else if (id == HOTKEY_SETTINGS_ID)
                {
                    // 打开设置
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // === 更新状态 UI (光盾、托盘、灯光) ===
        private void UpdateStatusUI()
        {
            string colorHex = ConfigManager.Current.GlowColor;

            _overlay.SetActiveState(_guard.IsLocked);

            if (_guard.IsLocked)
            {
                // 开启状态
                try
                {
                    StatusIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                }
                catch { }

                TrayIcon.ToolTipText = $"FocusShield: {TryGetResource("Lang_Tray_Locked")}";

                _overlay.Show();
            }
            else
            {
                // 关闭状态
                StatusIndicator.Background = Brushes.LightGray;
                TrayIcon.ToolTipText = $"FocusShield: {TryGetResource("Lang_Tray_Unlocked")}";

                _overlay.Show(); // 关闭时仍显示光棒（半透明）
            }
        }

        private void PlaySoundEffect(bool isLocking)
        {
            try { if (isLocking) SystemSounds.Asterisk.Play(); else SystemSounds.Hand.Play(); } catch { }
        }

        // === 界面事件处理 ===

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerWindow();
            picker.Owner = this;
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedProcessName))
            {
                string appName = picker.SelectedProcessName;
                if (!ConfigManager.Current.Blacklist.Contains(appName))
                {
                    ConfigManager.Current.Blacklist.Add(appName);
                    LstBlacklist.Items.Add(appName);
                }
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (LstBlacklist.SelectedItem is string app && !string.IsNullOrWhiteSpace(app))
            {
                ConfigManager.Current.Blacklist.Remove(app);
                LstBlacklist.Items.Remove(app);
            }
        }

        // === 拖拽快捷方式/exe 添加到黑名单 ===
        private void LstBlacklist_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    string? processName = null;
                    if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetPath = ResolveShortcut(file);
                        if (!string.IsNullOrEmpty(targetPath))
                            processName = System.IO.Path.GetFileNameWithoutExtension(targetPath);
                    }
                    else if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        processName = System.IO.Path.GetFileNameWithoutExtension(file);
                    }

                    if (!string.IsNullOrEmpty(processName)
                        && !ConfigManager.Current.Blacklist.Contains(processName))
                    {
                        ConfigManager.Current.Blacklist.Add(processName);
                        LstBlacklist.Items.Add(processName);
                    }
                }
            }
        }

        // 解析 .lnk 快捷方式，返回目标 exe 路径
        public static string? ResolveShortcut(string lnkPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(lnkPath)!;
                return shortcut.TargetPath as string;
            }
            catch { return null; }
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                ConfigManager.Current.GlowColor = e.NewValue.Value.ToString();
                ApplyTheme(); // 仅更新设置界面上的演示灯
            }
        }

        // 荧光棒尺寸改变事件
        private void NumGlowSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (NumGlowWidth.Value.HasValue)
                ConfigManager.Current.GlowWidth = NumGlowWidth.Value.Value;
            
            if (NumGlowHeight.Value.HasValue)
                ConfigManager.Current.GlowHeight = NumGlowHeight.Value.Value;

            // 实时更新荧光棒尺寸
            if (_overlay != null)
            {
                _overlay.SetSize(ConfigManager.Current.GlowWidth, ConfigManager.Current.GlowHeight);
            }
        }

        // 语言切换事件
        private void CboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboLanguage.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                // 防止重复加载
                if (ConfigManager.Current.Language != langCode)
                {
                    ApplyLanguage(langCode);
                }
            }
        }

        // === 快捷键录制 ===
        private void Hotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            if (modifiers == System.Windows.Input.ModifierKeys.None) return;

            var key = (e.Key == System.Windows.Input.Key.System) ? e.SystemKey : e.Key;

            if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin) return;

            uint fsModifiers = 0;
            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) != 0) fsModifiers |= 0x0001;
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) != 0) fsModifiers |= 0x0002;
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) fsModifiers |= 0x0004;
            if ((modifiers & System.Windows.Input.ModifierKeys.Windows) != 0) fsModifiers |= 0x0008;

            uint vKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            string keyText = $"{modifiers} + {key}";

            if (sender is not TextBox textBox) return;

            if (textBox.Name == "TxtHotkeySwitch")
            {
                ConfigManager.Current.HotkeySwitch.Modifier = fsModifiers;
                ConfigManager.Current.HotkeySwitch.Key = vKey;
                ConfigManager.Current.HotkeySwitch.DisplayText = keyText;
                TxtHotkeySwitch.Text = keyText;
            }
            else if (textBox.Name == "TxtHotkeySettings")
            {
                ConfigManager.Current.HotkeySettings.Modifier = fsModifiers;
                ConfigManager.Current.HotkeySettings.Key = vKey;
                ConfigManager.Current.HotkeySettings.DisplayText = keyText;
                TxtHotkeySettings.Text = keyText;
            }
        }

        private void Hotkey_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Background = new SolidColorBrush(Color.FromRgb(240, 240, 255));
            }
        }

        private void Hotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Background = Brushes.White;
            }
        }

        // === 底部按钮 ===
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Save();

            // 重新注册快捷键
            var helper = new WindowInteropHelper(this);
            if (!RegisterHotKeys(helper.Handle))
            {
                TrayIcon.ShowBalloonTip("FocusShield", "Hotkey registration failed. Please change shortcuts.", BalloonIcon.Warning);
            }

            // 如果此时开着专注模式，实时更新光盾颜色
            if (_guard.IsLocked)
            {
                _overlay.SetColor(ConfigManager.Current.GlowColor);
            }

            this.Hide();
            string msg = TryGetResource("Lang_Msg_Saved");
            TrayIcon.ShowBalloonTip("FocusShield", msg, BalloonIcon.Info);
        }

        private void BtnQuit_Click(object sender, RoutedEventArgs e)
        {
            _isRealExit = true;
            _overlay.Close(); // 记得关闭光盾
            _guard.Dispose(); // 记得释放钩子
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnClosing(e);
        }

        // 托盘菜单事件
        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isRealExit = true;
            _overlay.Close();
            _guard.Dispose();
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }

        // === 开机自启 ===
        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                string keyName = "FocusShield";
                string? assemblyLocation = Process.GetCurrentProcess().MainModule?.FileName;
                using RegistryKey? rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk == null || string.IsNullOrWhiteSpace(assemblyLocation))
                {
                    return;
                }

                if (ChkAutoStart.IsChecked == true)
                    rk.SetValue(keyName, assemblyLocation);
                else
                    rk.DeleteValue(keyName, false);
            }
            catch { }
        }

        private void CheckAutoStartStatus()
        {
            try
            {
                using RegistryKey? rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                if (rk?.GetValue("FocusShield") != null)
                    ChkAutoStart.IsChecked = true;
            }
            catch { }
        }

        private void ApplyTheme()
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(ConfigManager.Current.GlowColor);
                if (_guard.IsLocked) StatusIndicator.Background = new SolidColorBrush(color);
            }
            catch { }
        }

        // Win32 API
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
