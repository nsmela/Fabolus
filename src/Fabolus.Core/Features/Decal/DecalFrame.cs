using System;
using System.Numerics;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Represents a local tangent coordinate frame on a 3D surface:
/// - Origin: Anchor hit point.
/// - U: Tangent vector along the text baseline (width).
/// - V: Bitangent vector pointing upwards (height).
/// - N: Surface outward unit normal.
/// </summary>
public sealed record DecalFrame(System.Numerics.Vector3 Origin, System.Numerics.Vector3 U, System.Numerics.Vector3 V, System.Numerics.Vector3 N)
{
    private const float MinNormalLengthSquared = 1e-6f;
    private const float ZAlignmentThreshold = 0.85f;
    private const float MinTangentLengthSquared = 1e-6f;
    private const float MinRotationDegrees = 1e-4f;

    public static DecalFrame FromHit(System.Numerics.Vector3 anchor, System.Numerics.Vector3 normal, float rotationDeg = 0f)
    {
        var n = System.Numerics.Vector3.Normalize(normal);
        if (n.LengthSquared() < MinNormalLengthSquared)
            n = System.Numerics.Vector3.UnitZ;

        System.Numerics.Vector3 u;
        if (MathF.Abs(System.Numerics.Vector3.Dot(n, System.Numerics.Vector3.UnitZ)) > ZAlignmentThreshold)
        {
            u = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitY, n));
        }
        else
        {
            u = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitZ, n));
        }

        if (u.LengthSquared() < MinTangentLengthSquared)
        {
            u = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitX, n));
        }

        var v = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(n, u));

        if (MathF.Abs(rotationDeg) > MinRotationDegrees)
        {
            var rad = float.DegreesToRadians(rotationDeg);
            var rot = Quaternion.CreateFromAxisAngle(n, rad);
            u = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Transform(u, rot));
            v = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Transform(v, rot));
        }

        return new DecalFrame(anchor, u, v, n);
    }

    /// <summary>
    /// Transforms local (u, v, z) decal coordinates to world 3D space.
    /// </summary>
    public System.Numerics.Vector3 ToWorld(float u, float v, float z) =>
        Origin + u * U + v * V + z * N;

    /// <summary>
    /// Transforms a world point into local (u, v, z) coordinates relative to this frame.
    /// </summary>
    public System.Numerics.Vector3 ToLocal(System.Numerics.Vector3 worldPoint)
    {
        var diff = worldPoint - Origin;
        return new System.Numerics.Vector3(
            System.Numerics.Vector3.Dot(diff, U),
            System.Numerics.Vector3.Dot(diff, V),
            System.Numerics.Vector3.Dot(diff, N));
    }
}
