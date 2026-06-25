using System.Windows;

namespace Fabolus.Wpf.Common;

/// <summary>
/// To ensure the DataContext is disposed when the FrameworkElement is unloaded.
/// </summary>
public static class DataContextDisposal {
    public static readonly DependencyProperty AutoDisposeProperty =
        DependencyProperty.RegisterAttached(
            "AutoDispose",
            typeof(bool),
            typeof(DataContextDisposal),
            new PropertyMetadata(false, OnAutoDisposeChanged));

    public static void SetAutoDispose(DependencyObject element, bool value) =>
        element.SetValue(AutoDisposeProperty, value);

    public static bool GetAutoDispose(DependencyObject element) =>
        (bool)element.GetValue(AutoDisposeProperty);

    private static void OnAutoDisposeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FrameworkElement fe) {
            if ((bool)e.NewValue) { fe.Unloaded += OnUnloaded; }
            else { fe.Unloaded -= OnUnloaded; }
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement fe && fe.DataContext is IDisposable disposable) {
            disposable.Dispose();
            // optional: clear DataContext so bindings break
            fe.DataContext = null;
        }
    }
}
