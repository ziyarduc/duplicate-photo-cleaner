using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace DuplicatePhotoCleaner.Converters;

/// <summary>
/// Dosya yolunu bellek dostu thumbnail'e çevirir: tam boyut decode etmez.
/// ConverterParameter = decode genişliği (varsayılan 240).
/// </summary>
public class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        int decodeWidth = 240;
        if (parameter != null && int.TryParse(parameter.ToString(), out var w) && w > 0)
            decodeWidth = w;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = decodeWidth; // sadece küçük çözünürlük decode edilir
            bmp.CacheOption = BitmapCacheOption.OnLoad; // dosya kilidi açmaz
            bmp.EndInit();
            bmp.Freeze(); // UI thread dışından da güvenli
            return bmp;
        }
        catch
        {
            return null; // bozuk/desteklenmeyen resim -> boş görüntü
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
