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
            Interval = TimeSpan.FromMilliseconds(250)
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
            if (fg == IntPtr.Zero || fg == _dockHwnd) return;

            // Skip desktop and shell windows
            var sb = new StringBuilder(256);
            GetClassName(fg, sb, 256);
            string cls = sb.ToString();

            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd")
            {
                SetState(ForegroundAppState.Normal);
                return;
            }

            // Check monitor bounds
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

            // 1. Check FullScreen: window spans the entire monitor (or larger)
            int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
            int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

            int winW = winRect.Right - winRect.Left;
            int winH = winRect.Bottom - winRect.Top;

            // Tolerances for border / multi-monitor bleed
            bool isFullScreen = (winRect.Left <= mi.rcMonitor.Left &&
                                 winRect.Top <= mi.rcMonitor.Top &&
                                 winRect.Right >= mi.rcMonitor.Right &&
                                 winRect.Bottom >= mi.rcMonitor.Bottom);

            if (isFullScreen)
            {
                SetState(ForegroundAppState.FullScreen);
                return;
            }

            // 2. Check Maximized: either showCmd is SW_MAXIMIZE or window covers work area
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf(wp);
            GetWindowPlacement(fg, ref wp);

            bool isMaximized = (wp.showCmd == SW_SHOWMAXIMIZED);

            if (!isMaximized)
            {
                // Fallback: check if window matches or exceeds work area
                int workW = mi.rcWork.Right - mi.rcWork.Left;
                int workH = mi.rcWork.Bottom - mi.rcWork.Top;

                if (Math.Abs(winW - workW) <= 20 && Math.Abs(winH - workH) <= 20 &&
                    Math.Abs(winRect.Left - mi.rcWork.Left) <= 15 && Math.Abs(winRect.Top - mi.rcWork.Top) <= 15)
                {
                    isMaximized = true;
                }
            }

            if (isMaximized)
            {
                SetState(ForegroundAppState.Maximized);
            }
            else
            {
                SetState(ForegroundAppState.Normal);
            }
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
    private const int SW_SHOWMAXIMIZED = 3;

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
