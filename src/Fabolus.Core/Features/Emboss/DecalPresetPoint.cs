using System.Numerics;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Represents a pre-calculated placement target on a mesh surface (e.g. mould contour).
/// </summary>
public sealed record DecalPresetPoint(
    string Name,
    Vector3 Position,
    Vector3 Normal,
    EmbossTarget Target = EmbossTarget.Mould
);
