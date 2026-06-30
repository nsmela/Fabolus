using System.Numerics;
using Fabolus.Core.Common;

namespace Fabolus.Core.Features.Overhangs;

/// <summary>
/// A validated, unit-length direction that overhanging surfaces face toward.
/// For traditional 3D printing this is "down" (<c>-Z</c>), the direction gravity
/// pulls unsupported material. Constructed only through <see cref="Create"/> so an
/// instance can never hold a zero or denormalized vector.
/// </summary>
public sealed record OverhangDirection {
    private const float Epsilon = 1e-6f;

    /// <summary>The normalized direction vector.</summary>
    public Vector3 Value { get; }

    private OverhangDirection(Vector3 normalized) => Value = normalized;

    /// <summary>
    /// The default direction for traditional 3D printing: straight down (<c>-Z</c>).
    /// </summary>
    public static OverhangDirection PrintingDefault { get; } = new(-Vector3.UnitZ);

    /// <summary>
    /// The default direction for mould printing: straight up (<c>+Z</c>).
    /// </summary>
    public static OverhangDirection MouldDefault { get; } = new(Vector3.UnitZ);

    /// <summary>
    /// Creates a direction from any non-zero vector. The vector is normalized,
    /// so callers may pass <c>-Vector3.UnitZ</c>, a scaled vector, or a build-plate
    /// normal interchangeably.
    /// </summary>
    public static Result<OverhangDirection> Create(Vector3 direction) {
        var length = direction.Length();
        if (length < Epsilon)
            return new Error("Overhang.ZeroDirection", "The overhang direction must be a non-zero vector.");

        return new OverhangDirection(direction / length);
    }

    /// <summary>
    /// The angle, in degrees, between this direction and a face's outward normal.
    /// <para>
    /// 0 means the normal points exactly along the overhang direction (the most
    /// severe overhang, e.g. a flat downward-facing ceiling); 90 is a vertical wall;
    /// 180 means the normal is fully opposed (an up-facing surface that is never an
    /// overhang). Unnormalized normals are handled; a degenerate (zero) normal is
    /// reported as 180 so it is never classified as an overhang.
    /// </para>
    /// </summary>
    public float AngleToDegrees(Vector3 faceNormal) {
        var length = faceNormal.Length();
        if (length < Epsilon)
            return 180f;

        var cosine = Math.Clamp(Vector3.Dot(Value, faceNormal / length), -1f, 1f);
        return MathF.Acos(cosine) * (180f / MathF.PI);
    }
}