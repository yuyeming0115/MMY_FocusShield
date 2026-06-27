using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
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
        private DispatcherTimer? _clickTimer;
        private bool _clickHandled = false;

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

        // 设置视觉状态（开启/关闭）
        public void SetActiveState(bool isActive)
        {
            if (isActive)
            {
                // 开启：全发光
                GlowBorder.Opacity = 1.0;
                if (GlowBorder.Effect is DropShadowEffect shadow)
                {
                    shadow.Opacity = 1.0;
                    shadow.BlurRadius = 15;
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

        // 左键按下：记录位置 + 启动单击判定定时器
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _clickHandled = false;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;

            // 启动 200ms 定时器，判断是否为单击
            _clickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _clickTimer.Tick += (s, args) =>
            {
                _clickTimer.Stop();
                // 200ms 内没有拖拽 → 判定为单击
                if (!_isDragging && !_clickHandled)
                {
                    _clickHandled = true;
                    OnToggleRequested?.Invoke();
                }
            };
            _clickTimer.Start();

            // 不立即调用 DragMove()，等 Move 事件判断
            e.Handled = false;
            base.OnMouseLeftButtonDown(e);
        }

        // 鼠标移动：判断是否进入拖拽模式
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                var currentPos = e.GetPosition(this);
                double deltaX = Math.Abs(currentPos.X - _dragStartPoint.X);
                double deltaY = Math.Abs(currentPos.Y - _dragStartPoint.Y);

                // 移动超过 5px → 判定为拖拽
                if (deltaX > 5 || deltaY > 5)
                {
                    _isDragging = true;
                    _clickTimer?.Stop();
                    DragMove();
                }
            }
        }

        // 左键释放：拖拽结束
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                SnapToEdge();
            }
        }

        // 双击：打开设置
        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _clickHandled = true; // 阻止单击逻辑
            _clickTimer?.Stop();
            OnSettingsRequested?.Invoke();
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
