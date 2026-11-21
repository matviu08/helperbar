using System;
using System.Windows;
using System.Windows.Media;

namespace HelperBar;

public sealed class AudioMotion
{
    public float[] Bands { get; } = new float[SpectrumAnalyzer.BandCount];
    public float Level { get; private set; }
    public float Bass { get; private set; }
    public void Step(SpectrumFrame frame, double dt, double sensitivity = 1, bool enabled = true)
    {
        for (var i = 0; i < Bands.Length; i++) Bands[i] = Ease(Bands[i], enabled ? (float)Math.Clamp(frame.Bands[i] * sensitivity, 0, 1) : 0, dt);
        Level = Ease(Level, enabled ? (float)Math.Clamp(frame.Level * sensitivity, 0, 1) : 0, dt);
        Bass = Ease(Bass, enabled ? (float)Math.Clamp(frame.Bass * sensitivity, 0, 1) : 0, dt);
    }
    private static float Ease(float current, float target, double dt) => (float)(current + (target - current) * (1 - Math.Exp(-Math.Clamp(dt, 0, .2) / (target > current ? .045 : .22))));
}

public sealed class SpectrumView : FrameworkElement
{
    public AudioMotion? Motion { get; set; }
    public Color Color { get; set; } = Color.FromRgb(188, 172, 255);
    public bool ReducedMotion { get; set; }
    public double Intensity { get; set; } = 1;
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var count = SpectrumAnalyzer.BandCount; var gap = 4d; var barWidth = Math.Max(2, (width - gap * (count - 1)) / count);
        for (var i = 0; i < count; i++)
        {
            var value = ReducedMotion ? 0 : Math.Clamp((Motion?.Bands[i] ?? 0) * Intensity, 0, 1);
            var h = 2 + value * (height - 4); var x = i * (barWidth + gap); var y = (height - h) / 2;
            var brush = new LinearGradientBrush(Color.FromArgb((byte)(60 + value * 195), Color.R, Color.G, Color.B), Color.FromArgb((byte)(30 + value * 100), Color.R, Color.G, Color.B), 90);
            dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, h), 2.5, 2.5);
            if (value > .7) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(135, 255, 255, 255)), null, new Rect(x + 1, y, Math.Max(1, barWidth - 2), 1.3), .6, .6);
        }
    }
}
