using Avalonia.Data.Converters;
using KafkaLens.ViewModels;
using System;
using System.Globalization;

namespace AvaloniaApp;

public class NodeTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ITreeNode.NodeType nodeType && parameter is string target)
        {
            return nodeType.ToString().Equals(target, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
