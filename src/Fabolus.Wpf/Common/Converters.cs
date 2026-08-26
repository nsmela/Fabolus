using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fabolus.Wpf.Common.Convert;

public class EnumToBooleanConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is null || parameter is null) return Binding.DoNothing;
        return (bool)value ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
    }
}

public class EnumToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is null || parameter is null) return Visibility.Collapsed;
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

/// <summary>
/// Turns a camera look direction into a light direction rotated off the view axis.
///
/// Pointing a DirectionalLight3D straight down Camera.LookDirection makes a headlight: the
/// light vector is parallel to the view vector, so N.L peaks wherever a surface faces the
/// camera and the model shades as a radial gradient regardless of its curvature. Rotating
/// the light off the view axis is what makes a mould cavity, a crease or a dish read. The
/// light stays camera-relative, so orbiting never leaves the model unlit.
/// </summary>
public class LightOffsetConverter : IValueConverter {

    /// <summary>Degrees to swing the light left of the view axis. Negative swings right.</summary>
    public double OffsetDegrees { get; set; } = 22.0;

    /// <summary>Degrees to raise the light above the view axis. Negative lowers it.</summary>
    public double ElevationDegrees { get; set; } = 16.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is not System.Windows.Media.Media3D.Vector3D look || look.Length < 1e-6) {
            return new System.Windows.Media.Media3D.Vector3D(-1, 1, -1); // camera not ready yet
        }

        look.Normalize();

        // Viewport3DX.ModelUpDirection is +Z
        var right = System.Windows.Media.Media3D.Vector3D.CrossProduct(
            look, new System.Windows.Media.Media3D.Vector3D(0, 0, 1));
        if (right.Length < 1e-6) {
            right = new System.Windows.Media.Media3D.Vector3D(1, 0, 0); // looking straight up or down
        }
        right.Normalize();

        var up = System.Windows.Media.Media3D.Vector3D.CrossProduct(right, look);
        up.Normalize();

        // DirectionalLight3D.Direction is the direction light travels, so a light sitting up
        // and to the left of the camera travels right and down as it crosses the scene.
        var direction = look
            + right * Math.Tan(OffsetDegrees * Math.PI / 180.0)
            - up * Math.Tan(ElevationDegrees * Math.PI / 180.0);

        direction.Normalize();
        return direction;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
