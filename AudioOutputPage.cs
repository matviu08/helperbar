using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private AudioOutputService? outputService;
    private Button? musicOutput, homeOutput;
    private Popup? audioOutputsPopup;
    private StackPanel? audioOutputRows;
    private TextBlock? audioOutputHint;
    private string? audioOutputError;

    private void StartAudioControls()
    {
        volumeService = new AudioVolumeService(); volumeService.Changed += UpdateVolumeControls; volumeService.Start();
        outputService = new AudioOutputService(); outputService.Changed += UpdateAudioOutputs; outputService.Start();
    }
    private Button CreateOutputButton(bool home = false)
    {
        Button button = null!;
        button = Ui.Button("", () => ShowAudioOutputs(button), "Вибрати аудіовихід", scaleOnHover: false);
        button.Height = button.MinHeight = home ? 22 : 34;
        button.Padding = new Thickness(home ? 5 : 7, 0, 5, 0);
        button.BorderThickness = new Thickness(0); button.Background = Ui.Brush(home ? "#24202D" : "#292330");
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(19) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        var icon = Ui.Text("\uE767", 12, "#BEAFD5"); icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        var label = HomeLine("Звук", home ? 9 : 10, "#D9CFE6"); Grid.SetColumn(label, 1);
        var arrow = Ui.Text("⌄", 11, "#A296B3"); arrow.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(arrow, 2);
        content.Children.Add(icon); content.Children.Add(label); content.Children.Add(arrow); button.Content = content;
        UpdateOutputButton(button, outputService?.State ?? AudioOutputs.Initial);
        return button;
    }
    private static void UpdateOutputButton(Button? button, AudioOutputs state)
    {
        if (button == null) return;
        var content = (Grid)button.Content;
        ((TextBlock)content.Children[0]).Text = state.Current?.Headphones == true ? "\uE7F6" : "\uE767";
        ((TextBlock)content.Children[1]).Text = state.Busy ? "Змінюємо…" : state.Current?.ShortName ?? (state.Loading ? "Шукаємо…" : "Аудіовихід");
        button.ToolTip = state.Current == null ? "Вибрати аудіовихід" : "Аудіовихід: " + state.Current.Name + "\nНатисни, щоб змінити";
        AutomationProperties.SetName(button, "Вибрати аудіовихід: " + (state.Current?.Name ?? "не вибрано"));
        button.IsEnabled = !state.Busy;
    }
    private void UpdateAudioOutputs(AudioOutputs state)
    {
        if (closed) return;
        UpdateOutputButton(musicOutput, state); UpdateOutputButton(homeOutput, state);
        UpdateVolumeControls(volumeService?.State ?? VolumeState.Unavailable);
        if (audioOutputsPopup?.IsOpen == true) RenderAudioOutputRows(state);
    }
    private bool CanAdjustVolume(VolumeState state) => state.Available && outputService?.State.Busy != true &&
        (outputService == null || !outputService.State.Loading && state.EndpointId == outputService.State.DefaultId);

    private void ShowAudioOutputs(Button anchor)
    {
        if (audioOutputsPopup?.IsOpen == true) { CloseAudioOutputs(); return; }
        audioOutputError = null;
        var heading = Ui.Text("Куди виводити звук", 14, "#F0E9F8", true);
        var refresh = Ui.Button("↻", () => { audioOutputError = null; outputService?.Refresh(); RenderAudioOutputRows(outputService?.State ?? AudioOutputs.Initial); }, "Оновити пристрої", scaleOnHover: false);
        refresh.Width = 27; refresh.Height = refresh.MinHeight = 27; refresh.Padding = new Thickness(0); refresh.BorderThickness = new Thickness(0);
        var header = new DockPanel { Margin = new Thickness(3, 0, 0, 8) }; DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh); header.Children.Add(heading);
        audioOutputRows = new StackPanel();
        var scroll = new ScrollViewer { Content = audioOutputRows, MaxHeight = 224, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        audioOutputHint = Ui.Text("", 10, "#A89AB8"); audioOutputHint.Margin = new Thickness(5, 9, 5, 8);
        var windows = Ui.Button("Налаштування звуку Windows  ↗", () => { CloseAudioOutputs(); Launch("ms-settings:sound"); }, "Відкрити налаштування звуку Windows", scaleOnHover: false);
        windows.FontSize = 10; windows.MinHeight = 28; windows.Padding = new Thickness(5, 3, 5, 3); windows.BorderThickness = new Thickness(0); windows.Background = Ui.Brush("#292330");
        var shell = new Border { Width = 310, Padding = new Thickness(12), CornerRadius = new CornerRadius(16), Background = Ui.Brush("#1D1825"), BorderBrush = Ui.Brush("#44374F"), BorderThickness = new Thickness(1), Child = Ui.Column(header, scroll, audioOutputHint, windows) };
        shell.Resources.MergedDictionaries.Add(Resources);
        KeyboardNavigation.SetTabNavigation(shell, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(shell, KeyboardNavigationMode.Contained);
        audioOutputsPopup = new Popup { Child = shell, PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 6, AllowsTransparency = true, StaysOpen = false, PopupAnimation = settings.ReducedMotion ? PopupAnimation.None : PopupAnimation.Fade };
        shell.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseAudioOutputs(); anchor.Focus(); e.Handled = true; } };
        RenderAudioOutputRows(outputService?.State ?? AudioOutputs.Initial);
        audioOutputsPopup.IsOpen = true; outputService?.Refresh();
        shell.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private void RenderAudioOutputRows(AudioOutputs state)
    {
        if (audioOutputRows == null || audioOutputHint == null) return;
        var focused = (Keyboard.FocusedElement as Button)?.Tag as string;
        audioOutputRows.Children.Clear();
        foreach (var device in state.Devices)
        {
            var selected = state.DefaultId == device.Id;
            var row = Ui.Button("", async () => await SelectAudioOutput(device.Id), device.Name, scaleOnHover: false);
            row.Tag = device.Id; row.MinHeight = 46; row.Margin = new Thickness(0, 2, 0, 2); row.Padding = new Thickness(10, 5, 10, 5);
            row.BorderThickness = new Thickness(0); row.Background = Ui.Brush(selected ? "#382C49" : "#25202E"); row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.IsEnabled = !state.Busy;
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            var icon = Ui.Text(device.Headphones ? "\uE7F6" : "\uE767", 17, "#C8B5E4"); icon.FontFamily = new FontFamily("Segoe MDL2 Assets"); layout.Children.Add(icon);
            var labels = Ui.Column(HomeLine(device.Name, 11, "#EEE5F8", true), HomeLine(state.SwitchingId == device.Id ? "Перемикаємо…" : selected ? "Зараз відтворює" : "Натисни, щоб вибрати", 9, selected ? "#C3A6E5" : "#968AA4"));
            labels.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(labels, 1); layout.Children.Add(labels);
            var mark = Ui.Text(selected ? "✓" : "", 14, "#D1B7F2"); mark.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(mark, 2); layout.Children.Add(mark); row.Content = layout;
            AutomationProperties.SetName(row, device.Name); AutomationProperties.SetItemStatus(row, selected ? "Поточний аудіовихід" : "Доступний аудіовихід"); audioOutputRows.Children.Add(row);
        }
        if (state.Devices.Count == 0)
        {
            var empty = Ui.Text(state.Loading ? "Шукаємо аудіопристрої…" : "Підключи навушники або колонки — вони з’являться тут.", 12, "#C6B8D6"); empty.Margin = new Thickness(7, 12, 7, 12); audioOutputRows.Children.Add(empty);
        }
        audioOutputHint.Text = audioOutputError ?? state.Error ?? (state.Busy ? "Зачекай, Windows змінює аудіовихід…" : "Змінює системний вихід. Список оновлюється автоматично.");
        if (focused != null) audioOutputRows.Children.OfType<Button>().FirstOrDefault(x => Equals(x.Tag, focused))?.Focus();
    }
    private async Task SelectAudioOutput(string id)
    {
        if (outputService == null || outputService.State.Busy) return;
        notifyVolume = false; volumeService?.CancelPending(); audioOutputError = null;
        var pending = outputService.SelectAsync(id); UpdateAudioOutputs(outputService.State);
        var result = await pending;
        if (closed) return;
        audioOutputError = result.Success ? null : result.Message;
        UpdateAudioOutputs(outputService.State);
        if (result.Success) { CloseAudioOutputs(); surface.Notify("Звук · " + result.Message, "happy", "audio-output"); }
        else if (audioOutputsPopup?.IsOpen == true) surface.React("curious", .8);
        else surface.Notify("Не вдалося перемкнути звук. Спробуй ще раз.", "curious", "audio-output");
    }
    private void CloseAudioOutputs()
    {
        if (audioOutputsPopup != null) audioOutputsPopup.IsOpen = false;
        audioOutputsPopup = null; audioOutputRows = null; audioOutputHint = null;
    }
}
