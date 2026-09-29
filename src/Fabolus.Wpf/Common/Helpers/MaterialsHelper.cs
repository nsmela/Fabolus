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
    /// Material for the one thing the user has selected, lifted so it is unmistakably the
    /// brightest object in the scene.
    /// </summary>
    /// <remarks>
    /// The lift is partly emissive rather than only a lighter diffuse colour, because diffuse
    /// alone is at the mercy of the lighting: the same decal is bright on the lit side of a bolus
    /// and dark on the shadowed side, so a selected decal that happened to be facing away would
    /// read as the dimmest thing on screen. Emissive puts a floor under it that no amount of
    /// turning can take away.
    /// </remarks>
    public static PhongMaterial CreateHighlightMaterial(Color4 color) {
        var material = CreateMaterial(color);

        material.EmissiveColor = new Color4(
            color.Red * HighlightGlow,
            color.Green * HighlightGlow,
            color.Blue * HighlightGlow,
            1.0f);

        return material;
    }

    // Enough to keep the selected decal reading as lit from within, without washing the glyphs
    // out to a flat silhouette that loses its own shape.
    private const float HighlightGlow = 0.45f;

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
    public static Color4 Cyan => new(0.047f, 0.639f, 0.706f, 0.75f);     // #0CA3B4 (Steel Cyan Accent)
    public static Color4 TranslucentCyan => new(0.047f, 0.639f, 0.706f, 0.40f); // Translucent Cyan for preset markers
    public static Color4 Amber => new(0.941f, 0.604f, 0.047f, 1.0f);     // #F09A0C (Amber Rotate Handle)
    public static Color4 TranslucentAmber => new(0.941f, 0.604f, 0.047f, 0.50f); // Translucent Amber for hovered preset markers
    public static Color4 TranslucentGray => new(0.60f, 0.60f, 0.60f, 0.40f);     // Translucent Gray for mould overlay

    /// <summary>
    /// <see cref="Emerald"/> and <see cref="Ruby"/> held back, for the decals that are not the
    /// selected one. Enough hue survives to still tell an emboss from an engrave; the point is
    /// only that they stop competing with the one the user is working on.
    /// </summary>
    public static Color4 MutedEmerald => Muted(Emerald);
    public static Color4 MutedRuby => Muted(Ruby);

    /// <summary>
    /// <see cref="Emerald"/> and <see cref="Ruby"/> lifted, for the decal that is selected. Paired
    /// with <see cref="MaterialsHelper.CreateHighlightMaterial"/>, which adds the glow.
    /// </summary>
    public static Color4 BrightEmerald => Brightened(Emerald);
    public static Color4 BrightRuby => Brightened(Ruby);

    // Part way to white, and most of the way to opaque. The opacity does as much work as the
    // colour: these prisms sit a fraction of a millimetre off the surface, so a translucent one
    // is always showing some of the grey mesh through it and can never look fully saturated.
    private const float BrightLift = 0.30f;
    private const float BrightAlpha = 0.85f;

    private static Color4 Brightened(Color4 colour) => new(
        colour.Red + ((1f - colour.Red) * BrightLift),
        colour.Green + ((1f - colour.Green) * BrightLift),
        colour.Blue + ((1f - colour.Blue) * BrightLift),
        BrightAlpha);

    // Held back mostly by desaturation, with opacity barely touched. Taking the alpha down far
    // enough to read as "dimmed" also takes these below the lit grey mesh behind them, and an
    // unselected decal you cannot find is worse than one that fails to stand out - the selected
    // decal has the cyan box as well, so this only has to be the quieter of the two, not faint.
    // Desaturating is also what survives the lighting: a decal on the shadowed side of a bolus is
    // already dark, so darker-vs-lighter alone would say nothing there.
    private const float MutedBlend = 0.40f;
    private const float MutedGrey = 0.42f;
    private const float MutedAlpha = 0.48f;

    private static Color4 Muted(Color4 colour) => new(
        (colour.Red * (1f - MutedBlend)) + (MutedGrey * MutedBlend),
        (colour.Green * (1f - MutedBlend)) + (MutedGrey * MutedBlend),
        (colour.Blue * (1f - MutedBlend)) + (MutedGrey * MutedBlend),
        MutedAlpha);
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
        public static PhongMaterial TranslucentGray => MaterialsHelper.CreateSurfaceMaterial(SkinColours.TranslucentGray);
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
        public static PhongMaterial Ruby => MaterialsHelper.CreateMaterial(SkinColours.Ruby);
        public static PhongMaterial MutedEmerald => MaterialsHelper.CreateMaterial(SkinColours.MutedEmerald);
        public static PhongMaterial MutedRuby => MaterialsHelper.CreateMaterial(SkinColours.MutedRuby);
        public static PhongMaterial BrightEmerald => MaterialsHelper.CreateHighlightMaterial(SkinColours.BrightEmerald);
        public static PhongMaterial BrightRuby => MaterialsHelper.CreateHighlightMaterial(SkinColours.BrightRuby);
        public static PhongMaterial Orange => MaterialsHelper.CreateMaterial(SkinColours.Orange);
        public static PhongMaterial Pearl => MaterialsHelper.CreateMaterial(SkinColours.Pearl);
        public static PhongMaterial Cyan => MaterialsHelper.CreateMaterial(SkinColours.Cyan);
        public static PhongMaterial TranslucentCyan => MaterialsHelper.CreateMaterial(SkinColours.TranslucentCyan);
        public static PhongMaterial Amber => MaterialsHelper.CreateMaterial(SkinColours.Amber);
        public static PhongMaterial TranslucentAmber => MaterialsHelper.CreateMaterial(SkinColours.TranslucentAmber);
    }
}
