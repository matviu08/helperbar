using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HelperBar;

internal static class Ui
{
    public static SolidColorBrush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    public static TextBlock Text(string text, double size = 12, string color = "#EBE8F3", bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), FontFamily = new FontFamily("Segoe UI"),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    public static Button Button(string text, Action click, string? tooltip = null, bool primary = false, bool scaleOnHover = true)
    {
        var button = new Button { Content = text, ToolTip = tooltip, MinHeight = 34, HorizontalContentAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip ?? text);
        if (primary) { button.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); button.Foreground = Brush("#15121E"); button.FontWeight = FontWeights.SemiBold; }
        button.Click += (_, _) => click();
        if (scaleOnHover)
        {
            button.MouseEnter += (_, _) => AnimateScale(button, 1.035);
            button.MouseLeave += (_, _) => AnimateScale(button, 1);
        }
        return button;
    }
    public static bool ReducedMotion { get; set; }
    private static void AnimateScale(Button button, double value)
    {
        var transform = (ScaleTransform)button.RenderTransform;
        var animation = new DoubleAnimation(ReducedMotion ? 1 : value, TimeSpan.FromMilliseconds(ReducedMotion ? 0 : 140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    public static Border Card(UIElement content, Thickness? margin = null) => new()
    {
        Background = Brush("#17181E"), CornerRadius = new CornerRadius(16), BorderBrush = Brush("#24252D"), BorderThickness = new Thickness(1),
        Padding = new Thickness(14), Child = content, Margin = margin ?? new Thickness(0)
    };
    public static StackPanel Column(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel;
    }
    public static Grid Columns(UIElement left, UIElement right, double gap = 10, double first = 1, double second = 1)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(first, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(second, GridUnitType.Star) });
        Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); return grid;
    }
    public static FrameworkElement Gap(double height = 10) => new Border { Height = height };
    public static CheckBox Toggle(string title, bool initial, Action<bool> action)
    {
        var label = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var box = new CheckBox { Content = label, IsChecked = initial };
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        box.Checked += (_, _) => action(true); box.Unchecked += (_, _) => action(false); return box;
    }
    public static void Enter(FrameworkElement element, bool reduced)
    {
        if (reduced) return;
        element.RenderTransform = new TranslateTransform(0, 9);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210)));
        ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
