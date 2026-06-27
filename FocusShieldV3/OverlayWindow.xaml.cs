using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FocusShield.Core;

namespace FocusShield
{
    public partial class OverlayWindow : Window
    {
        // 事件：通知 MainWindow
        public event Action? OnToggleRequested;
        public event Action? OnSettingsRequested;
        public event Action? OnExitRequested;

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private bool _isActive = true;

        public OverlayWindow()
        {
            InitializeComponent();
            
            // 应用配置中的尺寸
            this.Width = ConfigManager.Current.GlowWidth;
            this.Height = ConfigManager.Current.GlowHeight;
            
            // 初始位置：屏幕右侧边缘，垂直居中（更醒目）
            double screenWidth = SystemParameters.WorkArea.Width;
            this.Left = screenWidth - this.Width;
            this.Top = (SystemParameters.WorkArea.Height - this.Height) / 2;

            // 应用发光设置
            ApplyGlowSettings();
        }

        public void SetColor(string colorHex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                GlowBorder.Background = new SolidColorBrush(color);
                if (GlowBorder.Effect is DropShadowEffect shadow)
                {
                    shadow.Color = color;
                }
            }
            catch { }
        }

        // 设置荧光棒尺寸
        public void SetSize(double width, double height)
        {
            this.Width = width;
            this.Height = height;
        }

        // 应用发光设置
        public void ApplyGlowSettings()
        {
            if (GlowBorder.Effect is DropShadowEffect shadow)
            {
                shadow.BlurRadius = ConfigManager.Current.GlowBlurRadius;
                shadow.Opacity = ConfigManager.Current.GlowIntensity;
                shadow.ShadowDepth = 0;
            }
        }

        // 设置视觉状态（开启/关闭）
        public void SetActiveState(bool isActive)
        {
            _isActive = isActive;
            
            if (isActive)
            {
                // 开启：全发光
                GlowBorder.Opacity = 1.0;
                if (GlowBorder.Effect is DropShadowEffect shadow)
                {
                    shadow.Opacity = ConfigManager.Current.GlowIntensity;
                    shadow.BlurRadius = ConfigManager.Current.GlowBlurRadius;
                }
                // 更新右键菜单文字
                if (MenuToggle != null)
                    MenuToggle.Header = "🔒 关闭屏蔽";
            }
            else
            {
                // 关闭：半透明灰色，无发光
                GlowBorder.Opacity = 0.25;
                if (GlowBorder.Effect is DropShadowEffect shadow)
                {
                    shadow.Opacity = 0.0; // 完全关闭发光
                }
                // 更新右键菜单文字
                if (MenuToggle != null)
                    MenuToggle.Header = "🔓 开启屏蔽";
            }
        }

        // ===== 鼠标交互 =====

        // 左键按下：开始拖拽
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                this.CaptureMouse();
                e.Handled = true;
            }
        }

        // 鼠标移动：执行拖拽
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(this);
                double deltaX = currentPos.X - _dragStartPoint.X;
                double deltaY = currentPos.Y - _dragStartPoint.Y;

                // 更新窗口位置
                this.Left += deltaX;
                this.Top += deltaY;
            }
        }

        // 左键释放：结束拖拽
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
                SnapToEdge();
            }
        }

        // 双击：切换状态
        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OnToggleRequested?.Invoke();
        }

        // 右键：显示上下文菜单
        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ContextMenu != null) ContextMenu.IsOpen = true;
        }

        // ===== 右键菜单 =====
        private void MenuToggle_Click(object sender, RoutedEventArgs e)
        {
            OnToggleRequested?.Invoke();
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            OnSettingsRequested?.Invoke();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            OnExitRequested?.Invoke();
        }

        // ===== 拖拽吸附 =====
        private void SnapToEdge()
        {
            double screenWidth = SystemParameters.WorkArea.Width;
            double currentLeft = this.Left;
            double windowWidth = this.Width;

            if (currentLeft + (windowWidth / 2) < screenWidth / 2)
            {
                this.Left = 0;
                GlowBorder.CornerRadius = new CornerRadius(0, 5, 5, 0);
            }
            else
            {
                this.Left = screenWidth - windowWidth;
                GlowBorder.CornerRadius = new CornerRadius(5, 0, 0, 5);
            }

            if (this.Top < 0) this.Top = 0;
            if (this.Top + this.Height > SystemParameters.WorkArea.Height)
                this.Top = SystemParameters.WorkArea.Height - this.Height;
        }
    }
}
