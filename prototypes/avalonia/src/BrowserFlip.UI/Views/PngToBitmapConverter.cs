using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BrowserFlip.UI.Views;

/// <summary>
/// Turns the raw PNG bytes a platform icon provider returns into an Avalonia bitmap.
/// Keeping the conversion here is what lets the view models stay free of UI types.
/// </summary>
public sealed class PngToBitmapConverter : IValueConverter
{
    /// <summary>Icons are shown at 32 px; 64 px keeps them crisp on Retina displays.</summary>
    private const int DecodeWidth = 64;

    public static PngToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            // Platform icons can be 1024 px; the list shows them at 32 px. Decoding to a bounded
            // width keeps UI memory small no matter how large the source icon is.
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
        }
#pragma warning disable CA1031 // A bad icon must never break the list.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
