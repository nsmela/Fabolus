using System.Numerics;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Represents a local tangent coordinate frame on a 3D surface:
/// - Origin: Anchor hit point.
/// - U: Tangent vector along the text baseline (width).
/// - V: Bitangent vector pointing upwards (height).
/// - N: Surface outward unit normal.
/// </summary>
public sealed record DecalFrame(Vector3 Origin, Vector3 U, Vector3 V, Vector3 N)
{
    public static DecalFrame FromHit(Vector3 anchor, Vector3 normal, float rotationDeg = 0f)
    {
        var n = Vector3.Normalize(normal);
        if (n.LengthSquared() < 1e-6f)
            n = Vector3.UnitZ;

        Vector3 u;
        if (MathF.Abs(Vector3.Dot(n, Vector3.UnitZ)) > 0.85f)
        {
            u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n));
        }
        else
        {
            u = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n));
        }

        if (u.LengthSquared() < 1e-6f)
        {
            u = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
        }

        var v = Vector3.Normalize(Vector3.Cross(n, u));

        if (MathF.Abs(rotationDeg) > 1e-4f)
        {
            var rad = rotationDeg * MathF.PI / 180f;
            var rot = Quaternion.CreateFromAxisAngle(n, rad);
            u = Vector3.Normalize(Vector3.Transform(u, rot));
            v = Vector3.Normalize(Vector3.Transform(v, rot));
        }

        return new DecalFrame(anchor, u, v, n);
    }

    /// <summary>
    /// Transforms local (u, v, z) decal coordinates to world 3D space.
    /// </summary>
    public Vector3 ToWorld(float u, float v, float z) =>
        Origin + u * U + v * V + z * N;

    /// <summary>
    /// Transforms a world point into local (u, v, z) coordinates relative to this frame.
    /// </summary>
    public Vector3 ToLocal(Vector3 worldPoint)
    {
        var diff = worldPoint - Origin;
        return new Vector3(
            Vector3.Dot(diff, U),
            Vector3.Dot(diff, V),
            Vector3.Dot(diff, N));
    }
}
