using System;
using System.Runtime.InteropServices;

namespace Iris.Shell.Platform;

/// <summary>The few Win32 calls the TV output needs: a monitor's friendly name and hiding the mouse cursor over a window.</summary>
internal static class NativeMethods
{
    private const uint WmSetCursor = 0x0020;
    private const int HtClient = 1;

    // Kept alive for as long as the subclassed windows exist.
    private static readonly SubclassProc CursorProc = HideCursorOverClient;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    /// <summary>The name Windows reports for the monitor of <paramref name="monitor"/>, or null.</summary>
    public static string? MonitorName(IntPtr monitor)
    {
        try
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = string.Empty };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return null;
            }

            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), DeviceName = string.Empty, DeviceString = string.Empty, DeviceId = string.Empty, DeviceKey = string.Empty };
            return EnumDisplayDevices(info.Device, 0, ref device, 0) ? device.DeviceString?.Trim() : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
    }

    /// <summary>Makes the mouse cursor invisible while it is over the window's client area.</summary>
    public static void HideCursor(IntPtr window)
    {
        try
        {
            SetWindowSubclass(window, CursorProc, new UIntPtr(1), UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static IntPtr HideCursorOverClient(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == WmSetCursor && (lParam.ToInt64() & 0xFFFF) == HtClient)
        {
            SetCursor(IntPtr.Zero);
            return new IntPtr(1);
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
