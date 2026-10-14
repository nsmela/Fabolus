namespace Fabolus.Wpf.Features.Viewport;

/// <summary>
/// How the viewport rasterises mesh visuals. Cycled by the wireframe button on the viewport overlay.
/// </summary>
public enum WireframeMode {
    /// <summary>Solid surfaces only. The default.</summary>
    None,

    /// <summary>Solid surfaces with the triangle edges drawn over them.</summary>
    Overlay,

    /// <summary>Triangle edges only, with no filled surfaces.</summary>
    Only
}
