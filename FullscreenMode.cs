using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private readonly FullscreenDetector fullscreenDetector = new();
    private readonly FullscreenDebounce fullscreenDebounce = new();
    private readonly FullscreenVisibility fullscreenVisibility = new();
    private readonly DispatcherTimer fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool fullscreenQuietApplied, fullscreenWasExpanded, applyingFullscreen;
    private bool fullscreenClickThrough;
    private bool fullscreenTimerFinished;
    private CheckBox? fullscreenEnabledBox;
    private FrameworkElement? fullscreenChoices;
    private Button? fullscreenHideButton, fullscreenEyesButton;
    private TextBlock? fullscreenStatus;

    private void StartFullscreenMonitoring()
    {
        fullscreenTimer.Tick += CheckFullscreen;
        CheckFullscreen(null, EventArgs.Empty); fullscreenTimer.Start();
    }
    private void CheckFullscreen(object? sender, EventArgs e)
    {
        if (closed || handle == IntPtr.Zero || dragging) return;
        if (!settings.FullscreenEnabled)
        {
            fullscreenDebounce.Reset(); fullscreenVisibility.Observe(false, false, settings.FullscreenEyes, false);
        }
        else
        {
            var reading = fullscreenDetector.Read(handle);
            fullscreenVisibility.Observe(fullscreenDebounce.Update(reading.Fullscreen, renderClock.Elapsed.TotalSeconds), true, settings.FullscreenEyes, reading.IslandForeground);
            if (reading.IslandForeground && surface.Expanded && !fullscreenQuietApplied) fullscreenVisibility.ShowManually();
        }
        ApplyFullscreenPresentation();
    }
    private void ApplyFullscreenPresentation()
    {
        if (closed || applyingFullscreen || handle == IntPtr.Zero) return;
        applyingFullscreen = true;
        try
        {
            var quiet = fullscreenVisibility.Active && !fullscreenVisibility.Revealed;
            surface.DeferNotifications(fullscreenVisibility.Active);
            if (!fullscreenVisibility.Active && fullscreenTimerFinished) { fullscreenTimerFinished = false; PlayTimerAlert(); }
            if (quiet && !fullscreenQuietApplied)
            {
                fullscreenWasExpanded = surface.Expanded;
                SetExpanded(false, false); fullscreenQuietApplied = true;
                hoverTime = 0; pressPoint = null; stage.ReleaseMouseCapture();
            }
            else if (!quiet && fullscreenQuietApplied)
            {
                fullscreenQuietApplied = false; surface.PetOnly = settings.PetOnly;
                SetExpanded(fullscreenWasExpanded, false);
            }
            var presentation = fullscreenVisibility.Presentation;
            SetFullscreenClickThrough(presentation == FullscreenPresentation.Eyes);
            if (presentation == FullscreenPresentation.Hidden)
            { if (IsVisible) Hide(); }
            else
            {
                if (presentation == FullscreenPresentation.Eyes) surface.PetOnly = true;
                if (!IsVisible) ShowWithoutActivation();
            }
            UpdateFullscreenPreferences();
        }
        finally { applyingFullscreen = false; }
    }
    private void SetFullscreenClickThrough(bool value)
    {
        if (fullscreenClickThrough == value) return;
        fullscreenClickThrough = value; stage.IsHitTestVisible = !value;
        var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
        Native.SetWindowLongPtr(handle, -20, new IntPtr(value ? style | 0x20L | 0x08000000L : style & ~0x20L));
    }
    private void ShowWithoutActivation()
    {
        var style = Native.GetWindowLongPtr(handle, -20);
        Native.SetWindowLongPtr(handle, -20, new IntPtr(style.ToInt64() | 0x08000000L));
        var previous = ShowActivated; ShowActivated = false;
        try { Show(); }
        finally { ShowActivated = previous; Native.SetWindowLongPtr(handle, -20, style); }
    }
    private void ShowIslandManually()
    {
        fullscreenVisibility.ShowManually(); ApplyFullscreenPresentation();
        if (!IsVisible) ShowWithoutActivation();
    }
    private void HideIslandManually()
    {
        fullscreenVisibility.HideManually(); fullscreenWasExpanded = false;
        SetExpanded(false, false); ApplyFullscreenPresentation();
    }
    private void ToggleIslandManually()
    {
        var reveal = !IsVisible || fullscreenVisibility.Presentation != FullscreenPresentation.Normal;
        ShowIslandManually(); SetExpanded(reveal || !surface.Expanded);
    }
    private void TogglePetManually()
    {
        // While temporarily reduced to eyes, toggle the user's saved mode, not that temporary appearance.
        ShowIslandManually(); surface.PetOnly = settings.PetOnly; TogglePet();
    }
    private void PlayTimerAlert()
    {
        if (settings.TimerSound) System.Media.SystemSounds.Asterisk.Play();
        tray?.ShowBalloonTip(5000, "Фокус завершено", "Час трохи відпочити.", System.Windows.Forms.ToolTipIcon.Info);
    }
    private void ClearFullscreenPreferences()
    {
        fullscreenEnabledBox = null; fullscreenChoices = null; fullscreenHideButton = fullscreenEyesButton = null; fullscreenStatus = null;
    }
    private FrameworkElement FullscreenPreferences()
    {
        fullscreenEnabledBox = Ui.Toggle("Не заважати у повному екрані", settings.FullscreenEnabled, enabled =>
        { settings.FullscreenEnabled = enabled; Persist(); CheckFullscreen(null, EventArgs.Empty); });
        fullscreenHideButton = HomeSmallButton("Приховувати острів", () => SetFullscreenEyes(false));
        fullscreenEyesButton = HomeSmallButton("Залишати лише очі", () => SetFullscreenEyes(true));
        fullscreenChoices = Ui.Columns(fullscreenHideButton, fullscreenEyesButton);
        fullscreenStatus = Ui.Text("", 10, "#A99AB9");
        var panel = Ui.Column(SettingsGroup("Коли граєш або дивишся відео", fullscreenEnabledBox, Ui.Gap(7), fullscreenChoices, Ui.Gap(9), fullscreenStatus),
            SettingsGroup("Як це працює", Ui.Text("На екрані з островом. Очі пропускають натискання. Сповіщення й звук таймера зачекають; відлік триває.", 10, "#A99AB9"), Ui.Gap(7),
                Ui.Text(settings.OpenShortcut + " — відкрити острів тимчасово.", 10, "#A99AB9")));
        UpdateFullscreenPreferences(); return panel;
    }
    private void SetFullscreenEyes(bool value)
    { settings.FullscreenEyes = value; Persist(); CheckFullscreen(null, EventArgs.Empty); }
    private void UpdateFullscreenPreferences()
    {
        if (fullscreenChoices == null) return;
        fullscreenChoices.IsEnabled = settings.FullscreenEnabled;
        foreach (var (button, selected) in new[] { (fullscreenHideButton!, !settings.FullscreenEyes), (fullscreenEyesButton!, settings.FullscreenEyes) })
        {
            button.Background = Ui.Brush(selected ? "#3C2F50" : "#201C28"); button.BorderBrush = Ui.Brush(selected ? "#8B73AD" : "#3C3448");
        }
        fullscreenStatus!.Text = !settings.FullscreenEnabled ? "Автоматичний режим вимкнений" : fullscreenVisibility.Active ?
            fullscreenVisibility.Revealed ? "Острів відкритий тимчасово · повернись до гри, щоб сховати" : "Повний екран · не відволікаю" : "Увімкнено · чекаю на гру або повноекранне відео";
    }
}
