using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace WindowsLive.Services;

public enum ForegroundAppState
{
    Normal,
    Maximized,
    FullScreen
}

public class AppStateTracker
{
    public event Action<ForegroundAppState>? StateChanged;

    public ForegroundAppState CurrentState { get; private set; } = ForegroundAppState.Normal;

    private readonly DispatcherTimer _pollTimer;
    private IntPtr _dockHwnd = IntPtr.Zero;

    public AppStateTracker()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _pollTimer.Tick += (s, e) => CheckForegroundWindow();
        _pollTimer.Start();
    }

    public void SetDockHwnd(IntPtr hwnd)
    {
        _dockHwnd = hwnd;
    }

    private void CheckForegroundWindow()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _dockHwnd)
            {
                // If the dock itself or no window has focus, do not change state
                return;
            }

            var sb = new StringBuilder(256);
            GetClassName(fg, sb, 256);
            string cls = sb.ToString();

            // Desktop / shell windows: always Normal (notch must appear)
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd")
            {
                SetState(ForegroundAppState.Normal);
                return;
            }

            // Check monitor containing the foreground window
            IntPtr hMonitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (!GetMonitorInfo(hMonitor, ref mi))
            {
                SetState(ForegroundAppState.Normal);
                return;
            }

            RECT winRect;
            GetWindowRect(fg, out winRect);

            // An app in full-screen mode covers the entire monitor, including the taskbar.
            // (with a small 4px tolerance for DWM / border margins)
            bool coversEntireMonitor = (winRect.Left <= mi.rcMonitor.Left + 4 &&
                                        winRect.Top <= mi.rcMonitor.Top + 4 &&
                                        winRect.Right >= mi.rcMonitor.Right - 4 &&
                                        winRect.Bottom >= mi.rcMonitor.Bottom - 4);

            if (coversEntireMonitor)
            {
                // Check whether it's truly full screen or just maximized:
                // When an app is MAXIMIZED:
                // - The taskbar is still visible (rcWork is smaller than rcMonitor)
                // - The window has WS_CAPTION (title bar)
                // When an app is FULL SCREEN (YouTube, Netflix, games, VLC, F11):
                // - It covers the taskbar completely
                // - It removes the standard WS_CAPTION titlebar
                long style = GetWindowLongAuto(fg, GWL_STYLE).ToInt64();
                bool hasStandardCaption = (style & WS_CAPTION) == WS_CAPTION && (style & WS_THICKFRAME) == WS_THICKFRAME;

                bool taskbarCovered = (mi.rcWork.Bottom < mi.rcMonitor.Bottom && winRect.Bottom >= mi.rcMonitor.Bottom - 4) ||
                                      (mi.rcWork.Top > mi.rcMonitor.Top && winRect.Top <= mi.rcMonitor.Top + 4) ||
                                      (mi.rcWork.Left > mi.rcMonitor.Left && winRect.Left <= mi.rcMonitor.Left + 4) ||
                                      (mi.rcWork.Right < mi.rcMonitor.Right && winRect.Right >= mi.rcMonitor.Right - 4);

                if (taskbarCovered || !hasStandardCaption)
                {
                    SetState(ForegroundAppState.FullScreen);
                    return;
                }
            }

            // In all other cases (normal windowed, maximized app, etc.):
            SetState(ForegroundAppState.Normal);
        }
        catch
        {
            SetState(ForegroundAppState.Normal);
        }
    }

    private void SetState(ForegroundAppState state)
    {
        if (CurrentState != state)
        {
            CurrentState = state;
            StateChanged?.Invoke(state);
        }
    }

    private static IntPtr GetWindowLongAuto(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
