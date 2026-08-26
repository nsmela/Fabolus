using System.Numerics;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Immutable record describing a text decal placement, geometry and projection properties.
/// </summary>
public sealed record TextDecal
{
    public const string DefaultText = "FABOLUS";
    public const EmbossOperation DefaultOperation = EmbossOperation.Engrave;
    public const EmbossTarget DefaultTarget = EmbossTarget.Base;
    public const DecalFont DefaultFont = DecalFont.Sans;
    public const float DefaultCapHeight = 6.0f;
    public const float DefaultDepth = 0.8f;
    public const float DefaultTracking = 0.4f;
    public const float DefaultRotationDeg = 0f;

    public string Text { get; init; } = DefaultText;
    public EmbossOperation Operation { get; init; } = DefaultOperation;
    public EmbossTarget Target { get; init; } = DefaultTarget;
    public DecalFont Font { get; init; } = DefaultFont;
    public float CapHeight { get; init; } = DefaultCapHeight;
    public float Depth { get; init; } = DefaultDepth;
    public float Tracking { get; init; } = DefaultTracking;
    public float RotationDeg { get; init; } = DefaultRotationDeg;
    public Vector3 Anchor { get; init; } = Vector3.Zero;
    public Vector3 AnchorNormal { get; init; } = Vector3.UnitZ;
    public Guid Id { get; init; } = Guid.NewGuid();
}
