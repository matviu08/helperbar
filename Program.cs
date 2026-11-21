using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace HelperBar;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0) return SelfTests.Run();
        if (Array.IndexOf(args, "--shutdown") >= 0) { Native.StopExisting(); return 0; }
        if (Array.IndexOf(args, "--media-probe") >= 0) return MediaProbe.RunAsync().GetAwaiter().GetResult();
        if (Array.IndexOf(args, "--battery-probe") >= 0) return BatteryProbe.RunAsync().GetAwaiter().GetResult();
        var musicTest = Array.IndexOf(args, "--music-test") >= 0;
        var dailyTest = Array.IndexOf(args, "--daily-test") >= 0;
        var homeTest = Array.IndexOf(args, "--home-test") >= 0;
        var batteryTest = Array.IndexOf(args, "--battery-test") >= 0;
        var fullscreenTest = Array.IndexOf(args, "--fullscreen-test") >= 0;
        var deviceNoticeTest = Array.IndexOf(args, "--device-notice-test") >= 0;
        var testMode = Array.IndexOf(args, "--smoke-test") >= 0 || musicTest || dailyTest || homeTest || batteryTest || fullscreenTest || deviceNoticeTest;
        using var singleInstance = new Mutex(true, "Local\\HelperBar.Personal.Island", out var first);
        if (!first) { if (testMode) return 2; if (Array.IndexOf(args, "--startup") < 0) Native.NotifyExisting(); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(Settings.DataDirectory); File.AppendAllText(Path.Combine(Settings.DataDirectory, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("Не вдалося виконати дію. Подробиці збережено у data/error.log.\n" + e.Exception.Message, "HelperBar");
            e.Handled = true;
        };
        app.Run(new IslandWindow(testMode, Array.IndexOf(args, "--inspect") >= 0, musicTest, dailyTest, homeTest, batteryTest, fullscreenTest, deviceNoticeTest));
        return Environment.ExitCode;
    }
}
