using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget;

/// <summary>Clamp native bounds in physical pixels, including mixed-DPI desktops.</summary>
internal static class WindowPlacement
{
    internal static Forms.Screen? ScreenFor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle == IntPtr.Zero ? null : Forms.Screen.FromHandle(handle);
    }

    internal static void KeepInside(Window window, Forms.Screen? selected = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception();
        var area = (selected ?? Forms.Screen.FromHandle(handle)).WorkingArea;
        var width = Math.Min(rect.Right - rect.Left, area.Width);
        var height = Math.Min(rect.Bottom - rect.Top, area.Height);
        var left = Math.Clamp(rect.Left, area.Left, area.Right - width);
        var top = Math.Clamp(rect.Top, area.Top, area.Bottom - height);
        if (left == rect.Left && top == rect.Top && width == rect.Right - rect.Left && height == rect.Bottom - rect.Top) return;
        if (!SetWindowPos(handle, IntPtr.Zero, left, top, width, height, 0x0004 | 0x0010)) throw new Win32Exception();
    }

    internal static bool IsInside(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var rect)) return false;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        return rect.Left >= area.Left && rect.Top >= area.Top && rect.Right <= area.Right && rect.Bottom <= area.Bottom;
    }

    internal static void AnchorNotification(Window window, Forms.Screen? selected = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception();
        var area = (selected ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.FromHandle(handle)).WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var marginX = (int)Math.Round(16 * dpi.DpiScaleX);
        var marginY = (int)Math.Round(16 * dpi.DpiScaleY);
        var width = Math.Min(rect.Right - rect.Left, Math.Max(1, area.Width - marginX * 2));
        var height = Math.Min(rect.Bottom - rect.Top, Math.Max(1, area.Height - marginY * 2));
        var left = Math.Max(area.Left, area.Right - marginX - width);
        var top = Math.Max(area.Top, area.Bottom - marginY - height);
        if (left == rect.Left && top == rect.Top && width == rect.Right - rect.Left && height == rect.Bottom - rect.Top) return;
        if (!SetWindowPos(handle, IntPtr.Zero, left, top, width, height, 0x0004 | 0x0010)) throw new Win32Exception();
    }

    internal static void MoveToCornerForPreview(Window window, Forms.Screen screen)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception();
        var area = screen.WorkingArea;
        if (!SetWindowPos(handle, IntPtr.Zero, area.Right - (rect.Right - rect.Left) - 2,
            area.Bottom - (rect.Bottom - rect.Top) - 2, 0, 0, 0x0001 | 0x0004 | 0x0010)) throw new Win32Exception();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
