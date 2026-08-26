using System.Numerics;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Represents a pre-calculated placement target on a mesh surface (e.g. mould contour).
/// </summary>
public sealed record DecalPresetPoint(
    string Name,
    Vector3 Position,
    Vector3 Normal,
    float RotationDeg = 0f,
    float AvailableSpan = 0f,
    EmbossTarget Target = EmbossTarget.Mould
)
{
    public float MouldHeight => AvailableSpan;
}
