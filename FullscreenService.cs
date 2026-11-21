using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace HelperBar;

internal static class FullscreenGeometry
{
    // Physical screen pixels, including negative coordinates and displays with a different DPI.
    public static bool Covers(Rect client, Rect monitor, bool hasCaption = false) => !hasCaption &&
        monitor.Width > 0 && monitor.Height > 0 && client.Width > 0 && client.Height > 0 &&
        client.Left <= monitor.Left + 2 && client.Top <= monitor.Top + 2 &&
        client.Right >= monitor.Right - 2 && client.Bottom >= monitor.Bottom - 2;
}

internal enum FullscreenPresentation { Normal, Hidden, Eyes }

internal sealed class FullscreenVisibility
{
    public bool Active { get; private set; }
    public bool UserHidden { get; private set; }
    public bool Revealed { get; private set; }
    public bool Eyes { get; private set; }
    public FullscreenPresentation Presentation => UserHidden ? FullscreenPresentation.Hidden :
        Active && !Revealed ? Eyes ? FullscreenPresentation.Eyes : FullscreenPresentation.Hidden : FullscreenPresentation.Normal;
    public void Observe(bool fullscreen, bool enabled, bool eyes, bool islandForeground)
    {
        Active = fullscreen && enabled; Eyes = eyes;
        if (!Active || !islandForeground) Revealed = false;
    }
    public void ShowManually() { UserHidden = false; Revealed = Active; }
    public void HideManually() { UserHidden = true; Revealed = false; }
    public void EndReveal() => Revealed = false;
}

internal sealed class FullscreenDebounce
{
    private bool candidate;
    private double since;
    public bool Active { get; private set; }
    public bool Update(bool value, double now)
    {
        if (candidate != value) { candidate = value; since = now; }
        if (Active != candidate && now - since >= (candidate ? .35 : .65)) Active = candidate;
        return Active;
    }
    public void Reset() { candidate = Active = false; since = 0; }
}

internal readonly record struct FullscreenReading(bool Fullscreen, bool IslandForeground);

internal sealed class FullscreenDetector
{
    private IntPtr lastFullscreen;
    public FullscreenReading Read(IntPtr island)
    {
        var foreground = GetForegroundWindow();
        var own = foreground == island || (foreground != IntPtr.Zero && GetAncestor(foreground, 3) == island);
        // Opening our own panel is an explicit temporary reveal, not an exit from the underlying full-screen app.
        var candidate = own ? lastFullscreen : foreground;
        var fullscreen = IsFullscreen(candidate, island);
        if (!own) lastFullscreen = fullscreen ? candidate : IntPtr.Zero;
        return new(fullscreen, own);
    }
    internal static bool IsFullscreen(IntPtr window, IntPtr island)
    {
        if (window == IntPtr.Zero || window == island || window == GetShellWindow() || !IsWindowVisible(window) || IsIconic(window)) return false;
        var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var monitor = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(island, 2), ref monitor) || !GetClientRect(window, out var client)) return false;
        var first = new Native.POINT { X = client.Left, Y = client.Top }; var last = new Native.POINT { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(window, ref first) || !ClientToScreen(window, ref last) || last.X <= first.X || last.Y <= first.Y) return false;
        var caption = (Native.GetWindowLongPtr(window, -16).ToInt64() & 0x00C00000L) == 0x00C00000L;
        return FullscreenGeometry.Covers(new Rect(first.X, first.Y, last.X - first.X, last.Y - first.Y),
            new Rect(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right - monitor.Bounds.Left, monitor.Bounds.Bottom - monitor.Bounds.Top), caption);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Bounds, WorkArea; public uint Flags; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Native.POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
}
