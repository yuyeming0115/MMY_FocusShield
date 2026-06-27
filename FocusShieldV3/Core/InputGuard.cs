using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FocusShield.Core
{
    public class InputGuard : IDisposable
    {
        public bool IsLocked { get; private set; } = false;

        private WinEventDelegate _dele;
        private IntPtr _hHook = IntPtr.Zero;
        private CancellationTokenSource? _cts;
        private readonly IntPtr _englishLayout;

        // === 缓存变量 (性能优化的关键) ===
        private IntPtr _lastHwnd = IntPtr.Zero;
        private bool _lastShouldLock = false;

        // Win32 常量
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const string KLF_US_ENGLISH = "00000409";
        private const uint KLF_ACTIVATE = 0x00000001;

        public InputGuard()
        {
            ConfigManager.Load();
            _dele = new WinEventDelegate(WinEventProc);
            _englishLayout = LoadKeyboardLayout(KLF_US_ENGLISH, KLF_ACTIVATE);
        }

        public void ToggleLock()
        {
            IsLocked = !IsLocked;
            if (IsLocked)
            {
                if (_hHook == IntPtr.Zero)
                {
                    _hHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _dele, 0, 0, WINEVENT_OUTOFCONTEXT);
                }

                // 重置缓存，防止逻辑错乱
                _lastHwnd = IntPtr.Zero;
                _lastShouldLock = false;

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                StartTurboSentinel(_cts.Token);
            }
            else
            {
                if (_hHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hHook);
                    _hHook = IntPtr.Zero;
                }
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        // === 极速哨兵 (100ms 级别) ===
        private async void StartTurboSentinel(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 1. 获取当前前台窗口
                    IntPtr currentHwnd = GetForegroundWindow();

                    // 2. 如果窗口没变，直接使用上次的判定结果 (极速路径)
                    if (currentHwnd == _lastHwnd)
                    {
                        if (_lastShouldLock)
                        {
                            // 只要是需要锁定的窗口，每 100ms 强制发送一次 ENG 指令
                            // 这会让误触 Shift 瞬间失效
                            ForceEnglish(currentHwnd);
                        }
                    }
                    else
                    {
                        // 3. 如果窗口变了，进行一次完整的 DCC 列表检查 (慢速路径)
                        _lastShouldLock = ShouldLockWindow(currentHwnd);
                        _lastHwnd = currentHwnd;

                        if (_lastShouldLock)
                        {
                            ForceEnglish(currentHwnd);
                        }
                    }

                    // 4. 等待 100ms (0.1秒)
                    // 这个频率对于 ZBrush 操作几乎是无感的
                    await Task.Delay(100, token);
                }
            }
            catch (TaskCanceledException) { }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // WinEventProc 现在主要起到“立即唤醒”的作用
            // 虽然轮询已经很快了，但加上这个钩子能保证切换瞬间 0 延迟
            if (!IsLocked || hwnd == IntPtr.Zero) return;

            // 更新缓存状态
            _lastShouldLock = ShouldLockWindow(hwnd);
            _lastHwnd = hwnd;

            if (_lastShouldLock)
            {
                ForceEnglish(hwnd);
            }
        }

        // 判断当前窗口是否应该被锁定 (返回 true = 需要强制英文)
        private bool ShouldLockWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            // 仅当窗口命中 DCC 列表/规则时才锁定
            return IsDccTargetWindow(hWnd);
        }

        private void ForceEnglish(IntPtr hWnd)
        {
            IntPtr hLayout = _englishLayout != IntPtr.Zero ? _englishLayout : LoadKeyboardLayout(KLF_US_ENGLISH, KLF_ACTIVATE);
            PostMessage(hWnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hLayout);
        }

        private bool IsDccTargetWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                var process = Process.GetProcessById((int)processId);
                if (process == null) return false;

                string procName = process.ProcessName;

                if (ConfigManager.Current.Blacklist.Contains(procName)) return true;

                string title = GetWindowTitle(hWnd);
                foreach (var rule in ConfigManager.Current.DeepRules)
                {
                    bool nameMatch = string.IsNullOrEmpty(rule.ProcessName) || rule.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase);
                    bool titleMatch = !string.IsNullOrEmpty(rule.TitleRegex) && Regex.IsMatch(title, rule.TitleRegex, RegexOptions.IgnoreCase);
                    if (nameMatch && titleMatch) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            if (GetWindowText(hWnd, Buff, nChars) > 0) return Buff.ToString();
            return string.Empty;
        }

        public void Dispose()
        {
            if (_hHook != IntPtr.Zero) UnhookWinEvent(_hHook);
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        // --- API 声明 ---
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);
    }
}
