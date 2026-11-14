using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WindowsLive.Services;

public static class WindowSnappingService
{
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void SnapForegroundWindow(SnapPosition pos)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        ShowWindow(hwnd, SW_RESTORE);

        var workArea = SystemParameters.WorkArea;

        int x = (int)workArea.Left;
        int y = (int)workArea.Top;
        int w = (int)workArea.Width;
        int h = (int)workArea.Height;

        switch (pos)
        {
            case SnapPosition.LeftHalf:
                w = (int)(workArea.Width / 2.0);
                break;
            case SnapPosition.RightHalf:
                x = (int)(workArea.Left + (workArea.Width / 2.0));
                w = (int)(workArea.Width / 2.0);
                break;
            case SnapPosition.TopHalf:
                h = (int)(workArea.Height / 2.0);
                break;
            case SnapPosition.BottomHalf:
                y = (int)(workArea.Top + (workArea.Height / 2.0));
                h = (int)(workArea.Height / 2.0);
                break;
            case SnapPosition.Maximize:
                break;
        }

        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_SHOWWINDOW);
    }
}

public enum SnapPosition
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    Maximize
}
