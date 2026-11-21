using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace HelperBar;

internal static class PlacementGeometry
{
    // All coordinates here are physical pixels; transparent margins may extend outside the work area.
    public static Rect Range(Rect work, double scale, double dpi)
    {
        var unit = scale * dpi;
        var left = work.Left - 22 * unit;
        var top = work.Top - 16 * unit;
        return new Rect(left, top, Math.Max(0, work.Width - 556 * unit), Math.Max(0, work.Height - 430 * unit));
    }
    public static Point Resolve(Rect range, bool free, double x, double y, double topOffset, double dpi) =>
        new(range.Left + range.Width * (free ? Math.Clamp(x, 0, 1) : .5),
            range.Top + (free ? range.Height * Math.Clamp(y, 0, 1) : Math.Min(range.Height, topOffset * dpi)));
    public static Point Normalize(Rect range, Point point) => new(range.Width > 0 ? Math.Clamp((point.X - range.Left) / range.Width, 0, 1) : .5, range.Height > 0 ? Math.Clamp((point.Y - range.Top) / range.Height, 0, 1) : 0);
}

public sealed partial class IslandWindow
{
    private bool positioning, dragging;
    private double effectiveScale = 1;
    private Forms.Screen PreferredScreen()
    {
        var screens = Forms.Screen.AllScreens;
        if (settings.MonitorId.Length > 0)
            return screens.FirstOrDefault(s => MonitorIdentity(s) == settings.MonitorId) ?? Forms.Screen.PrimaryScreen ?? screens[0];
        var selected = screens.FirstOrDefault(s => s.DeviceName == settings.MonitorName) ?? screens[Math.Clamp(settings.ScreenIndex, 0, screens.Length - 1)];
        RememberMonitor(selected);
        return selected;
    }
    private void RememberMonitor(Forms.Screen screen)
    {
        settings.MonitorId = MonitorIdentity(screen); settings.MonitorName = screen.DeviceName;
        settings.ScreenIndex = Array.FindIndex(Forms.Screen.AllScreens, s => s.DeviceName == screen.DeviceName);
    }
    private static string MonitorIdentity(Forms.Screen screen)
    {
        var display = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
        return EnumDisplayDevices(screen.DeviceName, 0, ref display, 1) && !string.IsNullOrEmpty(display.DeviceId) ? display.DeviceId : screen.DeviceName;
    }
    private static Rect WorkArea(Forms.Screen screen) => new(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
    private double WindowDpi => Math.Max(96, GetDpiForWindow(handle)) / 96d;
    private void SetPosition()
    {
        if (handle == IntPtr.Zero || positioning || dragging) return;
        positioning = true;
        try
        {
            var screen = PreferredScreen();
            // Move first so WPF receives WM_DPICHANGED for the destination monitor before sizing.
            if (Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
                SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left, screen.WorkingArea.Top, 0, 0, 0x15);
            var dpi = WindowDpi; var work = WorkArea(screen);
            effectiveScale = Math.Min(settings.Scale, Math.Min(work.Width / (556 * dpi), work.Height / (430 * dpi)));
            Width = IslandSurface.StageWidth * effectiveScale; Height = IslandSurface.StageHeight * effectiveScale;
            stage.LayoutTransform = new ScaleTransform(effectiveScale, effectiveScale);
            var point = PlacementGeometry.Resolve(PlacementGeometry.Range(work, effectiveScale, dpi), settings.FreePosition, settings.PositionX, settings.PositionY, settings.TopOffset, dpi);
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(point.X), (int)Math.Round(point.Y), 0, 0, 0x15);
        }
        finally { positioning = false; }
    }
    private void SaveDraggedPosition()
    {
        var screen = Forms.Screen.FromHandle(handle); RememberMonitor(screen);
        if (!GetWindowRect(handle, out var rect)) return;
        var normalized = PlacementGeometry.Normalize(PlacementGeometry.Range(WorkArea(screen), effectiveScale, WindowDpi), new Point(rect.Left, rect.Top));
        settings.FreePosition = true; settings.PositionX = normalized.X; settings.PositionY = normalized.Y;
        SetPosition(); Persist();
    }
    private void ResetPosition()
    {
        RememberMonitor(Forms.Screen.FromHandle(handle)); settings.FreePosition = false;
        SetPosition(); Persist(); surface.Notify("Острів зверху по центру", "wink");
    }
    private void NextScreen()
    {
        var screens = Forms.Screen.AllScreens;
        var index = Array.FindIndex(screens, s => s.DeviceName == Forms.Screen.FromHandle(handle).DeviceName);
        RememberMonitor(screens[(index + 1) % screens.Length]);
        SetPosition(); Persist(); surface.Notify("Острів на своєму місці");
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAY_DEVICE
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string name, uint index, ref DISPLAY_DEVICE display, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
