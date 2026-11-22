using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HelperBar;

public sealed class IslandSurface : FrameworkElement
{
    public const double StageWidth = 600;
    public const double StageHeight = 510;
    public readonly Spring IslandWidth = new(226);
    public readonly Spring IslandHeight = new(48);
    public readonly Spring Expansion = new(0);
    private readonly Spring gazeX = new(0), gazeY = new(0), squish = new(1), hoverAmount = new(0);
    private readonly Spring eyePosition = new(223);
    private readonly Random random = new();
    private readonly List<Spark> sparks = new();
    private readonly Settings settings;
    private readonly IslandNotifications notifications = new();
    private double time, nextBlink = 2.4, blinkStart = -10, moodUntil, rippleAt = -10;
    private double mouseX, mouseY, lastPointerMove;
    private bool idleSleeping;
    private string mood = "idle";
    private bool hover;
    private string lastClock = "", lastDate = "";
    private FormattedText? clockCache, dateCache;
    public bool Expanded { get; set; }
    public bool PetOnly { get; set; }
    public MediaTrack Media { get; set; } = new();
    public AudioMotion? Audio { get; set; }
    public IslandMode CompactMode => IslandState.Resolve(false, time < ToastUntil, PetOnly, TimerRunning, settings.CompactMusic && Media.Available);
    public IslandMode Mode => Expanded ? IslandMode.Expanded : CompactMode;
    public bool ShowCompactMusic => CompactMode == IslandMode.Music;
    public bool TimerRunning { get; set; }
    public string TimerText { get; set; } = "";
    public string TimerTitle { get; set; } = "Фокус";
    public bool TimerIsReminder { get; set; }
    public string Toast => notifications.Current?.Message ?? "";
    public double ToastUntil => notifications.Until;
    internal DeviceNotice? DeviceToast => time < ToastUntil ? notifications.Current?.Device : null;
    internal ReminderNotice? ReminderToast => time < ToastUntil ? notifications.Current?.Reminder : null;
    internal event Action<ReminderNotice>? ReminderShown;
    private string? lastReminderShown;
    public Geometry? BodyGeometry { get; private set; }
    public Rect BodyRect { get; private set; }
    public double Time => time;
    public double Open => Math.Clamp(Expansion.Value, 0, 1);
    public Color AccentColor => (Color)ColorConverter.ConvertFromString(settings.Accent);
    public Brush Accent => Brush(AccentColor);
    public string Mood => mood;
    public Point EyeCenter => new(eyePosition.Value, 41);

    private sealed class Spark
    {
        public double X, Y, Vx, Vy, Age, Life, Size;
        public bool Heart;
    }

    public IslandSurface(Settings settings)
    {
        this.settings = settings;
        Width = StageWidth; Height = StageHeight;
        IsHitTestVisible = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    public void Pointer(Point point, bool over)
    {
        if (Math.Abs(mouseX - point.X) + Math.Abs(mouseY - point.Y) > 2)
        {
            lastPointerMove = time;
            if (idleSleeping) { idleSleeping = false; React("surprised", .9); }
        }
        mouseX = point.X; mouseY = point.Y; hover = over;
        var center = EyeCenter;
        gazeX.Target = Math.Clamp((point.X - center.X) / 100, -1, 1) * 3.6;
        gazeY.Target = Math.Clamp((point.Y - center.Y) / 120, -1, 1) * 3;
    }

    public void React(string expression, double seconds = 2.6)
    {
        idleSleeping = false; mood = expression; moodUntil = time + seconds;
        if (!settings.ReducedMotion) { squish.Velocity = expression == "surprised" ? 4.5 : -3.2; rippleAt = time; }
        if ((expression == "happy" || expression == "celebrate") && !settings.ReducedMotion)
        {
            for (var i = 0; i < (expression == "celebrate" ? 22 : 7); i++)
                sparks.Add(new Spark { X = EyeCenter.X + random.NextDouble() * 28 - 14, Y = 36, Vx = random.NextDouble() * 110 - 55, Vy = -45 - random.NextDouble() * 65, Life = 1 + random.NextDouble() * .6, Size = 2 + random.NextDouble() * 3, Heart = expression == "happy" && i % 2 == 0 });
        }
        InvalidateVisual();
    }

    public void Notify(string message, string expression = "happy", string? key = null, int priority = 0)
    {
        if (notifications.Post(message, expression, key ?? message, priority, time)) React(expression);
    }

    public void DeferNotifications(bool value)
    {
        if (notifications.SetSuspended(value, time)) React(notifications.Current!.Mood);
    }

    internal void NotifyDevice(DeviceNotice notice)
    {
        if (notifications.Post(notice.Message, notice.Mood, notice.Key, notice.Priority, time, notice)) React(notice.Mood);
    }
    internal void RefreshDeviceNotices(IReadOnlyList<DeviceBattery> devices) => notifications.RefreshDevices(devices, settings);
    internal void NotifyReminder(ReminderNotice notice)
    {
        if (notifications.Post(notice.Title + " · час вийшов", "celebrate", notice.Key, 10, time, reminder: notice)) React("celebrate");
    }
    internal void RemoveReminder(string id) => notifications.RemoveReminder(id);

    public void Step(double dt)
    {
        time += dt;
        if (notifications.Advance(time)) React(notifications.Current!.Mood);
        if (!notifications.Suspended && ReminderToast is { } reminder && lastReminderShown != reminder.Key)
        { lastReminderShown = reminder.Key; ReminderShown?.Invoke(reminder); }
        var toast = time < ToastUntil;
        IslandWidth.Target = Mode switch { IslandMode.Expanded => 524, IslandMode.Pet => 136, IslandMode.Notification => DeviceToast != null ? 416 : ReminderToast != null ? 396 : 340, IslandMode.Focus => TimerIsReminder ? 340 : 280, IslandMode.Music => 374, _ => 226 };
        IslandHeight.Target = Mode switch { IslandMode.Expanded => 398, IslandMode.Pet => 56, IslandMode.Notification when DeviceToast != null || ReminderToast != null => 76, _ => 48 };
        if (hover && !Expanded) { IslandWidth.Target += 12; IslandHeight.Target += 3; }
        Expansion.Target = Expanded ? 1 : 0;
        hoverAmount.Target = hover ? 1 : 0;
        IslandWidth.Step(dt, settings.Speed, settings.ReducedMotion);
        IslandHeight.Step(dt, settings.Speed, settings.ReducedMotion);
        eyePosition.Target = PetOnly && !Expanded && !toast ? 300 : (StageWidth - IslandWidth.Value) / 2 + 36;
        eyePosition.Step(dt, settings.Speed, settings.ReducedMotion);
        Expansion.Step(dt, settings.Speed, settings.ReducedMotion);
        hoverAmount.Step(dt, settings.Speed, settings.ReducedMotion);
        gazeX.Step(dt, 1, settings.ReducedMotion); gazeY.Step(dt, 1, settings.ReducedMotion);
        squish.Step(dt, settings.Speed, settings.ReducedMotion);
        if (idleSleeping && (Audio?.Level ?? 0) > .1) React("curious", .8);
        if (time > moodUntil && mood != "idle") mood = "idle";
        if (mood == "idle" && !Expanded && time - lastPointerMove > 22 && !settings.ReducedMotion && (Audio?.Level ?? 0) < .04)
        {
            mood = "sleepy"; idleSleeping = true; moodUntil = double.PositiveInfinity;
        }
        if (time >= nextBlink && !settings.ReducedMotion) { blinkStart = time; nextBlink = time + 3 + random.NextDouble() * 4; }
        for (var i = sparks.Count - 1; i >= 0; i--)
        {
            var p = sparks[i]; p.Age += dt; p.X += p.Vx * dt; p.Y += p.Vy * dt; p.Vy += 80 * dt;
            if (p.Age > p.Life) sparks.RemoveAt(i);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var open = Open;
        var w = IslandWidth.Value;
        var h = IslandHeight.Value;
        var x = (StageWidth - w) / 2;
        const double y = 16;
        var reaction = settings.ReducedMotion ? 0 : Math.Exp(-Math.Max(0, time - rippleAt) * 2.7) * 7;
        var sound = settings.AudioReactive && !settings.ReducedMotion ? Audio?.Level ?? 0 : 0;
        var bass = settings.AudioReactive && !settings.ReducedMotion ? Audio?.Bass ?? 0 : 0;
        var liquid = settings.Liquid && !settings.ReducedMotion ? (1.0 + hoverAmount.Value * 1.8 + reaction + (bass * 6 + sound * 2) * settings.EdgeIntensity) * (1 - open * .86) : 0;
        BodyRect = new Rect(x, y, w, h);
        BodyGeometry = Shape(BodyRect, 25 + open * 7, liquid);

        for (var i = 7; i >= 1; i--)
        {
            var shadow = new Pen(Brush(Color.FromArgb((byte)(3 + (8 - i)), 0, 0, 0)), i * 3.4);
            dc.PushTransform(new TranslateTransform(0, 3)); dc.DrawGeometry(null, shadow, BodyGeometry); dc.Pop();
        }
        if (reaction > .2 || hoverAmount.Value > .05 || sound > .02)
        {
            var accent = DeviceToast?.Kind == DeviceNoticeKind.LowBattery ? (Color)ColorConverter.ConvertFromString("#F0B386") : Media.ArtworkColor ?? AccentColor;
            for (var i = 3; i >= 1; i--) dc.DrawGeometry(null, new Pen(Brush(Color.FromArgb((byte)Math.Clamp((reaction * 2 + hoverAmount.Value * 5 + sound * 22 * settings.GlowIntensity) / i, 0, 40), accent.R, accent.G, accent.B)), i * 3), BodyGeometry);
        }
        dc.DrawGeometry(Brush((Color)ColorConverter.ConvertFromString(settings.Body)), new Pen(Brush("#303137"), 1), BodyGeometry);
        dc.PushClip(BodyGeometry);
        var sheen = new LinearGradientBrush(Color.FromArgb(16, 255, 255, 255), Colors.Transparent, 90);
        dc.DrawRoundedRectangle(sheen, null, new Rect(x + 1, y + 1, w - 2, 48), 24, 24);
        dc.Pop();

        if (settings.Eyes)
        {
            var petScale = PetOnly && time >= ToastUntil ? 1 - open : 0;
            DrawEyes(dc, new Point(eyePosition.Value, y + 24 + petScale * 3), 1 + petScale * .2);
        }
        else
        {
            dc.DrawEllipse(Accent, null, new Point(x + 31, y + 24), 4.5, 4.5);
            dc.DrawEllipse(null, new Pen(Brush("#3C364D"), 1), new Point(x + 31, y + 24), 9, 9);
        }

        if (open > .01)
        {
            dc.PushOpacity(open);
            if (DeviceToast is { } headerNotice) DrawDeviceNotice(dc, headerNotice, new Rect(x + 61, y + 9, w - 181, 48), true);
            else if (ReminderToast is { } headerReminder) DrawReminderNotice(dc, headerReminder, new Rect(x + 61, y + 8, w - 181, 46));
            else
            {
                DrawText(dc, "helper", new Point(x + 61, y + 11), 17, Brush("#F7F6FB"), true);
                DrawText(dc, "bar", new Point(x + 110, y + 11), 17, Accent, true);
                DrawText(dc, "Твій маленький простір", new Point(x + 61, y + 32), 10, Brush("#777983"));
            }
            dc.Pop();
        }
        if (open < .98)
        {
            dc.PushOpacity(Math.Clamp(1 - open * 2, 0, 1));
            if (time < ToastUntil)
            {
                if (DeviceToast is { } deviceNotice) DrawDeviceNotice(dc, deviceNotice, new Rect(x + 65, y + 10, Math.Max(1, w - 85), 54), false);
                else if (ReminderToast is { } reminderNotice) DrawReminderNotice(dc, reminderNotice, new Rect(x + 65, y + 12, Math.Max(1, w - 85), 52));
                else DrawText(dc, Toast, new Point(x + 65, y + 15), 12, Brush("#F1EFF8"), false, w - 85);
            }
            else if (ShowCompactMusic)
            {
                var cover = new Rect(x + 59, y + 9, 30, 30);
                dc.PushClip(new RectangleGeometry(cover, 7, 7));
                dc.DrawRectangle(Brush("#2D273A"), null, cover);
                if (Media.Artwork != null)
                {
                    var ratio = Media.Artwork.PixelWidth / (double)Media.Artwork.PixelHeight;
                    var dw = ratio > 1 ? 30 * ratio : 30; var dh = ratio < 1 ? 30 / ratio : 30;
                    dc.DrawImage(Media.Artwork, new Rect(cover.X + (30 - dw) / 2, cover.Y + (30 - dh) / 2, dw, dh));
                }
                else DrawText(dc, "♫", new Point(cover.X + 7, cover.Y + 4), 17, Accent);
                dc.Pop();
                DrawText(dc, Media.Title, new Point(x + 101, y + 8), 12, Brush("#F4EFFB"), true, w - 166);
                DrawText(dc, Media.Playing ? Media.Artist : "На паузі · " + Media.Artist, new Point(x + 101, y + 26), 9, Brush("#9D90AF"), false, w - 166);
                var color = Media.ArtworkColor ?? AccentColor;
                for (var i = 0; i < 6; i++)
                {
                    var value = 0f;
                    if (Audio != null && !settings.ReducedMotion) for (var j = i * 4; j < i * 4 + 4; j++) value = Math.Max(value, (float)Math.Clamp(Audio.Bands[j] * settings.SpectrumIntensity, 0, 1));
                    var bh = 3 + value * 21;
                    dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(85 + value * 170), color.R, color.G, color.B)), null, new Rect(x + w - 49 + i * 5.2, y + 24 - bh / 2, 3, bh), 1.5, 1.5);
                }
            }
            else if (!PetOnly && TimerRunning && TimerIsReminder)
            {
                dc.DrawLine(new Pen(Brush("#35343C"), 1), new Point(x + 65, y + 15), new Point(x + 65, y + 33));
                DrawText(dc, TimerTitle, new Point(x + 79, y + 8), 12, Brush("#EEE4F8"), true, w - 184);
                DrawText(dc, "Таймер", new Point(x + 79, y + 26), 9, Brush("#9C8BAD"));
                DrawText(dc, TimerText, new Point(x + w - 94, y + 14), 15, Accent, true, 80);
            }
            else if (!PetOnly)
            {
                dc.DrawLine(new Pen(Brush("#35343C"), 1), new Point(x + 65, y + 15), new Point(x + 65, y + 33));
                var clock = DateTime.Now.ToString(settings.ShowSeconds ? "HH:mm:ss" : "HH:mm");
                if (clock != lastClock) { lastClock = clock; clockCache = Text(clock, settings.ShowSeconds ? 14 : 16, Brush("#F5F3FA"), true); }
                dc.DrawText(clockCache!, new Point(x + 80, y + 13));
                if (TimerRunning)
                {
                    dc.DrawEllipse(Accent, null, new Point(x + w - 73, y + 24), 3, 3);
                    DrawText(dc, TimerText, new Point(x + w - 62, y + 16), 11, Accent, true);
                }
                else
                {
                    var date = DateTime.Now.ToString("dd MMM", CultureInfo.GetCultureInfo("uk-UA")).TrimEnd('.');
                    if (date != lastDate) { lastDate = date; dateCache = Text(date, 11, Brush("#9B98A7")); }
                    dc.DrawText(dateCache!, new Point(x + w - 66, y + 17));
                }
            }
            dc.Pop();
        }

        if (!Expanded && settings.Liquid && reaction > 1.5)
        {
            var progress = Math.Clamp((time - rippleAt) / 1.3, 0, 1);
            var drop = new Point(x + w * .68, y + h + 4 + progress * 17);
            dc.DrawEllipse(Brush(settings.Body), new Pen(Brush("#484250"), .8), drop, 3.8 * (1 - progress), 4.5 * (1 - progress));
        }
        foreach (var p in sparks)
        {
            dc.PushOpacity(Math.Clamp(1 - p.Age / p.Life, 0, 1));
            if (p.Heart) DrawText(dc, "♥", new Point(p.X, p.Y), p.Size * 2.2, Accent);
            else dc.DrawEllipse(Accent, null, new Point(p.X, p.Y), p.Size / 2, p.Size / 2);
            dc.Pop();
        }
    }

    private Geometry Shape(Rect r, double radius, double amplitude)
    {
        var g = new StreamGeometry();
        const double k = .55228475;
        var rr = Math.Min(radius, r.Height / 2);
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + rr, r.Top), true, true);
            c.LineTo(new Point(r.Right - rr, r.Top), true, false);
            c.BezierTo(new Point(r.Right - rr + rr * k, r.Top), new Point(r.Right, r.Top + rr - rr * k), new Point(r.Right, r.Top + rr), true, false);
            c.LineTo(new Point(r.Right, r.Bottom - rr), true, false);
            c.BezierTo(new Point(r.Right, r.Bottom - rr + rr * k), new Point(r.Right - rr + rr * k, r.Bottom), new Point(r.Right - rr, r.Bottom), true, false);
            for (var i = 1; i <= 64; i++)
            {
                var t = i / 64d;
                var xx = r.Right - rr - (r.Width - rr * 2) * t;
                var wave = Math.Sin(t * Math.PI) * (Math.Sin(t * Math.PI * 5 + time * 2.9) * .65 + Math.Sin(t * Math.PI * 9 - time * 1.5) * .35);
                var pull = hover ? Math.Exp(-Math.Pow((xx - mouseX) / 45, 2)) * 1.2 * Math.Clamp((mouseY - r.Bottom) / 60, 0, 1) : 0;
                c.LineTo(new Point(xx, r.Bottom + amplitude * (wave + pull)), true, false);
            }
            c.BezierTo(new Point(r.Left + rr - rr * k, r.Bottom), new Point(r.Left, r.Bottom - rr + rr * k), new Point(r.Left, r.Bottom - rr), true, false);
            c.LineTo(new Point(r.Left, r.Top + rr), true, false);
            c.BezierTo(new Point(r.Left, r.Top + rr - rr * k), new Point(r.Left + rr - rr * k, r.Top), new Point(r.Left + rr, r.Top), true, false);
        }
        g.Freeze(); return g;
    }

    private void DrawEyes(DrawingContext dc, Point center, double scale)
    {
        var breathing = settings.ReducedMotion ? 0 : Math.Sin(time * 1.5) * .65;
        var blinkAge = time - blinkStart;
        var blink = blinkAge >= 0 && blinkAge < .19 ? 1 - Math.Sin(blinkAge / .19 * Math.PI) * .96 : 1;
        dc.PushTransform(new TranslateTransform(center.X, center.Y + breathing));
        var soundBounce = settings.AudioReactive && !settings.ReducedMotion ? (Audio?.Bass ?? 0) * .08 * settings.EyeIntensity : 0;
        dc.PushTransform(new ScaleTransform(scale * (2 - squish.Value) * (1 - soundBounce * .5), scale * squish.Value * (1 + soundBounce)));
        for (var i = 0; i < 2; i++)
        {
            var cx = (i == 0 ? -7.4 : 7.4) + gazeX.Value * .5;
            var cy = gazeY.Value * .5;
            var eyeHeight = mood == "surprised" ? 18 : mood == "sleepy" ? 6 : 14;
            var eyeWidth = mood == "surprised" ? 10 : 8.5;
            if (mood == "curious" && i == 1) eyeHeight = 18;
            var thisBlink = mood == "wink" && i == 1 ? .12 : blink;
            var eyeBrush = new LinearGradientBrush(Colors.White, (Color)ColorConverter.ConvertFromString(settings.Accent), 90);
            if (mood == "happy" || mood == "celebrate")
            {
                var arc = new StreamGeometry();
                using (var ctx = arc.Open()) { ctx.BeginFigure(new Point(cx - 3.5, cy + 2), false, false); ctx.QuadraticBezierTo(new Point(cx, cy - 6), new Point(cx + 3.5, cy + 2), true, true); }
                dc.DrawGeometry(null, new Pen(eyeBrush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            }
            else if (mood == "dizzy")
            {
                var pen = new Pen(eyeBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(cx - 3, cy - 3), new Point(cx + 3, cy + 3));
                dc.DrawLine(pen, new Point(cx + 3, cy - 3), new Point(cx - 3, cy + 3));
            }
            else
            {
                var eh = Math.Max(1.5, eyeHeight * thisBlink);
                dc.DrawRoundedRectangle(eyeBrush, null, new Rect(cx - eyeWidth / 2, cy - eh / 2, eyeWidth, eh), eyeWidth / 2, eyeWidth / 2);
                if (eh > 6 && mood != "sleepy") dc.DrawEllipse(Brush("#302B44"), null, new Point(cx + gazeX.Value * .45, cy + gazeY.Value * .55), 1.35, 1.9);
            }
        }
        if (mood == "sleepy") DrawText(dc, "z", new Point(13, -15 - Math.Sin(time * 2) * 2), 8, Accent);
        if (mood == "curious") DrawText(dc, "?", new Point(16, -20), 10, Accent, true);
        dc.Pop(); dc.Pop();
    }

    private void DrawReminderNotice(DrawingContext dc, ReminderNotice notice, Rect area)
    {
        dc.PushClip(new RectangleGeometry(area));
        DrawText(dc, notice.Title, new Point(area.X, area.Y + 2), 15, Brush("#F3EBFC"), true, area.Width);
        DrawText(dc, "Час вийшов · " + ReminderTimers.DurationLabel(notice.DurationSeconds), new Point(area.X, area.Y + 25), 10, Brush("#A5DEC5"), false, area.Width);
        dc.Pop();
    }
    private void DrawDeviceNotice(DrawingContext dc, DeviceNotice notice, Rect area, bool header)
    {
        dc.PushClip(new RectangleGeometry(area));
        var color = Brush(notice.Kind == DeviceNoticeKind.LowBattery ? "#F0B386" : "#A5DEC5");
        var glyph = notice.Device.Kind switch { PeripheralKind.Mouse => "\uE962", PeripheralKind.Keyboard => "\uE765", _ => "\uE7F6" };
        var icon = new FormattedText(glyph, CultureInfo.GetCultureInfo("uk-UA"), FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), 20, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(icon, new Point(area.Left, area.Top + 16));
        var textX = area.Left + 30;
        DrawText(dc, notice.Title, new Point(textX, area.Top), 8.5, color, true, Math.Max(1, area.Width - 30));
        var level = Text(notice.Device.Percent.HasValue ? notice.Device.LevelText : "—", header ? 16 : 20, color, true);
        var levelX = Math.Max(textX, area.Right - level.Width);
        dc.DrawText(level, new Point(levelX, area.Top + 15));
        DrawText(dc, notice.Device.Name, new Point(textX, area.Top + 17), header ? 12 : 14, Brush("#F4EBFF"), true, Math.Max(1, levelX - textX - 12));
        if (notice.Device.Percent is { } percent)
        {
            var bar = new Rect(textX, area.Bottom - 4, Math.Max(1, area.Right - textX), 3);
            dc.DrawRoundedRectangle(Brush("#35313D"), null, bar, 1.5, 1.5);
            var progress = settings.ReducedMotion ? 1 : 1 - Math.Pow(1 - Math.Clamp((4.5 - (ToastUntil - time)) / .45, 0, 1), 3);
            var width = bar.Width * Math.Clamp(percent / 100d, 0, 1) * progress;
            if (width > 0) dc.DrawRoundedRectangle(color, null, new Rect(bar.Left, bar.Top, width, bar.Height), 1.5, 1.5);
        }
        else DrawText(dc, "Заряд недоступний", new Point(textX, area.Bottom - 12), 9, Brush("#94889E"), false, Math.Max(1, area.Right - textX));
        dc.Pop();
    }

    private FormattedText Text(string value, double size, Brush brush, bool bold = false) => new(value, CultureInfo.GetCultureInfo("uk-UA"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void DrawText(DrawingContext dc, string value, Point point, double size, Brush brush, bool bold = false, double maxWidth = 0)
    {
        var text = Text(value, size, brush, bold);
        if (maxWidth > 0) { text.MaxTextWidth = maxWidth; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        dc.DrawText(text, point);
    }
    private static SolidColorBrush Brush(string hex) => Brush((Color)ColorConverter.ConvertFromString(hex));
    private static SolidColorBrush Brush(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
}
