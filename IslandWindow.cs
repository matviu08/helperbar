using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HelperBar;

public sealed partial class IslandWindow : Window
{
    private readonly Settings settings;
    private readonly IslandSurface surface;
    private readonly Canvas stage = new();
    private readonly Grid expanded = new();
    private readonly Grid pageHost = new();
    private readonly Grid tabs = new();
    private readonly FocusTimer focus = new();
    private readonly DispatcherTimer secondTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Stopwatch renderClock = Stopwatch.StartNew();
    private Forms.NotifyIcon? tray;
    private IntPtr handle;
    private HwndSource? source;
    private string currentPage = "Головна";
    private TextBlock? clockLabel, dateLabel, ramLabel, powerLabel, timerLabel, timerStateLabel, noteStateLabel;
    private Button? timerToggle;
    private Border? progressFill;
    private double lastFrame, hoverTime;
    private Point? pressPoint;
    private bool dragged, closed, loadedOnce, hoverArmed = true;
    private ShortcutRegistration? shortcuts;
    private readonly bool smokeTest;
    private readonly bool inspectionMode;
    private readonly bool musicTest;
    private string[] pageNames = { "Головна", "Музика", "Фокус", "Нотатки", "Вигляд" };

    public IslandWindow(bool smokeTest = false, bool inspectionMode = false, bool musicTest = false, bool dailyTest = false, bool homeTest = false, bool batteryTest = false, bool fullscreenTest = false, bool deviceNoticeTest = false, bool audioOutputTest = false, bool reminderTest = false, bool todoTest = false, bool fileDropTest = false)
    {
        this.smokeTest = smokeTest;
        this.inspectionMode = inspectionMode;
        this.musicTest = musicTest;
        settings = smokeTest ? new Settings { NotifyDeviceConnections = false, NotifyLowBattery = false } : Settings.Load();
        Ui.ReducedMotion = settings.ReducedMotion;
        focus.Set(settings.FocusMinutes);
        Title = "HelperBar — твій острів";
        Width = IslandSurface.StageWidth * settings.Scale; Height = IslandSurface.StageHeight * settings.Scale;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = inspectionMode; ShowActivated = inspectionMode;
        FontFamily = new FontFamily("Segoe UI");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/HelperBar;component/Styles.xaml", UriKind.Relative) });
        Resources["AccentBrush"] = Ui.Brush(settings.Accent);
        stage.Width = IslandSurface.StageWidth; stage.Height = IslandSurface.StageHeight;
        stage.Background = Brushes.Transparent; stage.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
        surface = new IslandSurface(settings);
        surface.ReminderShown += OnReminderShown;
        surface.PetOnly = settings.PetOnly;
        surface.Audio = audioMotion;
        media.Changed += OnMediaChanged;
        deviceBatteries.Changed += OnDeviceBatteriesChanged;
        stage.Children.Add(surface);
        Content = stage;
        BuildExpanded();
        InitializeFileDrop();
        stage.MouseLeftButtonDown += OnPointerDown;
        stage.MouseLeftButtonUp += OnPointerUp;
        stage.MouseMove += OnPointerMove;
        stage.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        stage.MouseWheel += (_, e) =>
        {
            if (!surface.Expanded) { ChangeVolume(e.Delta > 0 ? .02f : -.02f); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FilesPanelOpen) { CancelFileDrag(); CloseFilesPanel(); e.Handled = true; return; }
            if (e.Key == Key.Escape && currentPage == "Нотатки" && settings.NotesView == "tasks" && todoEditingId != null)
            { CancelTodoEdit(); e.Handled = true; return; }
            if (!capturingShortcut && e.Key == Key.Escape) { SetExpanded(false); e.Handled = true; }
        };
        SourceInitialized += (_, _) => InitializeWindow();
        DpiChanged += (_, _) => { if (!positioning && !dragging) Dispatcher.BeginInvoke(SetPosition); };
        Loaded += async (_, _) =>
        {
            if (loadedOnce) return;
            loadedOnce = true;
            SetPosition();
            UpdateShortcutLabels();
            CompositionTarget.Rendering += OnRender;
            secondTimer.Tick += OnSecond; secondTimer.Start();
            saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
            if (fileDropTest) await RunFileDropIntegrationTest();
            else if (todoTest) await RunTodoIntegrationTest();
            else if (reminderTest) await RunReminderIntegrationTest();
            else if (audioOutputTest) await RunAudioOutputIntegrationTest();
            else if (deviceNoticeTest) await RunDeviceNoticeIntegrationTest();
            else if (fullscreenTest) await RunFullscreenIntegrationTest();
            else if (batteryTest) await RunBatteryIntegrationTest();
            else if (homeTest) await RunHomeIntegrationTest();
            else if (dailyTest) await RunDailyIntegrationTest();
            else if (musicTest) await RunMusicIntegrationTest();
            else if (smokeTest) await SmokeTest();
            else
            {
                StartFullscreenMonitoring();
                UpdateDeviceBatteryMonitoring();
                StartAudioControls();
                if (settings.AudioReactive) audio = new AudioSpectrum();
                await media.StartAsync();
                if (closed) return;
                if (settings.LoadWarning is { } warning) surface.Notify(warning, "curious");
                else surface.Notify("Привіт! Клікни поруч з очима", "happy");
            }
        };
        Closing += (_, _) =>
        {
            closed = true; CompositionTarget.Rendering -= OnRender;
            secondTimer.Stop(); saveTimer.Stop(); SaveNow();
            fullscreenTimer.Stop(); fullscreenTimer.Tick -= CheckFullscreen;
            CloseAudioOutputs(); outputService?.Dispose();
            media.Dispose(); audio?.Dispose(); volumeService?.Dispose();
            deviceBatteries.Dispose();
            shortcuts?.Dispose();
            source?.RemoveHook(WindowMessage);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        };
    }

    private void InitializeWindow()
    {
        handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle); source?.AddHook(WindowMessage);
        var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
        Native.SetWindowLongPtr(handle, -20, new IntPtr(inspectionMode ? (style & ~0x80L) | 0x40000L : style | 0x80L | 0x08000000L));
        shortcuts = new ShortcutRegistration((id, key) => Native.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.VirtualKey), id => Native.UnregisterHotKey(handle, id));
        if (Shortcut.TryParse(settings.OpenShortcut, out var openKey)) shortcuts.Set("open", openKey);
        if (Shortcut.TryParse(settings.PetShortcut, out var petKey)) shortcuts.Set("pet", petKey);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        if (!smokeTest) CreateTray();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(SetPosition);

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0084) // Only the visible island receives mouse input; the rest is desktop.
        {
            if (fullscreenClickThrough) { handled = true; return new IntPtr(-1); }
            var packed = lParam.ToInt64();
            var point = stage.PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            if (!IsInside(point) && !(fileDropFixture && new Rect(105, 432, 390, 64).Contains(point))) { handled = true; return new IntPtr(-1); }
        }
        if (msg == Native.WM_HOTKEY)
        {
            if (capturingShortcut) { handled = true; return IntPtr.Zero; }
            if (shortcuts?.ActionFor(wParam.ToInt32()) == "open") ToggleIslandManually();
            if (shortcuts?.ActionFor(wParam.ToInt32()) == "pet") TogglePetManually();
            handled = true;
        }
        if (msg == Native.WM_SHOWISLAND) { ShowIslandManually(); SetExpanded(true); handled = true; }
        return IntPtr.Zero;
    }

    private bool IsInside(Point p) => surface.BodyGeometry?.FillContains(p) ?? new Rect(187, 16, 226, 48).Contains(p);

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Відкрити острів", null, (_, _) => Dispatcher.Invoke(() => { ShowIslandManually(); SetExpanded(true); }));
        menu.Items.Add("Лише очі / звичайний режим", null, (_, _) => Dispatcher.Invoke(TogglePetManually));
        menu.Items.Add("Налаштування", null, (_, _) => Dispatcher.Invoke(() => { ShowIslandManually(); SwitchPage("Вигляд"); SetExpanded(true); }));
        menu.Items.Add("Перенести на наступний екран", null, (_, _) => Dispatcher.Invoke(NextScreen));
        menu.Items.Add("Повернути нагору по центру", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Приховати", null, (_, _) => Dispatcher.Invoke(HideIslandManually));
        menu.Items.Add("Вийти", null, (_, _) => Dispatcher.Invoke(Close));
        tray = new Forms.NotifyIcon { Text = "HelperBar · " + settings.OpenShortcut, Icon = CreateTrayIcon(), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleIslandManually);
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var black = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(12, 12, 18));
            using var lavender = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(204, 191, 255));
            g.FillEllipse(black, 0, 2, 32, 28); g.FillEllipse(lavender, 8, 9, 6, 14); g.FillEllipse(lavender, 19, 9, 6, 14);
        }
        var nativeIcon = bitmap.GetHicon();
        try { using var borrowed = System.Drawing.Icon.FromHandle(nativeIcon); return (System.Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(nativeIcon); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private void ShowMenu()
    {
        var menu = new ContextMenu { Background = Ui.Brush("#19191F"), Foreground = Ui.Brush("#EAE7F3"), BorderBrush = Ui.Brush("#36333F") };
        void Add(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(surface.Expanded ? "Згорнути" : "Відкрити", () => SetExpanded(!surface.Expanded));
        Add("Лише очі / годинник", TogglePet);
        Add("Налаштування", () => { SwitchPage("Вигляд"); SetExpanded(true); });
        Add("Повернути нагору по центру", ResetPosition);
        Add("Наступний екран", NextScreen);
        menu.Items.Add(new Separator());
        Add("Приховати у трей", HideIslandManually);
        Add("Вийти", Close);
        menu.IsOpen = true;
    }

    private void SetExpanded(bool value, bool activate = true)
    {
        if (!value) { CloseAudioOutputs(); CloseFilesPanel(); }
        if (value && activate && !applyingFullscreen) ShowIslandManually();
        surface.Expanded = value;
        expanded.IsHitTestVisible = value;
        expanded.Visibility = Visibility.Visible;
        var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
        Native.SetWindowLongPtr(handle, -20, new IntPtr(value ? style & ~0x08000000L : style | 0x08000000L));
        if (value) { if (activate) Activate(); surface.React("curious", 1.1); }
        else { Keyboard.ClearFocus(); surface.React("wink", .8); hoverArmed = false; }
        if (!value && activate && !applyingFullscreen && fullscreenVisibility.Revealed)
        { fullscreenVisibility.EndReveal(); ApplyFullscreenPresentation(); }
    }

    private void TogglePet() { surface.PetOnly = !surface.PetOnly; settings.PetOnly = surface.PetOnly; Persist(); SetExpanded(false); surface.React("happy"); }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(stage);
        if (!IsInside(p) || (surface.Expanded && p.Y > 75)) return;
        // Let real header buttons handle their own clicks.
        for (DependencyObject? parent = e.OriginalSource as DependencyObject; parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Button) return;
        pressPoint = p; dragged = false;
        if (e.ClickCount == 2) { SetExpanded(!surface.Expanded); pressPoint = null; e.Handled = true; return; }
        stage.CaptureMouse(); e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (pressPoint is not { } origin || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(stage);
        if (!dragged && (p - origin).Length > 5)
        {
            dragged = true; stage.ReleaseMouseCapture();
            dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { }
            finally { dragging = false; }
            SaveDraggedPosition();
            pressPoint = null;
        }
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (pressPoint is not { } origin) return;
        pressPoint = null; stage.ReleaseMouseCapture();
        if (dragged || (e.GetPosition(stage) - origin).Length > 5) return;
        if (settings.Eyes && Math.Abs(origin.X - surface.EyeCenter.X) < 24)
        {
            surface.React("happy");
        }
        else
        {
            if (!surface.Expanded && surface.DeviceToast != null)
            {
                if (BatteryEnabled) SwitchPage("Головна"); else OpenSettings("devices");
            }
            else if (!surface.Expanded && (surface.ReminderToast != null || surface.TimerIsReminder && surface.TimerRunning))
            { settings.FocusView = "timers"; reminderForm = false; SwitchPage("Фокус"); }
            else if (!surface.Expanded && surface.ShowCompactMusic) SwitchPage("Музика");
            SetExpanded(!surface.Expanded);
        }
        e.Handled = true;
    }

    private void OnRender(object? sender, EventArgs e)
    {
        if (closed || !IsVisible) { lastFrame = renderClock.Elapsed.TotalSeconds; return; }
        var now = renderClock.Elapsed.TotalSeconds;
        var elapsed = now - lastFrame;
        var activeMotion = surface.Expanded || Math.Abs(surface.IslandWidth.Target - surface.IslandWidth.Value) > .1 || surface.Time < surface.ToastUntil || audioMotion.Level > .015 || (surface.Mood != "idle" && surface.Mood != "sleepy");
        if (elapsed < (settings.ReducedMotion ? 1d / 15 : activeMotion ? 1d / 60 : 1d / 30)) return;
        lastFrame = now;
        var dt = Math.Min(elapsed, .05);
        if (Native.GetCursorPos(out var cursor))
        {
            var point = stage.PointFromScreen(new Point(cursor.X, cursor.Y));
            var over = IsInside(point);
            surface.Pointer(point, over);
            if (!over) hoverArmed = true;
            if (over && hoverArmed && !surface.Expanded && settings.OpenOnHover && pressPoint == null && !fullscreenQuietApplied && !fileDragActive) hoverTime += dt;
            else hoverTime = 0;
            if (hoverTime > .65) { SetExpanded(true); hoverTime = 0; }
        }
        audioMotion.Step(audio?.Snapshot ?? SpectrumFrame.Silent, dt, settings.AudioSensitivity, settings.AudioReactive && !settings.ReducedMotion);
        surface.Step(dt);
        UpdateMusicFrame();
        expanded.Opacity = Math.Clamp((surface.Open - .22) / .78, 0, 1);
        expanded.RenderTransform = new TranslateTransform(0, (1 - surface.Open) * -7);
        if (!surface.Expanded && surface.Open < .002) expanded.Visibility = Visibility.Hidden;
        expanded.Clip = new RectangleGeometry(new Rect(0, 0, 480, Math.Max(0, surface.IslandHeight.Value - 20)), 12, 12);
    }

    private void OnSecond(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        RefreshTodoDay(DateOnly.FromDateTime(now.LocalDateTime));
        ProcessReminders(now);
        if (focus.Tick(now))
        {
            surface.Notify("Час вийшов. Ти молодець!", "celebrate", "timer", 10);
            if (fullscreenVisibility.Active) fullscreenTimerFinished = true;
            else PlayTimerAlert();
        }
        surface.TimerRunning = focus.Running;
        surface.TimerText = FocusTimer.Format(focus.Remaining(now));
        surface.TimerIsReminder = false; surface.TimerTitle = "Фокус";
        var nextReminder = settings.Reminders.Next;
        if (nextReminder != null && (!focus.Running || nextReminder.Remaining(now) < focus.Remaining(now)))
        {
            surface.TimerRunning = surface.TimerIsReminder = true; surface.TimerTitle = nextReminder.Title;
            surface.TimerText = ReminderTimers.Format(nextReminder.Remaining(now));
        }
        if (clockLabel != null) clockLabel.Text = DateTime.Now.ToString("HH:mm");
        if (dateLabel != null) dateLabel.Text = DateTime.Now.ToString("dddd, d MMMM", System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));
        if (ramLabel != null) { var mem = Native.Memory(); ramLabel.Text = $"{mem.Percent}%  ·  {mem.Used:0.0} / {mem.Total:0} ГБ"; }
        if (powerLabel != null) powerLabel.Text = Native.Power();
        if (timerLabel != null) timerLabel.Text = FocusTimer.Format(focus.Remaining(now));
        UpdateFocusTodo();
        if (timerToggle != null)
        {
            timerToggle.Content = focus.Running ? "Ⅱ   Пауза" : "▶   " + (focus.Finished ? "Ще раз" : "Почати");
            System.Windows.Automation.AutomationProperties.SetName(timerToggle, timerToggle.Content.ToString());
        }
        if (progressFill != null) progressFill.Width = Math.Clamp((1 - focus.Remaining(now).TotalSeconds / focus.Duration.TotalSeconds) * 420, 0, 420);
        UpdateHomeWidgets();
    }

    private void Persist() { if (smokeTest) return; saveTimer.Stop(); saveTimer.Start(); }
    private void SaveNow()
    {
        if (smokeTest) return;
        try { settings.Save(); if (noteStateLabel != null) noteStateLabel.Text = "Збережено на цьому комп’ютері"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (noteStateLabel != null) noteStateLabel.Text = "Не вдалося зберегти: папка недоступна";
            if (!closed) surface.Notify("Не вдалося зберегти налаштування", "surprised");
        }
    }

    private void Launch(string path)
    {
        try { Native.Open(path); SetExpanded(false); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { surface.Notify("Не вдалося відкрити застосунок", "curious"); }
    }

    private async Task SmokeTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        await Task.Delay(800); Capture(Path.Combine(directory, "01-compact.png"));
        SetExpanded(true); await Task.Delay(900); Capture(Path.Combine(directory, "02-home.png"));
        foreach (var name in pageNames.Skip(1))
        {
            SwitchPage(name); await Task.Delay(400); Capture(Path.Combine(directory, name + ".png"));
        }
        if (!await CheckSettingsNavigation(directory)) { Close(); return; }
        SetExpanded(false); surface.PetOnly = true; surface.React("happy"); await Task.Delay(600); Capture(Path.Combine(directory, "03-eyes.png"));
        surface.Notify("Час вийшов. Ти молодець!", "celebrate"); await Task.Delay(300); Capture(Path.Combine(directory, "04-notification.png"));
        File.WriteAllText(Path.Combine(directory, "smoke-test.txt"), "PASS: native WPF window; compact, expanded, 5 pages, 6 settings sections, pet and notification rendered; settings navigation and scoped reset passed; animations ran without dispatcher exceptions.");
        Close();
    }

    private void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap((int)stage.ActualWidth, (int)stage.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
