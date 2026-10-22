using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExHyperV.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace ExHyperV.Tools;

internal static class ConsoleToolbarAppearance
{
    private static readonly string[] BrushKeys =
    [
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
        "TextFillColorTertiaryBrush", "TextFillColorDisabledBrush",
        "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush"
    ];

    internal static void Apply(FrameworkElement toolbar, string? background)
    {
        // WPF-UI controls can have their own foreground defaults; explicitly use the scoped brush.
        BindForeground(toolbar);
        if (!AutoConnectOptions.TryNormalizeToolbarBackground(background, out string normalized)
            || normalized.Length == 0)
        {
            foreach (string key in BrushKeys) toolbar.Resources.Remove(key);
            if (toolbar is Border border)
                border.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
            else if (toolbar is Control control)
                control.ClearValue(Control.BackgroundProperty);
            return;
        }

        Color color = Color.FromRgb(
            byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber),
            byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber));
        Color text = ForegroundFor(color);
        toolbar.Resources[BrushKeys[0]] = FrozenBrush(text);
        toolbar.Resources[BrushKeys[1]] = FrozenBrush(text, 197);
        toolbar.Resources[BrushKeys[2]] = FrozenBrush(text, 153);
        toolbar.Resources[BrushKeys[3]] = FrozenBrush(text, 102);
        toolbar.Resources[BrushKeys[4]] = FrozenBrush(text, 24);
        toolbar.Resources[BrushKeys[5]] = FrozenBrush(text, 40);
        Brush fill = FrozenBrush(color);
        if (toolbar is Border customBorder) customBorder.Background = fill;
        else if (toolbar is Control customControl) customControl.Background = fill;
    }

    private static void BindForeground(DependencyObject element)
    {
        if (element is Control control)
            control.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        else if (element is System.Windows.Controls.TextBlock text)
            text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependencyObject) BindForeground(dependencyObject);
    }

    internal static Color ForegroundFor(Color background)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Linear(background.R) + 0.7152 * Linear(background.G) + 0.0722 * Linear(background.B);
        return luminance > 0.179 ? Colors.Black : Colors.White;
    }

    private static SolidColorBrush FrozenBrush(Color color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
