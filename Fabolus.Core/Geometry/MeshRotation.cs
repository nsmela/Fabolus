namespace Fabolus.Core.Geometry;

/// <summary>
/// Represents a single rotation operation applied to a mesh.
/// </summary>
public sealed record MeshRotation(
    double AngleRadians,
    double AxisX,
    double AxisY,
    double AxisZ,
    double CenterX,
    double CenterY,
    double CenterZ
)
{
    /// <summary>
    /// When this rotation was applied.
    /// </summary>
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;
}
