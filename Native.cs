using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HelperBar;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_SHOWISLAND = 0x8001;
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MEMORYSTATUSEX { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    public static void NotifyExisting() { var handle = FindWindow(null, "HelperBar — твій острів"); if (handle != IntPtr.Zero) PostMessage(handle, WM_SHOWISLAND, IntPtr.Zero, IntPtr.Zero); }
    public static void StopExisting() { var handle = FindWindow(null, "HelperBar — твій острів"); if (handle != IntPtr.Zero) PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero); }
    public static bool Key(ushort key)
    {
        var inputs = new[] { new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = key } } }, new INPUT { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = key, Flags = 2 } } } };
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>()) == 2;
    }
    public static (int Percent, double Used, double Total) Memory()
    {
        var status = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);
        return ((int)status.Load, (status.TotalPhysical - status.AvailablePhysical) / 1073741824d, status.TotalPhysical / 1073741824d);
    }
    public static string Power()
    {
        if (!GetSystemPowerStatus(out var status)) return "Живлення невідоме";
        if (status.BatteryFlag == 128 || status.BatteryLifePercent == 255) return "Від мережі";
        return $"{status.BatteryLifePercent}%" + (status.ACLineStatus == 1 ? " · заряджається" : " · батарея");
    }
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
