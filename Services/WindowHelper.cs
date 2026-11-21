using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsLive.Services;

public static class WindowHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int DWMWA_EXCLUDED_FROM_PEEK = 12;

    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;

    // Windows Messages for Drag & Drop UIPI filter bypass
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;
    private const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hWnd);
        void DeleteTab(IntPtr hWnd);
        void ActivateTab(IntPtr hWnd);
        void SetActiveAlt(IntPtr hWnd);
    }

    private static IntPtr GetWindowLongAuto(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongAuto(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    public static void SetActivationEnabled(Window window, bool enable)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;
        if (hWnd == IntPtr.Zero) return;

        IntPtr exStyle = GetWindowLongAuto(hWnd, GWL_EXSTYLE);
        long style = exStyle.ToInt64();

        if (enable)
        {
            // Remove WS_EX_NOACTIVATE so the window can receive physical keyboard input
            style &= ~WS_EX_NOACTIVATE;
        }
        else
        {
            // Add WS_EX_NOACTIVATE so background clicks don't steal focus from other apps
            style |= WS_EX_NOACTIVATE;
        }

        SetWindowLongAuto(hWnd, GWL_EXSTYLE, new IntPtr(style));
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static void MakeToolWindow(Window window)
    {
        var helper = new WindowInteropHelper(window);
        IntPtr hWnd = helper.Handle;
        if (hWnd != IntPtr.Zero)
        {
            // 1. Clear window title completely so Task Manager & Alt-Tab have no titled app window
            SetWindowText(hWnd, string.Empty);

            // 2. Remove WS_EX_APPWINDOW and add WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            IntPtr exStyle = GetWindowLongAuto(hWnd, GWL_EXSTYLE);
            long newStyle = (exStyle.ToInt64() & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongAuto(hWnd, GWL_EXSTYLE, new IntPtr(newStyle));

            // 3. Exclude from DWM Peek and Alt-Tab switcher
            try
            {
                int trueVal = 1;
                DwmSetWindowAttribute(hWnd, DWMWA_EXCLUDED_FROM_PEEK, ref trueVal, sizeof(int));
            }
            catch { }

            // 4. Instruct Windows Shell to remove from taskbar and task switcher tabs
            try
            {
                var taskbar = (ITaskbarList)new TaskbarInstance();
                taskbar.HrInit();
                taskbar.DeleteTab(hWnd);
            }
            catch { }

            // 5. Apply frame change
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // 6. Allow Drag & Drop messages from all processes (bypasses Windows UIPI restriction)
            try
            {
                ChangeWindowMessageFilterEx(hWnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hWnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hWnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hWnd, 0x004D, MSGFLT_ALLOW, IntPtr.Zero); // WM_QUERYENDSESSION
            }
            catch { }
        }
    }

    public static void EnsureTopMost(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
