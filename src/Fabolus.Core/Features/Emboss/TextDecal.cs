using System.Numerics;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Immutable record describing a text decal placement, geometry and projection properties.
/// </summary>
public sealed record TextDecal
{
    public string Text { get; init; } = "FABOLUS";
    public EmbossOperation Operation { get; init; } = EmbossOperation.Engrave;
    public EmbossTarget Target { get; init; } = EmbossTarget.Base;
    public DecalFont Font { get; init; } = DecalFont.Sans;
    public float CapHeight { get; init; } = 6.0f;
    public float Depth { get; init; } = 0.8f;
    public float Tracking { get; init; } = 0.4f;
    public float RotationDeg { get; init; } = 0f;
    public Vector3 Anchor { get; init; } = Vector3.Zero;
    public Vector3 AnchorNormal { get; init; } = Vector3.UnitZ;
    public Guid Id { get; init; } = Guid.NewGuid();
}
