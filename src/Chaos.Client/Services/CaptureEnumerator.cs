using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Chaos.Client.Models;

namespace Chaos.Client.Services;

public static class CaptureEnumerator
{
    public static List<CaptureTarget> GetScreens()
    {
        var screens = new List<CaptureTarget>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref info))
            {
                int left = info.rcMonitor.Left;
                int top = info.rcMonitor.Top;
                int width = info.rcMonitor.Right - info.rcMonitor.Left;
                int height = info.rcMonitor.Bottom - info.rcMonitor.Top;
                bool isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;

                string label = $"Display {index + 1}";
                if (isPrimary) label += " (Primary)";

                var thumbnail = CaptureScreenThumbnail(left, top, width, height);

                screens.Add(new CaptureTarget
                {
                    Type = CaptureTargetType.Screen,
                    DisplayName = label,
                    Handle = hMonitor,
                    ScreenIndex = index,
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    Thumbnail = thumbnail
                });
                index++;
            }
            return true;
        }, IntPtr.Zero);

        return screens;
    }

    public static List<CaptureTarget> GetWindows()
    {
        var windows = new List<CaptureTarget>();
        var selfHandle = Process.GetCurrentProcess().MainWindowHandle;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (hWnd == selfHandle) return true;

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            var sb = new char[256];
            int len = GetWindowText(hWnd, sb, sb.Length);
            if (len == 0) return true;
            string title = new string(sb, 0, len);

            if (!GetWindowRect(hWnd, out RECT rect)) return true;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return true;

            var thumbnail = CaptureWindowThumbnail(hWnd, width, height);

            windows.Add(new CaptureTarget
            {
                Type = CaptureTargetType.Window,
                DisplayName = title,
                Handle = hWnd,
                Left = rect.Left,
                Top = rect.Top,
                Width = width,
                Height = height,
                Thumbnail = thumbnail
            });
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static BitmapSource? CaptureScreenThumbnail(int left, int top, int width, int height)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(hdc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdc, width, height);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);
        try
        {
            BitBlt(memDC, 0, 0, width, height, hdc, left, top, SRCCOPY);
            SelectObject(memDC, oldBitmap);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(320, 180));
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally
        {
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static BitmapSource? CaptureWindowThumbnail(IntPtr hWnd, int width, int height)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(hdc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdc, width, height);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);
        try
        {
            PrintWindow(hWnd, memDC, PW_RENDERFULLCONTENT);
            SelectObject(memDC, oldBitmap);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(320, 180));
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally
        {
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    // P/Invoke
    private const int SRCCOPY = 0x00CC0020;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int MONITORINFOF_PRIMARY = 1;
    private const int PW_RENDERFULLCONTENT = 0x2;

    private delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int srcX, int srcY, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}
