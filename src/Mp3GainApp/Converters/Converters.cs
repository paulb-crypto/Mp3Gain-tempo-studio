// =================================================================================================
// FILE: Converters.cs
// LOCATION: Mp3GainApp/Converters/Converters.cs
//
// WHAT THIS FILE DOES:
//   This file contains WPF Value Converters. Converters translate raw data into user-friendly
//   formats for your screen.
//   For example:
//     - Translating raw seconds into a music clock like "03:45".
//     - Adding "+/- dB" to gain numbers.
//     - Choosing Green for safe audio or Red when clipping is detected.
//
// HOW IT RELATES TO THE APP:
//   - MainWindow.xaml uses these converters to format data bindings cleanly for the user.
// =================================================================================================

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Mp3GainApp.Converters
{
    /// <summary>Shows or hides an element based on true/false</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool val = value is bool b && b;
            if (Invert) val = !val;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    /// <summary>Formats raw time (seconds) into clean "01:23" or "01:15:30" display</summary>
    public class TimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TimeSpan ts)
            {
                return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
            }
            return "00:00";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => TimeSpan.Zero;
    }

    /// <summary>Formats Decibel numbers nicely with plus/minus sign (e.g. "+3.5 dB" or "-2.0 dB")</summary>
    public class GainDisplayConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d > 0 ? $"+{d:F1} dB" : $"{d:F1} dB";
            }
            return "0.0 dB";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => 0.0;
    }

    /// <summary>Returns Green if audio is safe, or Red if audio is clipping</summary>
    public class ClippingColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isClipping && isClipping)
            {
                return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Vivid Red
            }
            return new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Clean Green
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => false;
    }
}
