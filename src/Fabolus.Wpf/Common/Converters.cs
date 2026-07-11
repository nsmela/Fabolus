using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fabolus.Wpf.Common.Convert;

public class EnumToBooleanConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value == null || parameter == null) return Binding.DoNothing;
        return (bool)value ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
    }
}

public class EnumToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value == null || parameter == null) return Visibility.Collapsed;
        return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is Visibility v)
            return v != Visibility.Visible;
        return false;
    }
}

public class GreaterThanZeroConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is int i) return i > 0;
        if (value is long l) return l > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class GreaterThanZeroToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is int i) return i > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is long l) return l > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringToBrushConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is string s && !string.IsNullOrWhiteSpace(s)) {
            try {
                return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s));
            } catch {
                return Binding.DoNothing;
            }
        }
        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is System.Windows.Media.SolidColorBrush b) return b.Color.ToString();
        return Binding.DoNothing;
    }
}
