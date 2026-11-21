using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private readonly NowPlayingService media = new();
    private AudioSpectrum? audio;
    private AudioVolumeService? volumeService;
    private Slider? volumeSlider;
    private Button? volumeMute;
    private TextBlock? volumePercent;
    private bool updatingVolume, notifyVolume;
    private readonly AudioMotion audioMotion = new();
    private TextBlock? musicTitle, musicArtist, musicAlbum, musicElapsed, musicDuration, musicStatus, coverPlaceholder;
    private Button? musicPlay, musicPrevious, musicNext, musicSource;
    private Image? musicCover;
    private Border? musicCard;
    private Slider? musicSeek;
    private SpectrumView? musicSpectrum;
    private bool musicSeeking, updatingMusic;
    private ImageSource? displayedCover;
    private Color? displayedCoverColor;

    private FrameworkElement MusicPage()
    {
        var track = media.Track;
        var hero = new Grid { Height = 88 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        var artwork = new Grid { Width = 88, Height = 88, Clip = new RectangleGeometry(new Rect(0, 0, 88, 88), 13, 13), Background = Ui.Brush("#292333") };
        coverPlaceholder = Ui.Text("♫", 38, "#B9A8D1"); coverPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        artwork.Children.Add(coverPlaceholder);
        musicCover = new Image { Stretch = Stretch.UniformToFill }; artwork.Children.Add(musicCover); hero.Children.Add(artwork);
        musicSource = Ui.Button("Очікуємо плеєр", ShowMusicSources, "Вибрати плеєр");
        musicSource.FontSize = 9; musicSource.Padding = new Thickness(7, 2, 7, 2); musicSource.MinHeight = 20; musicSource.HorizontalAlignment = HorizontalAlignment.Left;
        musicSource.Background = Ui.Brush("#272331"); musicSource.BorderThickness = new Thickness(0);
        musicTitle = Ui.Text(track.Title, 18, "#F5F0FC", true); musicTitle.TextWrapping = TextWrapping.NoWrap; musicTitle.TextTrimming = TextTrimming.CharacterEllipsis; musicTitle.Margin = new Thickness(0, 5, 0, 0);
        musicArtist = Ui.Text(track.Artist, 12, "#B8ADCA"); musicArtist.TextWrapping = TextWrapping.NoWrap; musicArtist.TextTrimming = TextTrimming.CharacterEllipsis; musicArtist.Margin = new Thickness(0, 3, 0, 0);
        musicAlbum = Ui.Text("", 10, "#83788F"); musicAlbum.TextWrapping = TextWrapping.NoWrap; musicAlbum.TextTrimming = TextTrimming.CharacterEllipsis; musicAlbum.Margin = new Thickness(0, 3, 0, 0);
        var metadata = Ui.Column(musicSource, musicTitle, musicArtist, musicAlbum); Grid.SetColumn(metadata, 2); hero.Children.Add(metadata);
        musicSpectrum = new SpectrumView { Height = 26, Motion = audioMotion, Margin = new Thickness(0, 7, 0, 1) };
        musicSeek = new Slider { Minimum = 0, Maximum = 1, Height = 18, SmallChange = .01, LargeChange = .05 };
        System.Windows.Automation.AutomationProperties.SetName(musicSeek, "Позиція треку");
        musicSeek.PreviewMouseLeftButtonDown += (_, _) => musicSeeking = true;
        musicSeek.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(async (_, _) => await CommitSeek()), true);
        musicSeek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, _) => await CommitSeek()), true);
        musicSeek.PreviewKeyDown += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down) musicSeeking = true; };
        musicSeek.PreviewKeyUp += async (_, _) => await CommitSeek();
        musicSeek.ValueChanged += (_, _) =>
        {
            if (!updatingMusic && musicSeeking && musicElapsed != null) musicElapsed.Text = MediaTrack.FormatTime(TimeSpan.FromSeconds(media.Track.Duration.TotalSeconds * musicSeek.Value));
        };
        musicElapsed = Ui.Text("0:00", 9, "#8E829E"); musicDuration = Ui.Text("—", 9, "#8E829E"); musicDuration.HorizontalAlignment = HorizontalAlignment.Right;
        var timeline = Ui.Columns(musicElapsed, musicDuration, 0);
        var controls = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 38 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        var transport = new StackPanel { Orientation = Orientation.Horizontal };
        musicPrevious = Ui.Button("❮❮", async () => await RunMediaCommand("previous"), "Попередній трек"); musicPrevious.Width = 34; musicPrevious.Padding = new Thickness(0); musicPrevious.BorderThickness = new Thickness(0);
        musicPlay = Ui.Button("▶", async () => await RunMediaCommand("toggle"), "Відтворити", true); musicPlay.Width = 48; musicPlay.Padding = new Thickness(0); musicPlay.Margin = new Thickness(7, 0, 7, 0); musicPlay.FontSize = 16;
        musicNext = Ui.Button("❯❯", async () => await RunMediaCommand("next"), "Наступний трек"); musicNext.Width = 34; musicNext.Padding = new Thickness(0); musicNext.BorderThickness = new Thickness(0);
        transport.Children.Add(musicPrevious); transport.Children.Add(musicPlay); transport.Children.Add(musicNext); controls.Children.Add(transport);
        musicStatus = Ui.Text("", 9, "#8B7E99"); musicStatus.Margin = new Thickness(12, 0, 4, 0); Grid.SetColumn(musicStatus, 1); controls.Children.Add(musicStatus);
        var volume = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        volumeMute = Ui.Button("♪", () => { notifyVolume = true; volumeService?.ToggleMute(); }, "Увімкнути / вимкнути системний звук");
        volumeMute.Width = 30; volumeMute.MinHeight = 28; volumeMute.Height = 28; volumeMute.Padding = new Thickness(0);
        volumeSlider = new Slider { Minimum = 0, Maximum = 100, Width = 105, SmallChange = 2, LargeChange = 10, Margin = new Thickness(7, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Гучність Windows" };
        System.Windows.Automation.AutomationProperties.SetName(volumeSlider, "Гучність Windows");
        volumeSlider.ValueChanged += (_, _) =>
        {
            if (updatingVolume) return;
            volumePercent!.Text = $"{volumeSlider.Value:0}%";
            volumeService?.SetLevel((float)(volumeSlider.Value / 100));
        };
        volumePercent = Ui.Text("—", 10, "#BFB2CF"); volumePercent.Width = 36; volumePercent.TextAlignment = TextAlignment.Right;
        volume.Children.Add(volumeMute); volume.Children.Add(volumeSlider); volume.Children.Add(volumePercent);
        Grid.SetColumn(volume, 2); controls.Children.Add(volume);
        musicCard = Ui.Card(Ui.Column(hero, musicSpectrum, musicSeek, timeline, controls));
        displayedCover = null; displayedCoverColor = null;
        UpdateMusicCard();
        UpdateVolumeControls(volumeService?.State ?? VolumeState.Unavailable);
        return musicCard;
    }

    private void ClearMusicPage()
    {
        musicTitle = musicArtist = musicAlbum = musicElapsed = musicDuration = musicStatus = coverPlaceholder = null;
        musicPlay = musicPrevious = musicNext = musicSource = null; musicCover = null; musicCard = null; musicSeek = null; musicSpectrum = null; musicSeeking = false; displayedCover = null; displayedCoverColor = null;
        volumeSlider = null; volumeMute = null; volumePercent = null;
    }

    private void OnMediaChanged(MediaTrack track)
    {
        if (closed) return;
        surface.Media = track;
        UpdateMusicCard();
        UpdateHomeMusic();
    }

    private void UpdateMusicCard()
    {
        if (musicTitle == null) return;
        var track = media.Track;
        musicTitle.Text = track.Title; musicTitle.ToolTip = track.Title;
        musicArtist!.Text = track.Artist; musicArtist.ToolTip = track.Artist;
        musicAlbum!.Text = track.Available ? (track.Album.Length > 0 ? track.Album : track.Playing ? "Зараз грає" : "На паузі") : "Обкладинка й назва з’являться автоматично";
        musicSource!.Content = track.Source + (media.Sources.Count > 1 ? "  ▾" : ""); musicSource.IsEnabled = media.Sources.Count > 1;
        System.Windows.Automation.AutomationProperties.SetName(musicSource, "Плеєр: " + track.Source);
        musicPlay!.Content = track.Playing ? "Ⅱ" : "▶"; musicPlay.IsEnabled = track.CanToggle;
        musicPlay.ToolTip = track.Playing ? "Пауза" : "Відтворити"; System.Windows.Automation.AutomationProperties.SetName(musicPlay, (string)musicPlay.ToolTip);
        musicPrevious!.IsEnabled = track.CanPrevious; musicNext!.IsEnabled = track.CanNext; musicSeek!.IsEnabled = track.CanSeek;
        if (!ReferenceEquals(displayedCover, track.Artwork))
        {
            displayedCover = track.Artwork; musicCover!.Source = track.Artwork;
            if (track.Artwork != null && !settings.ReducedMotion) musicCover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        }
        coverPlaceholder!.Visibility = track.Artwork == null ? Visibility.Visible : Visibility.Hidden;
        var tint = track.ArtworkColor ?? surface.AccentColor;
        if (displayedCoverColor != tint)
        {
            displayedCoverColor = tint;
            musicCard!.Resources["AccentBrush"] = new SolidColorBrush(tint);
            musicCard!.Background = new LinearGradientBrush(Color.FromRgb((byte)(18 + tint.R * .12), (byte)(18 + tint.G * .12), (byte)(22 + tint.B * .12)), Color.FromRgb(18, 18, 24), 40);
            musicPlay.Background = new SolidColorBrush(tint); musicSpectrum!.Color = tint;
        }
        UpdateMusicFrame();
    }

    private void UpdateMusicFrame()
    {
        if (musicElapsed == null) return;
        var track = media.Track;
        if (!musicSeeking)
        {
            updatingMusic = true;
            var elapsed = track.PositionAt(DateTimeOffset.Now) - track.Start;
            musicElapsed.Text = MediaTrack.FormatTime(elapsed);
            musicSeek!.Value = track.Duration.TotalSeconds > 0 ? Math.Clamp(elapsed.TotalSeconds / track.Duration.TotalSeconds, 0, 1) : 0;
            updatingMusic = false;
        }
        musicDuration!.Text = track.Duration > TimeSpan.Zero ? MediaTrack.FormatTime(track.Duration) : track.Available ? "Наживо" : "—";
        musicStatus!.Text = settings.AudioReactive ? (audio?.Available == true ? (audioMotion.Level > .015 ? "●  Звук наживо" : "Тиша") : audio?.Status ?? "Звук вимкнено") : "Анімацію вимкнено";
        musicSpectrum!.ReducedMotion = settings.ReducedMotion; musicSpectrum.Intensity = settings.SpectrumIntensity; musicSpectrum.InvalidateVisual();
    }

    private void UpdateVolumeControls(VolumeState state)
    {
        if (closed) return;
        UpdateHomeVolume(state);
        if (volumeSlider != null)
        {
            updatingVolume = true;
            if (!volumeSlider.IsMouseCaptureWithin) volumeSlider.Value = state.Level * 100;
            volumeSlider.IsEnabled = state.Available; volumeMute!.IsEnabled = state.Available;
            volumePercent!.Text = state.Available ? $"{state.Percent}%" : "—";
            volumeMute.Content = state.Muted ? "✕" : "♪";
            volumeMute.ToolTip = !state.Available ? "Аудіовихід недоступний" : state.Muted ? "Звук вимкнений · увімкнути" : "Вимкнути звук";
            System.Windows.Automation.AutomationProperties.SetName(volumeMute, volumeMute.ToolTip.ToString());
            updatingVolume = false;
        }
        if (notifyVolume)
        {
            notifyVolume = false;
            surface.Notify(state.Available ? state.Muted ? $"Звук вимкнений · {state.Percent}%" : $"Гучність · {state.Percent}%" : "Аудіовихід недоступний", "wink", "volume");
        }
    }
    private void ChangeVolume(float delta)
    {
        if (volumeService?.State.Available != true) { surface.Notify("Аудіовихід недоступний", "curious"); return; }
        notifyVolume = true; volumeService.Change(delta);
        // At either boundary there may be no change event.
        surface.Notify(volumeService.State.Muted ? "Звук вимкнений" : $"Гучність · {Math.Clamp(volumeService.State.Percent + delta * 100, 0, 100):0}%", "wink", "volume");
    }

    private async System.Threading.Tasks.Task CommitSeek()
    {
        if (!musicSeeking || musicSeek == null) return;
        var fraction = musicSeek.Value; musicSeeking = false;
        if (!await media.CommandAsync("seek", fraction)) surface.Notify("Цей плеєр не дозволив перемотування", "curious");
    }
    private async System.Threading.Tasks.Task RunMediaCommand(string command)
    {
        if (!await media.CommandAsync(command)) surface.Notify("Плеєр не виконав команду", "curious");
        else surface.React("wink", .5);
    }
    private void ShowMusicSources()
    {
        var menu = new ContextMenu { Background = Ui.Brush("#211C2B"), Foreground = Ui.Brush("#F0E9F8"), BorderBrush = Ui.Brush("#463B55") };
        var automatic = new MenuItem { Header = "Автоматично · активний плеєр" }; automatic.Click += (_, _) => media.SelectSource(null); menu.Items.Add(automatic);
        foreach (var source in media.Sources)
        {
            var item = new MenuItem { Header = source.Name, IsChecked = source.Id == media.Track.SourceId }; item.Click += (_, _) => media.SelectSource(source.Id); menu.Items.Add(item);
        }
        menu.PlacementTarget = musicSource; menu.IsOpen = true;
    }
}
