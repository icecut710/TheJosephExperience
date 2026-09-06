using System.Globalization;
using System.Windows.Data;
using JosephExperience.Models;

namespace JosephExperience.Utilities;

/// <summary>
/// WPF IValueConverter that maps any enum value to its friendly display name
/// via <see cref="DisplayNames"/>.
/// </summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public static readonly EnumDisplayConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (value is not Enum enumValue) return value;
        return DisplayNames.GetFriendlyName(enumValue) ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}

/// <summary>
/// Specialized converter for <see cref="ImagePosition"/> — returns the friendly
/// name (e.g. "Top Left") instead of the raw enum member.
/// </summary>
public sealed class ImagePositionDisplayConverter : IValueConverter
{
    public static readonly ImagePositionDisplayConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (value is ImagePosition pos)
            return DisplayNames.GetFriendlyName<ImagePosition>(pos);
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}
