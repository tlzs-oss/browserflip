using System.Windows;
using System.Windows.Controls;

namespace BrowserFlip.App.Controls;

/// <summary>How <see cref="HeaderPanel"/> placed its two parts in the last layout pass.</summary>
public enum HeaderLayoutMode
{
    /// <summary>Brand and tools side by side; the brand fits on its natural lines.</summary>
    SingleLine,

    /// <summary>Brand and tools side by side; the brand text wraps into the space left of the tools.</summary>
    Wrapped,

    /// <summary>Not enough width for both: the brand takes the full width and the tools go to a second row (right-aligned).</summary>
    TwoRows,
}

/// <summary>
/// BF-07: main window header. First child = brand (icon, title, tagline), second child = tools (language, pin,
/// compact, refresh). The tools always get their full desired size and are never pushed out or overlapped:
/// the brand gets what is left. Degradation: one line, then the tagline wraps, then the tools move to a second row.
/// (Before: a DockPanel with the brand in a horizontal StackPanel, which measures its children with infinite
/// width, so the tagline could neither trim nor wrap and was cut off under the language box.)
/// </summary>
public sealed class HeaderPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(HeaderPanel), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap), typeof(double), typeof(HeaderPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Wrapped mode is accepted while the brand grows by at most this factor in height compared with one line
    /// (title + tagline on two lines); beyond that the tools go to a second row.
    /// </summary>
    public static readonly DependencyProperty MaxWrapGrowthProperty = DependencyProperty.Register(
        nameof(MaxWrapGrowth), typeof(double), typeof(HeaderPanel), new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Horizontal space between brand and tools (DIP).</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Vertical space between the brand and the tools in <see cref="HeaderLayoutMode.TwoRows"/> (DIP).</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public double MaxWrapGrowth
    {
        get => (double)GetValue(MaxWrapGrowthProperty);
        set => SetValue(MaxWrapGrowthProperty, value);
    }

    /// <summary>More than one device pixel at any scale (DIP): covers layout rounding of the brand's measured width.</summary>
    private const double RoundingReserve = 1;

    /// <summary>Layout chosen by the last measure pass.</summary>
    public HeaderLayoutMode Mode { get; private set; } = HeaderLayoutMode.SingleLine;

    /// <summary>Width the header needs to show everything on one line without wrapping (DIP; last measure pass).</summary>
    public double SingleLineWidth { get; private set; }

    private UIElement? Brand => InternalChildren.Count > 0 ? InternalChildren[0] : null;

    private UIElement? Tools => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var brand = Brand;
        var tools = Tools;
        if (brand is null || tools is null)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(availableSize);
            }

            return brand?.DesiredSize ?? default;
        }

        tools.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var t = tools.DesiredSize;

        // The brand's natural (one-line) size. Layout rounding snaps it to device pixels, which can be a fraction below
        // the text's real width: measuring the brand again with that rounded width wraps the last character. So the
        // one-line brand keeps this measurement (no second measure) and needs RoundingReserve more than it reports.
        brand.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = brand.DesiredSize;
        SingleLineWidth = natural.Width + RoundingReserve + Gap + t.Width;

        var width = availableSize.Width;
        if (double.IsInfinity(width) || SingleLineWidth <= width)
        {
            Mode = HeaderLayoutMode.SingleLine;
            return new Size(double.IsInfinity(width) ? SingleLineWidth : width, Math.Max(natural.Height, t.Height));
        }

        var beside = width - Gap - t.Width;
        if (beside > 0)
        {
            // Measured a little narrower than its slot: arrange rounding can then never make the slot narrower than
            // the measured lines (which would wrap again and clip the last line).
            brand.Measure(new Size(Math.Max(0, beside - RoundingReserve), double.PositiveInfinity));
            var b = brand.DesiredSize;
            if (b.Width <= beside + 0.5 && b.Height <= (natural.Height * MaxWrapGrowth) + 0.5)
            {
                Mode = HeaderLayoutMode.Wrapped;
                return new Size(width, Math.Max(b.Height, t.Height));
            }
        }

        Mode = HeaderLayoutMode.TwoRows;
        brand.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, brand.DesiredSize.Height + RowGap + t.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var brand = Brand;
        var tools = Tools;
        if (brand is null || tools is null)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(finalSize));
            }

            return finalSize;
        }

        var t = tools.DesiredSize;
        if (Mode == HeaderLayoutMode.TwoRows)
        {
            var b = brand.DesiredSize;
            brand.Arrange(new Rect(0, 0, finalSize.Width, b.Height));
            tools.Arrange(new Rect(Math.Max(0, finalSize.Width - t.Width), b.Height + RowGap, Math.Min(t.Width, finalSize.Width), t.Height));
            return finalSize;
        }

        var brandWidth = Math.Max(0, finalSize.Width - Gap - t.Width);
        brand.Arrange(new Rect(0, Math.Max(0, (finalSize.Height - brand.DesiredSize.Height) / 2), brandWidth, brand.DesiredSize.Height));
        tools.Arrange(new Rect(Math.Max(0, finalSize.Width - t.Width), Math.Max(0, (finalSize.Height - t.Height) / 2), t.Width, t.Height));
        return finalSize;
    }
}
