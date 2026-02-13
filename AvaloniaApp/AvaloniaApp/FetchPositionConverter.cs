using Avalonia.Controls;

using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;
using KafkaLens.Shared.Models;

namespace AvaloniaApp;
public class FetchPositionConverter : IValueConverter
{
    public object Convert(object? position, Type targetType, object? parameter, CultureInfo culture)
    {
        if (position == null)
            return false;

        try
        {
            var value = position.ToString();
            var target = parameter?.ToString();

            if (target == null)
            {
                return value is "Timestamp";
            }

            return value?.Equals(target, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
