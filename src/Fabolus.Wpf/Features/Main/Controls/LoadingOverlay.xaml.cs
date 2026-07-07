using System.Windows;
using System.Windows.Controls;

namespace Fabolus.Wpf.Features.Main.Controls
{
    /// <summary>
    /// Indeterminate loading overlay for the 3D viewport.
    /// Bind <see cref="IsLoading"/> to a CommunityToolkit.Mvvm [ObservableProperty]:
    ///     <controls:LoadingOverlay IsLoading="{Binding IsLoading}" />
    /// Setting IsLoading true shows the overlay and starts the ring/label animations;
    /// setting it false hides it and stops them.
    /// </summary>
    public partial class LoadingOverlay : UserControl
    {
        public LoadingOverlay()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(
                nameof(IsLoading),
                typeof(bool),
                typeof(LoadingOverlay),
                new PropertyMetadata(false));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }
    }
}
