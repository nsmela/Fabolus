using System.Windows;

namespace Fabolus.Wpf.Features.Viewport;

/// <summary>
/// Markers a scene manager puts on the visuals it hands to the viewport, so the viewport can tell
/// the user's geometry apart from the furniture drawn around it.
/// </summary>
public static class SceneVisual {

    /// <summary>
    /// Marks a visual as the user's own geometry - the meshes, mould and channels that end up in
    /// the exported result. Viewport-wide display modes such as the wireframe toggle apply only
    /// to these.
    /// <para>
    /// Opt-in rather than opt-out on purpose: grids, cut planes and drag handles are furniture,
    /// and HelixToolkit's manipulators derive from MeshGeometryModel3D, so anything keyed off the
    /// visual's type alone sweeps them up too. Leaving the marker off is always the safe default.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty IsModelGeometryProperty =
        DependencyProperty.RegisterAttached(
            "IsModelGeometry",
            typeof(bool),
            typeof(SceneVisual),
            new PropertyMetadata(false));

    public static void SetIsModelGeometry(DependencyObject element, bool value) =>
        element.SetValue(IsModelGeometryProperty, value);

    public static bool GetIsModelGeometry(DependencyObject element) =>
        (bool)element.GetValue(IsModelGeometryProperty);
}
