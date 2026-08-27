using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ArenaDrafter;

public sealed class PortraitImage : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(PortraitImage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(Stretch), typeof(PortraitImage),
        new FrameworkPropertyMetadata(Stretch.UniformToFill, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(PortraitImage),
        new FrameworkPropertyMetadata("RSL", FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        var width = double.IsInfinity(availableSize.Width) ? desired.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? desired.Height : availableSize.Height;
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (Source is not null && bounds.Width > 0 && bounds.Height > 0)
        {
            drawingContext.DrawImage(Source, ImageRect(Source, bounds, Stretch));
            return;
        }

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(24, 42, 49), Color.FromRgb(11, 20, 26), 135);
            drawingContext.DrawRectangle(background, new Pen(new SolidColorBrush(Color.FromRgb(79, 179, 168)), 1), bounds);

            var label = Initials(PlaceholderText);
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Bahnschrift SemiCondensed"), Math.Max(14, Math.Min(28, ActualWidth * 0.28)),
                new SolidColorBrush(Color.FromRgb(200, 155, 74)), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { TextAlignment = TextAlignment.Center, MaxTextWidth = Math.Max(1, ActualWidth - 8) };
            drawingContext.DrawText(text, new Point(4, Math.Max(0, (ActualHeight - text.Height) / 2)));
        }
    }

    private static Rect ImageRect(ImageSource source, Rect bounds, Stretch stretch)
    {
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0 || stretch == Stretch.Fill) return bounds;

        var scale = stretch == Stretch.Uniform
            ? Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight)
            : Math.Max(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new Rect(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }

    private static string Initials(string? value)
    {
        var words = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 1) return string.Concat(words[0][0], words[^1][0]).ToUpperInvariant();
        var compact = new string((value ?? "RSL").Where(char.IsLetterOrDigit).Take(3).ToArray());
        return string.IsNullOrWhiteSpace(compact) ? "RSL" : compact.ToUpperInvariant();
    }
}
