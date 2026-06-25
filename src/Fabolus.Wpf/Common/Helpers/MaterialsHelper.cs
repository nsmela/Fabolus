using Fabolus.Core.Geometry;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace Fabolus.Wpf.Common.Helpers;

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

}
