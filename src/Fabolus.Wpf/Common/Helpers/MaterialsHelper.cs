using Fabolus.Core.Geometry;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.Helpers;

/// <summary>
/// Viewport materials.
///
/// These are Phong rather than Diffuse deliberately. HelixToolkit's diffuse pass
/// (psDiffuseMap.hlsl) never samples the scene lights - it fakes shading with
/// clamp(0.5 + 0.5 * abs(dot(viewDir, normal)), 0, 1). The multiplier only spans 0.5 to
/// 1.0, so a material caps out at half its own colour and every model lives in a quarter
/// of the available tonal range; and because of the abs(), a surface shades identically
/// whether it faces towards or away from the camera. On a mould that is the whole problem:
/// the cavity wall and the outer shell you are looking through return the same value, so
/// the shape being cast is unreadable.
///
/// The Phong pass needs actual lights, which is why ViewportControl.xaml declares them.
/// </summary>
public static class MaterialsHelper
{

    public static PhongMaterial CreateMaterial(Color4 color, bool enableVertexColor = false) {
        return new PhongMaterial {
            DiffuseColor = color,
            AmbientColor = color * 0.3f,
            SpecularColor = new Color4(0.02f, 0.02f, 0.02f, 1.0f), // Extremely subtle specular
            SpecularShininess = 10.0f, // Very soft/broad highlights
            EmissiveColor = new Color4(0.0f, 0.0f, 0.0f, 1.0f),
            VertexColorBlendingFactor = enableVertexColor ? 1.0f : 0.0f
        };
    }

    /// <summary>
    /// Material for a scanned or mesh-processed surface: the target mesh, a mould, a split
    /// region. Everything else - manipulators, air channel tubes, the grid - is generated
    /// geometry whose smooth normals are correct and should not use this.
    ///
    /// Vertex normals reach the GPU from MeshLib's computePerVertNormals, an area-weighted
    /// one-ring average with no crease threshold, and these meshes are full of real creases.
    /// It is worse on moulds than on the boli: 9.7% of mould_test's triangle corners are
    /// shaded with a normal more than 45 degrees off the facet they belong to (5.1% for the
    /// boli), because a mould mixes 1000+ mm2 flat wall facets with sub-mm2 cavity facets.
    /// At 1% of its vertices the largest incident face is over 24,000 times the area of the
    /// smallest, so the area-weighted average there is effectively just the wall's normal.
    /// The visible result is a gradient smeared across walls that are actually flat and
    /// rounded-off box corners.
    ///
    /// EnableFlatShading makes the pixel shader recover the true facet normal from
    /// screen-space derivatives, so flat walls read flat and creases stay sharp without any
    /// geometry changing. It also fixes two-sided surfaces: cross(ddy(wp), ddx(wp)) has no
    /// access to winding, so the normal always faces the camera and the far wall of a
    /// transparent mould stays lit instead of going black.
    /// </summary>
    public static PhongMaterial CreateSurfaceMaterial(Color4 color, bool enableVertexColor = false) {
        var material = CreateMaterial(color, enableVertexColor);
        material.EnableFlatShading = true;
        return material;
    }

}

/// <summary>
/// Colours carried over verbatim from the DiffuseMaterials entries these replaced, so
/// nothing changes hue or transparency. The scRGB values are the arguments HelixToolkit
/// passes to its own ToColor for the same named material.
/// </summary>
public static class SkinColours
{
    public static Color4 Gray => PhongMaterials.ToColor(0.254902, 0.254902, 0.254902);
    public static Color4 LightGray => PhongMaterials.ToColor(0.682353, 0.682353, 0.682353);
    public static Color4 Ruby => PhongMaterials.ToColor(0.61424, 0.04136, 0.04136, 0.55);
    public static Color4 Emerald => PhongMaterials.ToColor(0.07568, 0.61424, 0.07568, 0.55);
    public static Color4 Pearl => PhongMaterials.ToColor(1.0, 0.829, 0.829, 0.922);
    public static Color4 Orange => PhongMaterials.ToColor(0.992157, 0.513726, 0.0);

    // DiffuseMaterials took these straight from the named palette rather than via
    // ToColor, so they are the raw byte colours, not scRGB conversions.
    public static Color4 Green => new(0.0f, 0.501961f, 0.0f, 1.0f);       // 0,128,0
    public static Color4 Red => new(1.0f, 0.0f, 0.0f, 1.0f);              // 255,0,0
    public static Color4 SkyBlue => new(0.529412f, 0.807843f, 0.921569f, 1.0f); // 135,206,235
}

/// <summary>
/// Ready-made viewport skins. Use these rather than calling <see cref="MaterialsHelper"/>
/// directly, so the flat-vs-smooth decision is made once, here, instead of at every site.
///
/// The split is by what the geometry IS, not what colour it is. Anything that came out of a
/// scan or a mesh operation goes under <see cref="Surface"/>; anything the app generated
/// itself goes under <see cref="Primitive"/>. See
/// <see cref="MaterialsHelper.CreateSurfaceMaterial"/> for why that distinction matters.
///
/// Each property hands back a fresh instance. Cache it in a field if you are going to reuse
/// it, rather than calling per frame.
/// </summary>
public static class Skins
{
    /// <summary>
    /// Scanned or mesh-processed surfaces: the target mesh, a mould, a split region. Flat
    /// shaded, because their vertex normals are averaged across real creases.
    /// </summary>
    public static class Surface
    {
        public static PhongMaterial Gray => MaterialsHelper.CreateSurfaceMaterial(SkinColours.Gray);
        public static PhongMaterial Ruby => MaterialsHelper.CreateSurfaceMaterial(SkinColours.Ruby);
        public static PhongMaterial Emerald => MaterialsHelper.CreateSurfaceMaterial(SkinColours.Emerald);
        public static PhongMaterial Orange => MaterialsHelper.CreateSurfaceMaterial(SkinColours.Orange);
        public static PhongMaterial SkyBlue => MaterialsHelper.CreateSurfaceMaterial(SkinColours.SkyBlue);
    }

    /// <summary>
    /// Geometry the app generated: air channel tubes, manipulator handles, markers. Their
    /// smooth normals are correct, so these are left smooth shaded.
    /// </summary>
    public static class Primitive
    {
        public static PhongMaterial Emerald => MaterialsHelper.CreateMaterial(SkinColours.Emerald);
        public static PhongMaterial Pearl => MaterialsHelper.CreateMaterial(SkinColours.Pearl);
    }
}
